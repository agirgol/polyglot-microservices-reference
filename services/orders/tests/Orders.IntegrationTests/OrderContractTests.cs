using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Shouldly;
using Xunit;

namespace Orders.IntegrationTests;

/// <summary>
/// What a caller gets, and what the rest of the estate is told.
/// </summary>
/// <remarks>
/// <para>
/// The headers asserted here are the contract the Java consumer reads. They are
/// checked because they were wrong once: Wolverine's default put the trace
/// context under <c>parent-id</c> and the .NET type name under
/// <c>message-type</c>, and nothing failed — the consumer simply started its own
/// trace and the two halves of every request lived apart. A test is the only
/// thing that notices that.
/// </para>
/// </remarks>
public sealed class OrderContractTests(OrdersUnderTest service) : IClassFixture<OrdersUnderTest>
{
    private static readonly TimeSpan WaitForDelivery = TimeSpan.FromSeconds(30);

    /// <summary>The test's own token, so a hung container does not hang the run.</summary>
    private static CancellationToken Stopping => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_order_is_persisted_and_announced()
    {
        var client = service.CreateClient();

        var response = await client.PostAsJsonAsync("/orders", new
        {
            customerId = "acme",
            currency = "TRY",
            lines = new[] { new { sku = "WIDGET-1", quantity = 3, unitPrice = 19.90m } },
        }, Stopping);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var placed = await response.Content.ReadFromJsonAsync<JsonElement>(Stopping);
        var orderId = placed.GetProperty("orderId").GetString()!;

        // Read back through the API rather than the database: the round trip
        // through NUMERIC(18,4) is part of what is being checked, and so is the
        // scale surviving it.
        var readBack = await client.GetFromJsonAsync<JsonElement>($"/orders/{orderId}", Stopping);
        readBack.GetProperty("total").GetDecimal().ShouldBe(59.70m);
        readBack.GetProperty("total").GetRawText().ShouldBe("59.7000");
        readBack.GetProperty("status").GetString().ShouldBe("Placed");

        var message = await Consume("orders.placed", orderId);

        Header(message, "event-type").ShouldBe("order.placed");
        Header(message, "content-type").ShouldBe("application/json");

        // W3C trace context, under the name the standard gives it.
        var traceParent = Header(message, "traceparent");
        traceParent.ShouldNotBeNull();
        traceParent.ShouldStartWith("00-");
        traceParent.Split('-').Length.ShouldBe(4);

        // No .NET type name anywhere on the wire.
        message.Message.Headers.Select(h => Encoding.UTF8.GetString(h.GetValueBytes()))
            .ShouldNotContain(value => value.Contains("Orders.Domain", StringComparison.Ordinal));

        var body = JsonDocument.Parse(message.Message.Value).RootElement;
        body.GetProperty("customerId").GetString().ShouldBe("acme");
        body.GetProperty("total").GetDecimal().ShouldBe(59.70m);
        body.GetProperty("lineCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Confirming_twice_announces_once()
    {
        var client = service.CreateClient();

        var placed = await (await client.PostAsJsonAsync("/orders", new
        {
            customerId = "twice",
            currency = "TRY",
            lines = new[] { new { sku = "ONE-EVENT", quantity = 1, unitPrice = 5.00m } },
        }, Stopping)).Content.ReadFromJsonAsync<JsonElement>(Stopping);
        var orderId = placed.GetProperty("orderId").GetString()!;

        (await client.PostAsync($"/orders/{orderId}/confirmation", null, Stopping))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // A retry. The caller asked for a state and gets it, both times.
        (await client.PostAsync($"/orders/{orderId}/confirmation", null, Stopping))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var announcements = await ConsumeAll("orders.confirmed", orderId);

        // Once, because the second call changed nothing. The consumer would
        // deduplicate a repeat, but an event claiming a confirmation at a time
        // the order was not confirmed at is a false statement either way.
        announcements.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_unbalanced_request_is_refused_with_something_to_act_on()
    {
        var client = service.CreateClient();

        var response = await client.PostAsJsonAsync("/orders", new
        {
            customerId = "acme",
            currency = "TRY",
            lines = Array.Empty<object>(),
        }, Stopping);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Stopping);
        problem.GetProperty("detail").GetString()
            .ShouldNotBeNull()
            .ShouldContain("at least one line");
    }

    private static string? Header(ConsumeResult<string, byte[]> message, string name) =>
        message.Message.Headers.TryGetLastBytes(name, out var value)
            ? Encoding.UTF8.GetString(value)
            : null;

    private async Task<ConsumeResult<string, byte[]>> Consume(string topic, string orderId)
    {
        var messages = await ConsumeAll(topic, orderId);
        messages.ShouldNotBeEmpty($"nothing for order {orderId} arrived on {topic}");
        return messages[0];
    }

    /// <summary>
    /// Reads the topic from the beginning, keeping only what belongs to one order.
    /// </summary>
    /// <remarks>
    /// A fresh group id per call, so each test sees the whole topic rather than
    /// whatever offset a previous one left behind. Filtering by order id is what
    /// makes the tests independent of each other while sharing a broker.
    /// </remarks>
    private async Task<List<ConsumeResult<string, byte[]>>> ConsumeAll(string topic, string orderId)
    {
        using var consumer = new ConsumerBuilder<string, byte[]>(new ConsumerConfig
        {
            BootstrapServers = service.KafkaBootstrapServers,
            GroupId = $"test-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();

        consumer.Subscribe(topic);

        var found = new List<ConsumeResult<string, byte[]>>();
        var deadline = DateTime.UtcNow + WaitForDelivery;
        var quietSince = (DateTime?)null;

        while (DateTime.UtcNow < deadline)
        {
            ConsumeResult<string, byte[]>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromSeconds(2));
            }
            catch (ConsumeException notYet)
                when (notYet.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                // The topic does not exist until something publishes to it, and
                // publishing happens on the outbox's sweep rather than during
                // the request. Subscribing first is the point of the test —
                // waiting for the topic to appear is not a workaround, it is
                // what any consumer started before its producer has to do.
                continue;
            }

            if (result?.Message is null)
            {
                // The outbox forwards on a sweep, so silence early means "not
                // yet" and silence after a hit means "that was all of them".
                if (found.Count > 0 && quietSince is null)
                {
                    quietSince = DateTime.UtcNow;
                }

                if (quietSince is not null && DateTime.UtcNow - quietSince > TimeSpan.FromSeconds(4))
                {
                    break;
                }

                continue;
            }

            if (Encoding.UTF8.GetString(result.Message.Value).Contains(orderId, StringComparison.Ordinal))
            {
                found.Add(result);
                quietSince = null;
            }
        }

        consumer.Close();
        return found;
    }
}

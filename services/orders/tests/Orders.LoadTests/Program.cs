using System.Net.Http.Json;
using System.Text.Json;
using NBomber.Contracts.Stats;
using NBomber.CSharp;

/*
 * A load profile for the orders service.
 *
 * Pointed at the service directly, not at the gateway. The gateway rate-limits
 * to 100 requests per 10 seconds per client, so a load test through it measures
 * the limiter and nothing else — which is the limiter working, and not a fact
 * about how much the service can do. Run against the gateway to see the 429s
 * arrive on schedule; run against the service to see what it costs to place an
 * order.
 *
 *     docker compose up -d
 *     dotnet run --project services/orders/tests/Orders.LoadTests
 *
 * ORDERS_URL overrides the target.
 */

var target = Environment.GetEnvironmentVariable("ORDERS_URL") ?? "http://localhost:8081";
var http = new HttpClient { BaseAddress = new Uri(target), Timeout = TimeSpan.FromSeconds(30) };

Console.WriteLine($"Target: {target}");
await WaitUntilAnswering(http);

// Seeded once so the read scenario has something to read that is not the thing
// the write scenario just created — otherwise the two share a cache line and a
// page, and the read numbers flatter themselves.
var seededOrderId = await PlaceOne(http);
Console.WriteLine($"Seeded order for the read scenario: {seededOrderId}");

/*
 * The write path: an insert, an order line, and an outbox envelope, in one
 * transaction. This is the expensive one and the one worth watching — every
 * order placed is also a message durably stored.
 */
var placeOrder = Scenario.Create("place_order", async context =>
{
    var response = await http.PostAsJsonAsync("/orders", new
    {
        customerId = $"load-{context.ScenarioInfo.InstanceNumber}",
        currency = "TRY",
        lines = new[] { new { sku = "LOAD-1", quantity = 2, unitPrice = 12.50m } },
    });

    return response.IsSuccessStatusCode
        ? Response.Ok(statusCode: ((int)response.StatusCode).ToString())
        : Response.Fail(statusCode: ((int)response.StatusCode).ToString());
})
.WithWarmUpDuration(TimeSpan.FromSeconds(5))
.WithLoadSimulations(
    // A rate, not a thread count. Injecting arrivals is what a service actually
    // experiences; holding N threads busy measures how fast the test can spin.
    Simulation.Inject(rate: 20, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)),
    Simulation.Inject(rate: 60, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));

/* The read path: one row and its lines, no writes, no outbox. */
var readOrder = Scenario.Create("read_order", async context =>
{
    var response = await http.GetAsync($"/orders/{seededOrderId}");

    return response.IsSuccessStatusCode
        ? Response.Ok(statusCode: ((int)response.StatusCode).ToString())
        : Response.Fail(statusCode: ((int)response.StatusCode).ToString());
})
.WithWarmUpDuration(TimeSpan.FromSeconds(5))
.WithLoadSimulations(
    Simulation.Inject(rate: 100, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(30)));

NBomberRunner
    .RegisterScenarios(placeOrder, readOrder)
    .WithReportFolder("load-reports")
    .WithReportFormats(ReportFormat.Html, ReportFormat.Md, ReportFormat.Csv)
    .Run();

return;

/// <summary>
/// Waits for the service to answer at all, before NBomber warms each scenario.
/// </summary>
/// <remarks>
/// Warming through /health only was a mistake worth recording: it jitted the
/// health endpoint and nothing else, so the read scenario paid its first-call
/// cost inside the measured window and reported a p99 of 80 ms against a p50 of
/// 1.4 ms. Each scenario now warms its own path, which is what
/// WithWarmUpDuration is for.
/// </remarks>
static async Task WaitUntilAnswering(HttpClient http)
{
    using var response = await http.GetAsync("/health");
    response.EnsureSuccessStatusCode();
}

static async Task<string> PlaceOne(HttpClient http)
{
    var response = await http.PostAsJsonAsync("/orders", new
    {
        customerId = "load-seed",
        currency = "TRY",
        lines = new[] { new { sku = "SEED", quantity = 1, unitPrice = 1.00m } },
    });
    response.EnsureSuccessStatusCode();

    var placed = await response.Content.ReadFromJsonAsync<JsonElement>();
    return placed.GetProperty("orderId").GetString()!;
}

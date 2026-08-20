using JasperFx.CommandLine;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Xunit;

namespace Orders.IntegrationTests;

/// <summary>
/// The orders service, running against a real Postgres and a real Kafka.
/// </summary>
/// <remarks>
/// <para>
/// Containers rather than fakes, because what is being tested is not the code's
/// behaviour against an interface — it is whether a NUMERIC column round-trips a
/// decimal, whether the outbox writes where it says, and what actually lands on
/// a topic. A fake broker agrees with whatever the test author believed.
/// </para>
/// <para>
/// One fixture per class, so the containers start once. They take a few seconds
/// each and the tests take milliseconds.
/// </para>
/// </remarks>
public sealed class OrdersUnderTest : WebApplicationFactory<Program>, IAsyncLifetime
{
    static OrdersUnderTest()
    {
        /*
         * Tells JasperFx that something other than its command line is starting
         * the host.
         *
         * Program.cs only calls RunJasperFxCommands when it was given arguments,
         * and a test gives it none — so this looks like it should not matter.
         * It does: JasperFx installs itself when the assembly is used at all,
         * and without this flag WebApplicationFactory ends up with a host whose
         * services resolve and whose web server was never started. CreateClient
         * then fails with "the server has not been started", several layers away
         * from anything that mentions JasperFx.
         */
        JasperFxEnvironment.AutoStartHost = true;
    }

    // The image goes to the constructor: Testcontainers 4.14 deprecated the
    // parameterless builders, on the grounds that a test should say which
    // version it ran against rather than inherit whatever the library defaulted
    // to that release. These match compose.yaml.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("orders")
        .WithUsername("orders")
        .WithPassword("orders")
        .Build();

    /*
     * Confluent's build, where compose runs apache/kafka. The Testcontainers
     * module could not bring 4.3.0 up under either vendor setting — the
     * container starts and dies on an empty `advertised.listeners`, because
     * working out the advertised address means starting the container, reading
     * the mapped port, and rewriting the config, and the module only does that
     * reliably for the image it was built around.
     *
     * The difference is packaging, not protocol: same broker, same wire format.
     * What these tests assert is the shape of *our* messages — the headers, the
     * body, how many were published — and none of that is a property of who
     * built the image. The place where the broker version does matter is
     * compose, and that is pinned to what gets deployed.
     */
    private readonly KafkaContainer _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.8.0")
        .WithKRaft()
        .Build();

    public string KafkaBootstrapServers => _kafka.GetBootstrapAddress();

    public async ValueTask InitializeAsync()
    {
        // Together: two container pulls and two starts is most of this suite's
        // wall clock, and they have nothing to do with each other.
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _kafka.DisposeAsync().AsTask());
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Orders"] = _postgres.GetConnectionString(),
                ["Kafka:BootstrapServers"] = _kafka.GetBootstrapAddress(),

                // The schema comes from the migrations, not from a test helper
                // that creates tables. A test passing against a schema the
                // migrations never produced proves nothing about deployment.
                ["Migrations:ApplyOnStartup"] = "true",

                // No telemetry endpoint: the service skips exporting rather
                // than retrying against nothing for the length of the suite.
                ["Otlp:Endpoint"] = "",
            }));

        return base.CreateHost(builder);
    }
}

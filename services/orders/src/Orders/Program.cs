using Microsoft.EntityFrameworkCore;
using Orders;
using Orders.Api;
using Orders.Persistence;
using Wolverine;
using Orders.Domain;
using Wolverine.EntityFrameworkCore;
using Wolverine.Kafka;
using Wolverine.Postgresql;

var builder = WebApplication.CreateBuilder(args);

// No default here on purpose. A service that quietly connects to whatever is
// listening on localhost is worse than one that refuses to start.
var connectionString = builder.Configuration.GetConnectionString("Orders")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Orders is not configured. Set it, or run the service through docker compose.");

// Registered through Wolverine's integration rather than plain AddDbContext.
//
// Wolverine generates handler code and inlines dependency resolution into it;
// it refuses to fall back to service location, which is what a plain
// AddDbContext forces because EF registers DbContextOptions through an opaque
// lambda factory. This registration is also what the transactional outbox will
// hook into, so the context and the outgoing messages share one transaction.
builder.Services.AddDbContextWithWolverineIntegration<OrdersDbContext>(
    options => options.UseNpgsql(connectionString));

// Injected rather than called statically, so a test can place an order at a
// chosen instant instead of at whatever time the test happens to run.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddTelemetry(builder.Configuration, "orders");
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

var kafkaBootstrapServers = builder.Configuration["Kafka:BootstrapServers"]
    ?? throw new InvalidOperationException(
        "Kafka:BootstrapServers is not configured. Set it, or run the service through docker compose.");

builder.Host.UseWolverine(opts =>
{
    // The outbox. Outgoing messages are written to the same Postgres the order
    // is written to, inside the same transaction, and forwarded afterwards.
    // Without this an order can commit and its event never leave — or the event
    // can leave for an order that was rolled back. Both happen under load, and
    // both are silent.
    opts.PersistMessagesWithPostgresql(connectionString, schemaName: "wolverine");
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();

    // Without this the outbox above is configured and unused. Wolverine's
    // sending endpoints are buffered in memory by default: a message is handed
    // to the transport after the transaction commits, and if the process dies
    // in between it is simply gone. The storage exists either way — what makes
    // it an outbox is that outgoing messages are written to it first.
    //
    // Discovered the way it should be: by stopping Kafka, posting an order, and
    // finding the outgoing envelopes table empty.
    opts.Policies.UseDurableOutboxOnAllSendingEndpoints();

    opts.UseKafka(kafkaBootstrapServers);

    // One topic per event type rather than one topic carrying a type header.
    // The consumer is a different runtime with a different deserialiser, and a
    // topic whose payload shape depends on a header is a contract that only
    // holds by convention.
    var wireFormat = new OrderEventKafkaMapper();
    opts.PublishMessage<OrderPlaced>().ToKafkaTopic("orders.placed").UseInterop(wireFormat);
    opts.PublishMessage<OrderConfirmed>().ToKafkaTopic("orders.confirmed").UseInterop(wireFormat);
    opts.PublishMessage<OrderCancelled>().ToKafkaTopic("orders.cancelled").UseInterop(wireFormat);
});

var app = builder.Build();

/*
 * Applying migrations from the service is a convenience, not a pattern, and the
 * flag is how that stays visible. It is off unless something turns it on, and
 * the only thing that turns it on is the compose file.
 *
 * With more than one replica this is a race: several instances start, all see
 * pending migrations, all try to apply them. EF takes an advisory lock so the
 * losers wait rather than corrupt anything, but the losers are also blocking
 * their own startup on a migration they are not running. In production this
 * belongs in a step that runs once — a job, an init container, or a migration
 * bundle — before any instance starts.
 */
if (builder.Configuration.GetValue("Migrations:ApplyOnStartup", false))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>()
        .Database.MigrateAsync();
}

app.UseExceptionHandler();
app.MapOrderEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Named so the test project can drive this application in-process.</summary>
public partial class Program;

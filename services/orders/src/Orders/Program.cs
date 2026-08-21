using Microsoft.EntityFrameworkCore;
using JasperFx;
using JasperFx.CodeGeneration;
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
/*
 * `migrate` applies the schema and exits, so a deployment can run migrations
 * once — a Kubernetes Job, an init container, a release step — instead of every
 * replica racing to do it on startup.
 *
 * It builds a host containing a DbContext and nothing else. The first version
 * of this ran after the application had been built, which meant migrating also
 * started a message bus: the Job's logs filled with a Wolverine node assuming
 * leadership and a Kafka producer retrying a broker the Job has no business
 * talking to, and the pod never exited. A migration needs a connection string
 * and a schema, not an application.
 */
if (args is ["migrate"])
{
    var migrations = Host.CreateApplicationBuilder(args);
    migrations.Services.AddDbContext<OrdersDbContext>(o => o.UseNpgsql(connectionString));

    using var scope = migrations.Build().Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();
    return;
}

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

    /*
     * Handlers are generated code either way. The question is when.
     *
     * Dynamic compiles them on first use. Static loads types written to disk by
     * `dotnet run -- codegen write` and committed. Measured on the same machine
     * against the same database, first request after start: 1.098 s dynamic,
     * 0.430 s static. The second request is 12 ms either way.
     *
     * The 430 ms that remains is not Roslyn — it is EF building its model, the
     * connection pool opening, and the request pipeline being jitted. See
     * ADR 0010.
     *
     * Dynamic in development, because generated code that has to be regenerated
     * by hand after every handler change is generated code that will be stale.
     */
    opts.CodeGeneration.TypeLoadMode = builder.Environment.IsDevelopment()
        ? TypeLoadMode.Auto
        : TypeLoadMode.Static;

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

/*
 * Commands only when asked for one.
 *
 * `dotnet run -- codegen write` is what writes the handler types this service
 * loads in production, and RunJasperFxCommands is what makes it available. It
 * also takes over the host lifecycle, and WebApplicationFactory drives the
 * application by running this file and capturing the host that app.Run() would
 * have started — so routing every start through it leaves the integration tests
 * with "the server has not been started".
 *
 * Splitting on the arguments keeps both: the CLI when there is a command, the
 * ordinary path when there is not.
 */
if (args.Length > 0)
{
    // Through Environment.ExitCode rather than by returning it. Returning an
    // int from top-level statements makes the generated entry point
    // `Task<int>`, and the host resolver WebApplicationFactory uses only
    // recognises `void` and `Task` — so a failing codegen command would still
    // report success, or the integration tests would not start. This keeps both.
    Environment.ExitCode = await app.RunJasperFxCommands(args);
    return;
}

app.Run();

/// <summary>Named so the test project can drive this application in-process.</summary>
public partial class Program;

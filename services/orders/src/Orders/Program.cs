using Microsoft.EntityFrameworkCore;
using Orders.Api;
using Orders.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;

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

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// Handler discovery only, for now. The durable outbox and the Kafka transport
// arrive with the messaging milestone; the handlers are already shaped for them
// because they return their events rather than sending them.
builder.Host.UseWolverine();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOrderEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>Named so the test project can drive this application in-process.</summary>
public partial class Program;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Orders.Persistence;

/// <summary>
/// Builds a context for `dotnet ef`, which needs the model but not a database.
/// </summary>
/// <remarks>
/// <para>
/// The application refuses to start without a configured connection string, on
/// purpose. Generating a migration is not starting the application: nothing
/// connects, the provider is only there so EF knows which SQL dialect to write.
/// Hence a placeholder rather than a real address — and hence no risk that
/// `dotnet ef database update` silently targets a developer's own Postgres.
/// </para>
/// </remarks>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
    public OrdersDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OrdersDbContext>()
            .UseNpgsql("Host=design-time-only;Database=orders")
            .Options;

        return new OrdersDbContext(options);
    }
}

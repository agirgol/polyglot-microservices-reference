using Microsoft.EntityFrameworkCore;
using Orders.Domain;

namespace Orders.Persistence;

/// <summary>
/// The orders database.
/// </summary>
/// <remarks>
/// <para>
/// The mapping is configured here rather than with attributes on the domain
/// types. <see cref="Order"/> has rules about what may happen to it and knows
/// nothing about tables; keeping the persistence concerns out of it is what
/// lets those rules be tested without a database.
/// </para>
/// </remarks>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var order = model.Entity<Order>();

        order.ToTable("orders");
        order.HasKey(o => o.Id);

        order.Property(o => o.CustomerId).HasMaxLength(64).IsRequired();
        order.Property(o => o.Currency).HasMaxLength(3).IsRequired().IsFixedLength(false);
        order.Property(o => o.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        order.Property(o => o.PlacedAt).IsRequired();
        order.Property(o => o.CancellationReason).HasMaxLength(256);

        // Derived from the lines. Mapping it would create a column that can
        // disagree with the rows it was computed from.
        order.Ignore(o => o.Total);

        order.HasIndex(o => o.CustomerId);
        order.HasIndex(o => o.Status);

        // Optimistic concurrency through Postgres's own row version. Two
        // requests confirming and cancelling the same order at once would
        // otherwise both read Placed and both win.
        //
        // A `uint` marked IsRowVersion is how the Npgsql provider is told to use
        // `xmin`, the transaction id Postgres already stamps on every row. It
        // costs no schema and no write path — unlike a version column the
        // application has to remember to increment — and because the column
        // already exists, no migration creates it.
        //
        // Naming the column by hand instead produces a migration that tries to
        // CREATE `xmin`, which Postgres refuses: it conflicts with a system
        // column. The provider-specific UseXminAsConcurrencyToken() that used
        // to express this was removed in Npgsql 7 in favour of the standard API.
        //
        // A shadow property, because the domain type has no business carrying
        // a persistence concern.
        order.Property<uint>("Version").IsRowVersion();

        order.OwnsMany(o => o.Lines, line =>
        {
            line.ToTable("order_lines");
            line.WithOwner().HasForeignKey("order_id");
            line.Property<int>("id");
            line.HasKey("id");

            line.Property(l => l.Sku).HasMaxLength(64).IsRequired();
            line.Property(l => l.Quantity).IsRequired();

            // NUMERIC, not double precision. The scale is 4 rather than 2
            // because a unit price is not always a displayable amount — per
            // thousand and per litre pricing both need more.
            line.Property(l => l.UnitPrice).HasColumnType("numeric(18,4)").IsRequired();
            line.Ignore(l => l.LineTotal);
        });

        // Reached through the aggregate, always. Loading an order without its
        // lines gives a Total of zero, which is a wrong number rather than a
        // missing one.
        order.Navigation(o => o.Lines).AutoInclude();
    }
}

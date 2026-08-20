using Microsoft.EntityFrameworkCore;
using Orders.Domain;
using Orders.Persistence;

namespace Orders.Features;

public sealed record PlaceOrderLine(string Sku, int Quantity, decimal UnitPrice);

public sealed record PlaceOrder(
    string CustomerId,
    string Currency,
    IReadOnlyList<PlaceOrderLine> Lines);

public sealed record OrderPlacedResult(Guid OrderId, decimal Total, string Currency);

/// <summary>
/// Places an order and announces it.
/// </summary>
/// <remarks>
/// <para>
/// The handler returns a tuple: the HTTP response, and the event to publish.
/// Wolverine treats the second as a cascading message — it is not sent by this
/// method, it is returned from it. That is what lets the send join the same
/// transaction as the insert once the outbox is turned on, and it is why
/// nothing here calls a bus.
/// </para>
/// </remarks>
public static class PlaceOrderHandler
{
    public static async Task<(OrderPlacedResult, OrderPlaced)> Handle(
        PlaceOrder command,
        OrdersDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var placedAt = clock.GetUtcNow();
        var order = Order.Place(
            Guid.CreateVersion7(),
            command.CustomerId,
            command.Currency,
            [.. command.Lines.Select(line => new OrderLine(line.Sku, line.Quantity, line.UnitPrice))],
            placedAt);

        database.Orders.Add(order);
        await database.SaveChangesAsync(cancellationToken);

        return (
            new OrderPlacedResult(order.Id, order.Total, order.Currency),
            new OrderPlaced(
                order.Id,
                order.CustomerId,
                order.Currency,
                order.Total,
                order.Lines.Count,
                order.PlacedAt));
    }
}

public sealed record GetOrder(Guid OrderId);

public sealed record OrderView(
    Guid OrderId,
    string CustomerId,
    string Currency,
    string Status,
    decimal Total,
    DateTimeOffset PlacedAt,
    IReadOnlyList<PlaceOrderLine> Lines);

public static class GetOrderHandler
{
    public static async Task<OrderView?> Handle(
        GetOrder query,
        OrdersDbContext database,
        CancellationToken cancellationToken)
    {
        // No tracking: a query has nothing to save, and a tracked read is a
        // change the next SaveChanges might decide to write.
        var order = await database.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(o => o.Id == query.OrderId, cancellationToken);

        return order is null ? null : ToView(order);
    }

    internal static OrderView ToView(Order order) => new(
        order.Id,
        order.CustomerId,
        order.Currency,
        order.Status.ToString(),
        order.Total,
        order.PlacedAt,
        [.. order.Lines.Select(l => new PlaceOrderLine(l.Sku, l.Quantity, l.UnitPrice))]);
}

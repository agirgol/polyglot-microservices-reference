using Microsoft.EntityFrameworkCore;
using Orders.Domain;
using Orders.Persistence;

namespace Orders.Features;

public sealed record ConfirmOrder(Guid OrderId);

public sealed record CancelOrder(Guid OrderId, string Reason);

/// <summary>
/// Thrown when a command names an order that was never placed.
/// </summary>
public sealed class OrderNotFoundException(Guid orderId)
    : InvalidOperationException($"No order {orderId} has been placed.")
{
    public Guid OrderId { get; } = orderId;
}

public static class ConfirmOrderHandler
{
    public static async Task<(OrderView, OrderConfirmed)> Handle(
        ConfirmOrder command,
        OrdersDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var order = await database.Orders
            .SingleOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        var confirmedAt = clock.GetUtcNow();
        order.Confirm(confirmedAt);
        await database.SaveChangesAsync(cancellationToken);

        return (
            GetOrderHandler.ToView(order),
            new OrderConfirmed(order.Id, order.CustomerId, confirmedAt));
    }
}

public static class CancelOrderHandler
{
    public static async Task<(OrderView, OrderCancelled)> Handle(
        CancelOrder command,
        OrdersDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var order = await database.Orders
            .SingleOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        var cancelledAt = clock.GetUtcNow();
        order.Cancel(command.Reason, cancelledAt);
        await database.SaveChangesAsync(cancellationToken);

        return (
            GetOrderHandler.ToView(order),
            new OrderCancelled(order.Id, order.CustomerId, command.Reason, cancelledAt));
    }
}

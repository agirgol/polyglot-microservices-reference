using Microsoft.EntityFrameworkCore;
using Orders.Domain;
using Orders.Persistence;
using Wolverine;

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

/// <remarks>
/// <para>
/// Both handlers return <see cref="OutgoingMessages"/> rather than an event,
/// and it is empty when nothing changed. A retried confirmation still answers
/// 200 with the order's state — the caller asked for a state and got it — but
/// it announces nothing, because nothing happened.
/// </para>
/// <para>
/// The consumer deduplicates anyway; delivery is at-least-once and it has to.
/// That is not a reason to publish an event saying an order was confirmed at a
/// time it was not. A safety net catching a lie does not make it true.
/// </para>
/// </remarks>
public static class ConfirmOrderHandler
{
    public static async Task<(OrderView, OutgoingMessages)> Handle(
        ConfirmOrder command,
        OrdersDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var order = await database.Orders
            .SingleOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        var confirmedAt = clock.GetUtcNow();
        var announcements = new OutgoingMessages();

        if (order.Confirm(confirmedAt))
        {
            announcements.Add(new OrderConfirmed(order.Id, order.CustomerId, confirmedAt));
        }

        await database.SaveChangesAsync(cancellationToken);

        return (GetOrderHandler.ToView(order), announcements);
    }
}

public static class CancelOrderHandler
{
    public static async Task<(OrderView, OutgoingMessages)> Handle(
        CancelOrder command,
        OrdersDbContext database,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var order = await database.Orders
            .SingleOrDefaultAsync(o => o.Id == command.OrderId, cancellationToken)
            ?? throw new OrderNotFoundException(command.OrderId);

        var cancelledAt = clock.GetUtcNow();
        var announcements = new OutgoingMessages();

        if (order.Cancel(command.Reason, cancelledAt))
        {
            announcements.Add(
                new OrderCancelled(order.Id, order.CustomerId, command.Reason, cancelledAt));
        }

        await database.SaveChangesAsync(cancellationToken);

        return (GetOrderHandler.ToView(order), announcements);
    }
}

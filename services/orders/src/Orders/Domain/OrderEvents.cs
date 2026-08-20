using Wolverine.Attributes;

namespace Orders.Domain;

/// <summary>
/// What the rest of the estate is told when an order changes.
/// </summary>
/// <remarks>
/// <para>
/// These are contracts, not internal types. The notification service is written
/// in Java and deserialises them from Kafka, so a field renamed here is a field
/// broken there — with no compiler to say so. They carry only what a consumer
/// needs, because every field published is a field that has to keep existing.
/// </para>
/// <para>
/// Each carries a <see cref="MessageIdentityAttribute"/>, so what travels on the
/// wire is <c>order.placed</c> rather than <c>Orders.Domain.OrderPlaced</c>. A
/// consumer in another language has no business knowing this one's namespace,
/// and moving a class here should not rename anything there.
/// </para>
/// </remarks>
[MessageIdentity("order.placed")]
public sealed record OrderPlaced(
    Guid OrderId,
    string CustomerId,
    string Currency,
    decimal Total,
    int LineCount,
    DateTimeOffset PlacedAt);

[MessageIdentity("order.confirmed")]
public sealed record OrderConfirmed(
    Guid OrderId,
    string CustomerId,
    DateTimeOffset ConfirmedAt);

[MessageIdentity("order.cancelled")]
public sealed record OrderCancelled(
    Guid OrderId,
    string CustomerId,
    string Reason,
    DateTimeOffset CancelledAt);

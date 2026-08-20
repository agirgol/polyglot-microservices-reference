namespace Orders.Domain;

/// <summary>
/// An order, and the rules about what may happen to it next.
/// </summary>
/// <remarks>
/// <para>
/// The transitions live here rather than in a handler. A handler that checks
/// "is this order cancellable" is a rule one caller enforces; a method that
/// refuses is a rule every caller gets. The difference shows up the first time
/// a second entry point is added.
/// </para>
/// </remarks>
public sealed class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order()
    {
        CustomerId = string.Empty;
        Currency = string.Empty;
    }

    private Order(
        Guid id,
        string customerId,
        string currency,
        IEnumerable<OrderLine> lines,
        DateTimeOffset placedAt)
    {
        Id = id;
        CustomerId = customerId;
        Currency = currency;
        PlacedAt = placedAt;
        Status = OrderStatus.Placed;
        _lines.AddRange(lines);
    }

    public Guid Id { get; private set; }

    public string CustomerId { get; private set; }

    /// <summary>ISO 4217. One order, one currency — a basket priced in two is two orders.</summary>
    public string Currency { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset PlacedAt { get; private set; }

    public DateTimeOffset? SettledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyList<OrderLine> Lines => _lines;

    /// <summary>
    /// Derived from the lines rather than stored.
    /// </summary>
    /// <remarks>
    /// A stored total is a second source of truth that can disagree with the
    /// lines it came from, and when it does there is nothing on screen to say
    /// which one is wrong.
    /// </remarks>
    public decimal Total => _lines.Sum(line => line.LineTotal);

    public static Order Place(
        Guid id,
        string customerId,
        string currency,
        IReadOnlyCollection<OrderLine> lines,
        DateTimeOffset placedAt)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new ArgumentException("An order needs a customer.", nameof(customerId));
        }

        if (currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException(
                $"'{currency}' is not a three-letter ISO 4217 currency code.",
                nameof(currency));
        }

        if (lines.Count == 0)
        {
            throw new ArgumentException(
                "An order needs at least one line. An empty order is not an order.",
                nameof(lines));
        }

        return new Order(id, customerId, currency, lines, placedAt);
    }

    /// <summary>Confirms the order. Returns false if it was already confirmed.</summary>
    /// <remarks>
    /// The answer matters to the caller: an event announcing a confirmation
    /// that did not happen is a false statement on a topic other services act
    /// on. A consumer deduplicating it is a safety net for at-least-once
    /// delivery, not a licence to publish it.
    /// </remarks>
    public bool Confirm(DateTimeOffset at)
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException(
                $"Order {Id} was cancelled and cannot be confirmed. Place a new order instead.");
        }

        if (Status == OrderStatus.Confirmed)
        {
            // Confirming twice is what a retried request looks like, and the
            // second one asks for a state the order is already in. Refusing it
            // would make callers distinguish "failed" from "already done".
            return false;
        }

        Status = OrderStatus.Confirmed;
        SettledAt = at;
        return true;
    }

    /// <summary>Cancels the order. Returns false if it was already cancelled.</summary>
    public bool Cancel(string reason, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                "A cancellation needs a reason. Six months later it is the only thing anyone has.",
                nameof(reason));
        }

        if (Status == OrderStatus.Cancelled)
        {
            return false;
        }

        Status = OrderStatus.Cancelled;
        CancellationReason = reason;
        SettledAt = at;
        return true;
    }
}

namespace Orders.Domain;

/// <summary>
/// One line of an order.
/// </summary>
/// <remarks>
/// <para>
/// The price is <see cref="decimal"/>, never <c>double</c>. Binary floating
/// point cannot represent 0.10, and across a basket those fractions accumulate
/// rather than cancel.
/// </para>
/// </remarks>
public sealed record OrderLine
{
    /// <summary>
    /// Decimal places a unit price may carry, matching the numeric(18,4) column.
    /// </summary>
    /// <remarks>
    /// Four rather than two, because a unit price is not always a displayable
    /// amount — per-thousand and per-litre pricing both need more. Rounding a
    /// total to a currency's minor units is a presentation decision, and this
    /// service does not make it.
    /// </remarks>
    public const int PriceScale = 4;

    public OrderLine(string sku, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new ArgumentException("A line needs a SKU.", nameof(sku));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                "A line quantity must be positive. To remove a line, leave it out.");
        }

        if (unitPrice < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                "A unit price cannot be negative. A refund is its own transaction, not a negative line.");
        }

        if (decimal.Round(unitPrice, PriceScale) != unitPrice)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitPrice),
                unitPrice,
                $"A unit price carries at most {PriceScale} decimal places. Round it deliberately "
                    + "rather than letting the database decide which digits to drop.");
        }

        Sku = sku;
        Quantity = quantity;

        // Adding a zero at the working scale pads the value to it. Without this
        // a price given as 10.00 stays at scale 2 in memory and comes back from
        // the numeric(18,4) column at scale 4, so the same order's total
        // serialises as 20.00 when it is created and 20.0000 when it is read.
        // Same number, two strings, and a client comparing them sees a change
        // that did not happen.
        UnitPrice = unitPrice + 0.0000m;
    }

    /// <summary>For EF Core, which materialises without running the checks above.</summary>
    private OrderLine()
    {
        Sku = string.Empty;
    }

    public string Sku { get; private init; }

    public int Quantity { get; private init; }

    public decimal UnitPrice { get; private init; }

    public decimal LineTotal => Quantity * UnitPrice;
}

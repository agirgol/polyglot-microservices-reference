using Orders.Domain;
using Shouldly;
using Xunit;

namespace Orders.Tests;

/// <summary>
/// The rules an order enforces on itself.
/// </summary>
/// <remarks>
/// <para>
/// No database, no host, no container. These are statements about a type, and a
/// type that needs infrastructure to prove its own rules has the rules in the
/// wrong place.
/// </para>
/// </remarks>
public class OrderRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    private static Order AnOrder(params OrderLine[] lines) =>
        Order.Place(Guid.CreateVersion7(), "acme", "TRY", lines, Now);

    [Fact]
    public void An_order_needs_at_least_one_line()
    {
        Should.Throw<ArgumentException>(() => AnOrder())
            .Message.ShouldContain("at least one line");
    }

    [Fact]
    public void The_total_is_the_sum_of_the_lines()
    {
        var order = AnOrder(
            new OrderLine("WIDGET-1", 3, 19.90m),
            new OrderLine("BOLT-9", 10, 2.55m));

        order.Total.ShouldBe(85.20m);
    }

    [Fact]
    public void A_price_and_its_total_carry_the_working_scale()
    {
        // Not a formatting nicety. Left unnormalised, a price given as 19.90
        // stays at scale 2 in memory and returns from numeric(18,4) at scale 4,
        // so the same order serialises two different ways depending on which
        // endpoint answered.
        var order = AnOrder(new OrderLine("W", 1, 19.90m));

        order.Lines[0].UnitPrice.ToString().ShouldBe("19.9000");
        order.Total.ToString().ShouldBe("19.9000");
    }

    [Fact]
    public void A_price_finer_than_the_working_scale_is_refused_rather_than_rounded()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new OrderLine("W", 1, 1.234567m))
            .Message.ShouldContain("at most 4 decimal places");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_line_quantity_must_be_positive(int quantity)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new OrderLine("W", quantity, 1.00m));
    }

    [Fact]
    public void A_negative_price_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new OrderLine("W", 1, -1.00m))
            .Message.ShouldContain("refund");
    }

    [Fact]
    public void A_currency_must_be_a_three_letter_code()
    {
        Should.Throw<ArgumentException>(() =>
                Order.Place(Guid.CreateVersion7(), "acme", "Turkish Lira",
                    [new OrderLine("W", 1, 1.00m)], Now))
            .Message.ShouldContain("ISO 4217");
    }

    [Fact]
    public void A_cancelled_order_cannot_be_confirmed()
    {
        var order = AnOrder(new OrderLine("W", 1, 1.00m));
        order.Cancel("out of stock", Now);

        Should.Throw<InvalidOperationException>(() => order.Confirm(Now))
            .Message.ShouldContain("cancelled");
    }

    [Fact]
    public void Confirming_twice_is_what_a_retry_looks_like_and_is_allowed()
    {
        var order = AnOrder(new OrderLine("W", 1, 1.00m));
        order.Confirm(Now);
        var firstSettlement = order.SettledAt;

        order.Confirm(Now.AddMinutes(5));

        order.Status.ShouldBe(OrderStatus.Confirmed);
        // The second call changed nothing, including when it settled.
        order.SettledAt.ShouldBe(firstSettlement);
    }

    [Fact]
    public void A_cancellation_needs_a_reason()
    {
        var order = AnOrder(new OrderLine("W", 1, 1.00m));

        Should.Throw<ArgumentException>(() => order.Cancel("  ", Now))
            .Message.ShouldContain("reason");
    }

    [Fact]
    public void A_confirmed_order_can_still_be_cancelled()
    {
        var order = AnOrder(new OrderLine("W", 1, 1.00m));
        order.Confirm(Now);

        order.Cancel("customer changed their mind", Now.AddHours(1));

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancellationReason.ShouldBe("customer changed their mind");
    }
}

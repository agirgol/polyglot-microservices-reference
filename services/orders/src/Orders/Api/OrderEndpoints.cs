using Orders.Features;
using Wolverine;

namespace Orders.Api;

/// <summary>
/// The HTTP surface: parse, dispatch, translate the answer.
/// </summary>
/// <remarks>
/// <para>
/// Endpoints hold no rules. Each one turns a request into a command, hands it
/// to the bus, and maps the result onto a status code. When that is all an
/// endpoint does, the question "where is this enforced" has one answer.
/// </para>
/// </remarks>
public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder routes)
    {
        var orders = routes.MapGroup("/orders").WithTags("Orders");

        orders.MapPost("/", async (PlaceOrder command, IMessageBus bus) =>
        {
            var placed = await bus.InvokeAsync<OrderPlacedResult>(command);
            return Results.Created($"/orders/{placed.OrderId}", placed);
        });

        orders.MapGet("/{id:guid}", async (Guid id, IMessageBus bus) =>
        {
            var order = await bus.InvokeAsync<OrderView?>(new GetOrder(id));
            return order is null ? Results.NotFound() : Results.Ok(order);
        });

        orders.MapPost("/{id:guid}/confirmation", async (Guid id, IMessageBus bus) =>
        {
            var view = await bus.InvokeAsync<OrderView>(new ConfirmOrder(id));
            return Results.Ok(view);
        });

        orders.MapPost("/{id:guid}/cancellation", async (
            Guid id, CancelOrderRequest request, IMessageBus bus) =>
        {
            var view = await bus.InvokeAsync<OrderView>(new CancelOrder(id, request.Reason));
            return Results.Ok(view);
        });
    }

    public sealed record CancelOrderRequest(string Reason);
}

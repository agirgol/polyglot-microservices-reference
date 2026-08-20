using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orders.Features;

namespace Orders.Api;

/// <summary>
/// Turns the domain's refusals into responses that say what to change.
/// </summary>
/// <remarks>
/// <para>
/// Every exception handled here is a refusal rather than a fault: an order with
/// no lines, a negative quantity, a confirmation of something already
/// cancelled. Each is recoverable by whoever sent the request, so the message
/// has to survive the trip out — left untranslated they become a 500, and a
/// caller that cannot tell a refusal from a crash retries the request that will
/// never work.
/// </para>
/// <para>
/// 422 rather than 400: the body parsed and the types were right. What failed
/// is the rule.
/// </para>
/// </remarks>
public sealed class DomainExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            OrderNotFoundException => (StatusCodes.Status404NotFound, "Order not found"),
            ArgumentException => (StatusCodes.Status422UnprocessableEntity, "Cannot be placed"),
            InvalidOperationException => (StatusCodes.Status422UnprocessableEntity, "Not allowed in this state"),
            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            // Not a refusal. Let it through untouched so it is logged and
            // reported as the fault it is, rather than dressed up as advice.
            return false;
        }

        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
                Type = $"/problems/{title.ToLowerInvariant().Replace(' ', '-')}",
            },
        });
    }
}

using Gateway;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTelemetry(builder.Configuration, "gateway");

builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

/*
 * Rate limiting at the edge, where it belongs: a limit applied inside a service
 * has already cost that service the work of accepting the request.
 *
 * Partitioned by client address rather than global. A single global bucket
 * means one noisy caller exhausts everyone's allowance, which turns a rate
 * limit into an outage with extra steps. Behind a real load balancer this key
 * would come from a forwarded-for header that the balancer sets and the client
 * cannot; taking it from the socket here is correct only because nothing sits
 * in front of this.
 */
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimit:PermitsPerWindow", 100),
                Window = TimeSpan.FromSeconds(
                    builder.Configuration.GetValue("RateLimit:WindowSeconds", 10)),
                QueueLimit = 0,
            }));

    // Says when to come back. A 429 with no Retry-After leaves a client to
    // guess, and clients guess badly — usually immediately.
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new
            {
                type = "/problems/rate-limited",
                title = "Too many requests",
                status = 429,
            },
            cancellationToken);
    };
});

var app = builder.Build();

app.UseRateLimiter();

// The gateway's own health, not its destinations'. A destination being down is
// reported by YARP's health checks and handled by routing around it; it is not
// this endpoint's business to fail because something behind it did.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapReverseProxy();

app.Run();

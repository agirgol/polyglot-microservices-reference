using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Gateway;

/// <summary>
/// Traces and metrics for the edge.
/// </summary>
/// <remarks>
/// <para>
/// This is where a trace starts. If the gateway is not reporting, every trace
/// begins one hop in and the thing most worth knowing — how long the caller
/// actually waited — is missing from all of them.
/// </para>
/// </remarks>
internal static class Telemetry
{
    internal static IServiceCollection AddTelemetry(
        this IServiceCollection services, IConfiguration configuration, string serviceName)
    {
        var endpoint = configuration["Otlp:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                // YARP forwards through HttpClient, so the proxied call is an
                // outbound HTTP span. Without this the gateway's span has no
                // child and the trace looks like it stops at the edge.
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = new Uri(endpoint)))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = new Uri(endpoint)));

        return services;
    }
}

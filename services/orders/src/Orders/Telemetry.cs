using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Orders;

/// <summary>
/// Traces and metrics, exported over OTLP.
/// </summary>
/// <remarks>
/// <para>
/// The point of collecting these is not this service. It is that a request
/// entering at the gateway and a notification leaving a Java consumer end up on
/// one trace, which is only checkable if every hop reports to the same place.
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
            // Absent rather than defaulted. A service that silently exports
            // nowhere looks identical to one that is not instrumented, and the
            // difference is an afternoon.
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                    // Health probes are traffic, not work. Left in, they are
                    // most of the trace volume and none of the information.
                    options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                // Wolverine publishes its own spans for sending and handling a
                // message. Without this source the trace stops at the HTTP
                // request and the Kafka hop is invisible.
                .AddSource("Wolverine")
                .AddOtlpExporter(options => options.Endpoint = new Uri(endpoint)))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Wolverine:Orders")
                .AddOtlpExporter(options => options.Endpoint = new Uri(endpoint)));

        return services;
    }
}

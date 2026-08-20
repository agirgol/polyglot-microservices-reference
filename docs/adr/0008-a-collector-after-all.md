# ADR 0008: A collector after all

**Status:** Accepted
**Date:** 2026-08-21
**Revisits:** the collector alternative rejected in [ADR 0007](0007-one-trace-across-two-runtimes.md)

## Context

ADR 0007 rejected an OpenTelemetry Collector, and the reason it gave was true at
the time: *"this system has one backend and one sampling rate, so the collector
would be a container that forwards."*

Then metrics arrived, and there were two backends.

Every service was exporting straight to Jaeger. For traces that works. For
metrics it does not, and it does not fail loudly either — Jaeger is a trace
store, so metrics pushed at it are refused or dropped. The Java service was
caught doing exactly this and answered with a 404 once a minute. The .NET
services were quieter: they had no Prometheus endpoint at all, so their metrics
reached nothing and nothing said so. Prometheus reported them as `down` and that
was the only symptom.

The obvious local fix was to give the .NET services a `/metrics` endpoint. The
package that does that — `OpenTelemetry.Exporter.Prometheus.AspNetCore` — is
published only as a prerelease, and a prerelease had already been declined once
here, for the EF Core instrumentation. Taking it now would have meant either
inconsistency or reversing that too.

## Decision

An **OpenTelemetry Collector** between the services and the backends.

All three services export OTLP to one address. The collector fans out: traces to
Jaeger over OTLP, metrics to a Prometheus endpoint that Prometheus scrapes.

Prometheus now has one service target instead of three, and the Java service no
longer exposes `/actuator/prometheus` — it pushes OTLP like the others.

## Consequences

**Where a signal ends up stops being a property of each service.** Adding a
backend, changing sampling, or dropping a noisy metric is configuration in
`ops/otel/collector.yaml` rather than a change to three codebases in two
languages.

**No prerelease dependency, and no per-runtime shape.** Neither the .NET
Prometheus exporter nor Spring's actuator scrape path is needed. Both runtimes
do the same thing.

**One more container, and one more thing that can be down.** If the collector
stops, all telemetry stops — where previously each service failed independently.
Services are configured with `condition: service_started` rather than
`service_healthy` for it, so telemetry being unavailable does not stop the
system doing its job.

**Metrics arrive labelled by their source.** The collector's Prometheus exporter
keeps the service name as `exported_job`, so `notifications_handled_total`
carries `exported_job="notifications"` while being scraped from
`instance="otel-collector:8889"`.

Verified after the change: 523 metric names in Prometheus, `aspnetcore_*` and
`dotnet_*` among them, and all four services still reporting traces to Jaeger.

## Alternatives considered

**The prerelease .NET Prometheus exporter.** Smallest change, no new container.
Rejected on the prerelease, and because it leaves the two runtimes exporting
metrics by different mechanisms — which is the kind of asymmetry that is fine
until someone has to change both.

**Drop .NET metrics.** Honest, and leaves an observability story with a hole in
the half of the estate that serves the requests.

**Push metrics to a Prometheus remote-write endpoint from each service.**
Removes the scrape but keeps every service knowing about every backend, which is
what the collector exists to stop.

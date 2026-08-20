# ADR 0007: One trace across two runtimes, and the header that nearly stopped it

**Status:** Accepted
**Date:** 2026-08-21

## Context

The claim this repository is built to support is that a request entering at the
gateway and a notification leaving a Java consumer are one trace. That is only
worth claiming if it can be shown, and it is exactly the thing that quietly does
not work: each service is instrumented, each produces spans, and the trace
silently restarts at every boundary the context fails to cross.

## Decision

OpenTelemetry everywhere, exported over OTLP to Jaeger.

- **`gateway`, `orders`** — the .NET OpenTelemetry SDK. ASP.NET Core and
  HttpClient instrumentation, plus the `Wolverine` activity source so the
  message send is a span rather than a gap.
- **`notifications`** — Micrometer Tracing with the OpenTelemetry bridge, via
  `spring-boot-starter-opentelemetry`, and `spring.kafka.listener.observation-enabled`
  so a consumed record continues the producer's trace instead of starting a new
  one.
- **The wire** — order events carry a standard `traceparent` header, written by
  a custom `IKafkaEnvelopeMapper`.

Sampling is 1.0. A claim that one request makes one trace is not demonstrable at
10%.

## The header

Wolverine writes the trace context to a Kafka header named `parent-id`. The
value is in W3C `traceparent` format; the name is not `traceparent`.
OpenTelemetry's Java instrumentation reads the standard name, finds nothing, and
starts a fresh trace. Nothing fails. Both services report spans, both look
healthy, and the two halves of every request live in separate traces.

The fix belongs on the producer. W3C Trace Context exists so that a consumer
does not need to know what produced a message; teaching the Java service about a
.NET framework's header naming would work once and be wrong for the next
consumer, in whatever language it arrives. So `OrderEventKafkaMapper` writes
`traceparent`, and while it was being written the rest of the contract was made
explicit too — `event-type: order.placed` rather than
`message-type: Orders.Domain.OrderPlaced`, because a consumer has no business
knowing the producer's namespace.

## Consequences

**It can be shown.** One request through the gateway:

```
+  0.0 ms  gateway        POST /orders/{**catch-all}
+  0.8 ms  gateway        POST
+  1.1 ms  orders         POST /orders/
+  1.5 ms  orders         Orders.Features.PlaceOrder
+ 15.7 ms  orders         send
+282.0 ms  notifications  orders.placed process
+284.9 ms  notifications  set
```

Seven spans, three services, one trace id.

**The 282 ms is the outbox, and it is supposed to be there.** The event is not
sent during the request; it is written to Postgres and forwarded by a background
sweep. The gap on the trace is the cost of the guarantee in ADR 0002, made
visible. A reader who expects an instant hop should see this and understand why
it is not one.

**The contract is now ours to keep.** The mapper writes every header
deliberately, so nothing changes shape because a framework changed its defaults
— and equally, nothing changes shape unless someone changes this file.

**Two runtimes, two ways to be misconfigured.** Both were, and neither said so:
see below.

## What was silently broken

**`spring-kafka` without `spring-boot-starter-kafka`.** The classes resolve,
no listener container is registered, `@KafkaListener` is ignored, and the
service starts cleanly while consuming nothing.

**`micrometer-tracing-bridge-otel` plus `opentelemetry-exporter-otlp` without
`spring-boot-starter-opentelemetry`.** Same shape: the tracer runs, spans are
created, log lines even carry a trace id — and no exporter is configured, so
nothing leaves the process.

**`management.otlp.tracing.endpoint`.** The Boot 3 spelling, still accepted and
still documented, and it did not export. The current path is
`management.opentelemetry.tracing.export.otlp.endpoint`.

Three failures, three services that reported healthy. The only way any of them
surfaced was asking Jaeger which services it had heard from, which is why that
question is now the first step of the verification rather than the last.

## Alternatives considered

**The OpenTelemetry Java agent.** Instruments more, for no code. Rejected
because it is a second artifact that has to be shipped beside the jar and
attached at launch — one more thing to get right in a container image, and
silent when it is not. A dependency either builds or does not.

**Teaching the consumer to read `parent-id`.** Smaller change, and it puts
knowledge of a .NET framework's internals into a Java service. It also has to be
repeated in every future consumer.

**A collector between the services and Jaeger.** The right answer in production:
it decouples applications from backends and moves sampling out of them. Left out
because this system has one backend and one sampling rate, so the collector
would be a container that forwards.

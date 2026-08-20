# ADR 0005: The notification service is Java, and that is the point

**Status:** Accepted
**Date:** 2026-08-21

## Context

A third service in .NET would have been faster to write, shared the solution
file, shared the contracts by project reference, and demonstrated nothing that
the second one did not.

Two of this repository's claims cannot be checked without a second runtime:

- **A Kafka contract is language-neutral.** Unprovable while every consumer
  deserialises with the producer's own library, from the producer's own types.
- **A trace crosses service boundaries.** Trivially true between two processes
  using the same SDK and the same propagator. The interesting case is two
  different instrumentations agreeing on a header.

## Decision

`notifications` is **Spring Boot 4.1** on Java 21. It shares no library, no
generated code, and no build with the .NET side. Its event records are
hand-written from the JSON on the topic.

## Consequences

**The contract is enforced by nothing, which is the honest situation.** Rename a
field on the .NET side and nothing fails to compile — it fails at runtime,
against a consumer that has already been deployed. That is what a cross-language
boundary is, and it is why the shape is asserted by an integration test rather
than trusted.

**It found things a second .NET service could not have.** Every one of these was
silent — the service started cleanly and did the wrong thing, or nothing:

- Wolverine writes the trace context to a `parent-id` header in W3C format under
  a name that is not `traceparent`. OpenTelemetry's Java instrumentation reads
  the standard name, so every request lived in two traces while both services
  reported healthy (ADR 0007).
- `spring-kafka` without `spring-boot-starter-kafka` registers no listener
  container. `@KafkaListener` is ignored, the service starts, and it consumes
  nothing.
- The tracing libraries without `spring-boot-starter-opentelemetry` produce
  spans with no exporter configured. Log lines carry trace ids; nothing leaves
  the process.
- Spring Boot 4 ships Jackson 3, which moved from `com.fasterxml.jackson` to
  `tools.jackson`.

**Two toolchains.** Two dependency managers, two version files, two CI jobs, two
ways to be out of date. Contributors need both installed.

**Two ways to express the same idea.** Idempotency is a Redis `SETNX` in Java
and would be something else in .NET; metrics were an actuator endpoint until the
collector made both sides do the same thing (ADR 0008). Every such asymmetry is
a place where the two halves can drift.

## Alternatives considered

**A third .NET service.** Cheaper in every way and proves neither claim. The
contract would be a shared assembly, and the trace would cross between two
copies of the same SDK.

**Go or Node.** Would demonstrate the same two things. Java was chosen because
Spring Boot is the other stack this estate is plausibly built from, and because
its Kafka and observability integrations are the ones a reviewer from that world
will recognise — including the parts that were wrong.

**A consumer written against a shared schema registry with generated types.**
The right answer at scale, and it removes the property being demonstrated: the
whole point is that the two sides agree on JSON without a compiler holding them
together.

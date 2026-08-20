# ADR 0009: Real containers at the boundaries, not mocks

**Status:** Accepted
**Date:** 2026-08-21

## Context

The claims this repository makes are almost all about boundaries: a decimal
surviving a NUMERIC column, a message landing on a topic with particular
headers, an event published once rather than twice. None of them are claims
about the code's behaviour against an interface, so none of them can be checked
by substituting one.

A mocked broker agrees with whatever the test author believed about brokers.
That is the failure mode here specifically: the wire contract was wrong for
several commits — the trace context under `parent-id`, the .NET type name in
`message-type` — and a mock would have asserted the wrong thing confidently.

## Decision

Domain rules are tested with no infrastructure at all. Everything at a boundary
is tested against the real thing, through Testcontainers.

- `Orders.Tests` — 13 tests, no database, no containers, milliseconds. A type
  that needs infrastructure to prove its own rules has the rules in the wrong
  place.
- `Orders.IntegrationTests` — the service under `WebApplicationFactory` against
  a real Postgres and a real Kafka. Schema from the migrations, not from a test
  helper: a test passing against a schema the migrations never produced proves
  nothing about deployment.

What the integration tests assert is mostly the contract, because the contract
is what was wrong: `event-type: order.placed` and not a .NET type name, a
`traceparent` header in W3C form, `59.7000` surviving the round trip at its
scale, and one `orders.confirmed` event for two confirmations.

## Consequences

**They are slow.** Around three minutes, most of it container startup. That is
why they are a separate project from the unit tests rather than mixed in — the
fast ones stay fast and get run constantly.

**They need a Docker daemon**, which every developer here has and
`ubuntu-latest` ships running.

**Kafka is the Confluent build in tests and the Apache one in compose.** Not a
preference: the Testcontainers module could not bring `apache/kafka:4.3.0` up
under either vendor setting, because working out the advertised address means
starting the container, reading the mapped port and rewriting the config, and
the module only does that reliably for the image it was built around. The
difference is packaging rather than protocol, and what these tests assert is the
shape of our own messages. Where the broker version does matter is compose, and
that is pinned to what gets deployed.

**One race had to be handled rather than hidden.** A consumer subscribing to
`orders.confirmed` before anything has published gets `Unknown topic or
partition`, because the topic does not exist yet — the outbox publishes on a
sweep, not during the request. The test waits for the topic to appear. That is
not a workaround; it is what any consumer started before its producer has to do.

## Alternatives considered

**An in-memory broker and an in-memory database.** Fast, and they answer
questions about themselves. `numeric(18,4)` rounding, `xmin` concurrency,
Kafka header encoding and topic auto-creation are all properties of the real
things.

**Testing the contract by reading the mapper's code.** That is what the mapper
already says it does. The test exists because it said that before, and was wrong.

**Only the outbox script.** `ops/prove-outbox.sh` is a better demonstration than
any test — it stops a broker mid-flight — but it needs a running system and a
person to read it. CI needs something that fails on its own.

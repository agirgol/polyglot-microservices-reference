# ADR 0006: Idempotency keyed on the business fact, and what happens when the key store is down

**Status:** Accepted
**Date:** 2026-08-21

## Context

Delivery is at-least-once. That is not a defect of the producer — it is what an
outbox forwarding to a broker can promise (ADR 0002). A message arriving twice
is ordinary traffic, and the consumer is where it stops being a second email.

## Decision

Each event is claimed in Redis before it is acted on:

```java
String key = "notifications:handled:%s:%s".formatted(eventType, businessKey);
redis.opsForValue().setIfAbsent(key, "1", Duration.ofDays(7));
```

Three choices in that line.

**Keyed on the business fact — the order id and what happened to it — not the
producer's envelope id.** An envelope id recognises a redelivery of that exact
message. It does not recognise the same fact republished, which is what a
replayed topic or a re-emitting producer looks like.

**`SETNX`, not read-then-write.** Two consumer instances handed the same message
would both find nothing and both proceed.

**Bounded at seven days.** This is a cache, not a ledger. The window has to
cover the longest plausible gap between a delivery and its retry — a broker
outage, a consumer restart, an operator replaying a topic after an incident.

## Consequences

**A duplicate after the window produces a duplicate notification.** That is the
accepted cost, and it is acceptable *here*: the consequence is a second email.
The same design under a payment would not be, and that difference is the
decision — not the number of days.

**Measured, with the producer fixed:** two confirmations of the same order
produce one event and one notification. Before `Order.Confirm` reported whether
it had changed anything, the same test produced two events, one notification and
one suppression — the guard working, around a producer that should not have been
publishing.

**`notifications_suppressed_total` sitting at zero is the healthy state**, not a
sign the guard is untested. It moves when a genuine duplicate is delivered.

## What happens when Redis is down

Measured, because a guard nobody has watched fail is not a guard.

With Redis stopped and an order placed:

- The listener **blocks for 60 seconds** — Lettuce's default command timeout —
  and logs nothing at all while it does. The consumer has stopped consuming and
  the only visible symptom is reconnect noise from the Redis client.
- It then throws `RedisCommandTimeoutException`. Spring Kafka's default error
  handler retries, so the message is **not lost**: when Redis came back, the
  notification was produced.
- **The container reported `healthy` throughout.** The health endpoint does
  check Redis, and it took 60 seconds to answer — long enough that the readiness
  signal said nothing was wrong while the service was doing nothing.

The first two are acceptable and the third is not. A service that cannot work
should not report that it can, and here it did — for minutes, while a queue
built up behind it.

Two things would fix it, neither done yet and both recorded rather than left to
be discovered:

- A command timeout measured in seconds, so a failure fails fast instead of
  stalling a consumer thread for a minute per message.
- A readiness probe that reflects the consumer's ability to work, rather than
  one that waits on the same dependency it is reporting on.

## Alternatives considered

**A Postgres table of handled events.** Durable, no TTL question, and
transactional with any other write the consumer makes. Rejected because this
consumer writes nothing else, so the table would exist only to be the cache
Redis already is — and would put the notification service on the orders
database or give it one of its own.

**Kafka exactly-once semantics.** A transactional producer and a
`read_committed` consumer remove duplicates *within* Kafka. They do not make the
consumer's own side effect idempotent, which is the part that sends an email,
and they do not span the outbox forwarding from Postgres.

**No deduplication, and accept duplicates.** Defensible if the effect is
harmless. Sending a customer two emails saying their order was placed is the
kind of harmless that generates support tickets.

**Deduplicate in the producer.** It already does, in the sense that matters: it
no longer publishes an event for a transition that changed nothing. That
narrows the problem and does not remove it, because redelivery happens below
the producer entirely.

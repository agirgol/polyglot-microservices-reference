# ADR 0002: An order and its event commit together, or neither does

**Status:** Accepted
**Date:** 2026-08-20

## Context

Placing an order does two things: it writes a row, and it tells the rest of the
estate. The obvious implementation does them one after the other — save, then
publish. That is a dual write, and it has two failure modes that are invisible
in development:

- The row commits and the publish fails. The order exists and nothing downstream
  knows. No notification, no fulfilment, no error anywhere: the request returned
  201.
- The publish succeeds and the transaction rolls back. Consumers act on an order
  that does not exist.

Neither is rare under load, and neither produces a log line saying what
happened.

## Decision

Outgoing messages are written to the same Postgres database as the order,
inside the same transaction, and forwarded to Kafka afterwards by Wolverine's
durable outbox. Handlers return their events rather than sending them, so
nothing in the handler decides when a message leaves.

Concretely, three settings, and the third is the one that matters:

```csharp
opts.PersistMessagesWithPostgresql(connectionString, schemaName: "wolverine");
opts.UseEntityFrameworkCoreTransactions();
opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
```

## Consequences

**At-least-once, not exactly-once.** A message can be delivered twice — the
forward can succeed and the acknowledgement can be lost. Consumers have to be
idempotent, which is a constraint on the notification service rather than a
detail of this one. See ADR 0006.

**Latency, not much of it.** The message is written, then forwarded. Measured
here, delivery after the broker returned took under three seconds; the forward
is a background sweep, not part of the request.

**A schema the application does not own.** Wolverine's tables live in a
`wolverine` schema alongside the domain tables. They are its business and
should not be queried by anything here except to diagnose.

**The database is now the single point of failure for messaging too.** If
Postgres is down nothing works — but nothing worked anyway, since the order
cannot be written either.

## What the third line is for, and how we found out

The first two lines configure storage. On their own they do nothing: Wolverine's
sending endpoints are *buffered* by default, which means a message is held in
memory and handed to the transport after the transaction commits. The outbox
tables exist, and stay empty.

This was not read in documentation. It was found by stopping Kafka, posting an
order, and looking:

```
=== outgoing envelopes ===
(0 rows)

=== is the order in the database ===
 01a020d2-24ed-7ab7-93de-f75c747b0614 | OUTBOX-PROOF
```

The order committed. Its event was in memory, and the process was restarted, so
it is gone — permanently, with nothing anywhere to say so. That is precisely the
first failure mode above, reproduced by a configuration that looked correct.

With `UseDurableOutboxOnAllSendingEndpoints()` the same test gives:

```
=== outgoing envelopes ===
 Orders.Domain.OrderPlaced | kafka://topic/orders.placed
```

and when the broker comes back, the row clears and the message arrives. The two
orders are distinguishable afterwards: both are in the database, only the second
reached the topic.

`ops/prove-outbox.sh` runs this end to end.

## Alternatives considered

**Publish after commit, accept the risk.** Cheapest, and defensible for events
nobody acts on. Rejected because these events drive a notification: a customer
not being told their order was placed is the failure the system is for.

**Two-phase commit across Postgres and Kafka.** Kafka does not participate in
XA, and distributed transactions bring their own coordinator to keep alive.

**Change data capture with Debezium.** Reads the write-ahead log and publishes
without an application-level outbox. A legitimate answer at scale — see ADR 0001
for why it is out of scope here.

**Publish first, then write.** Reverses which failure is possible rather than
removing one.

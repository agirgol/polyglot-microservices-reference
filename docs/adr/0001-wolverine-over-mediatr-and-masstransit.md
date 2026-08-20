# ADR 0001: Wolverine for in-process dispatch and messaging, not MediatR plus MassTransit

**Status:** Accepted
**Date:** 2026-08-20

## Context

The orders service needs two things that are conventionally two libraries in
.NET: in-process request dispatch (the mediator pattern, usually MediatR) and
asynchronous messaging to Kafka with a transactional outbox (usually
MassTransit). The pairing is close to a default in this ecosystem.

Two facts made the default unavailable.

**MassTransit's transactional outbox does not work with Kafka.** In MassTransit,
Kafka is a *rider*, not a transport, and the outbox supports transports only.
This is not a version gap to wait out; it follows from Kafka not being a broker
in the sense the outbox is built around. The outbox is the entire reason the
library was on the list, so a Kafka-based estate does not get the feature it was
chosen for.

**Both libraries moved to commercial licences.** MassTransit v9 shipped under a
commercial licence in Q1 2026 and maintenance of the free v8 line ends after
2026. MediatR v13 and later are commercial under Lucky Penny Software.

The licence cost is not the binding constraint here — both vendors waive fees
below a revenue threshold this project is far under. The constraint is that the
free path is a library whose maintenance ends within months, and which cannot do
the one thing it was picked for.

## Decision

Use **Wolverine** (MIT, JasperFx) for both roles. It provides in-process
handler dispatch, Kafka as a first-class transport, and a durable transactional
outbox backed by the service's own Postgres database through EF Core.

One library covers what two were going to, and the outbox works with the
transport this system actually uses.

## Consequences

**The outbox becomes real rather than aspirational.** Outgoing messages commit
in the same Postgres transaction as the business state, then forward. There is
no window in which an order exists and its event does not.

**Fewer moving parts.** No second library to configure, version, and explain;
handler dispatch and message publishing share one model.

**Less familiar to reviewers.** MediatR and MassTransit are what .NET job
listings name, and a reader may have to learn Wolverine's handler conventions to
follow the code. This ADR exists partly to answer the question that will
provoke.

**Wolverine's source-generated handler pipeline is harder to step through** in a
debugger than a MediatR handler call. Compiled handlers are fast and the
generated code can be written to disk for inspection, but "set a breakpoint and
walk in" is not the same experience.

**Undoing it is not cheap.** Handlers, the outbox wiring, and the Kafka
publishing all follow Wolverine's conventions. Moving back would mean adopting
MediatR for dispatch and hand-writing an outbox relay for Kafka — which is the
work this decision avoids.

## Alternatives considered

**MediatR (Community edition) + MassTransit v8 + a hand-written outbox.** The
brief's original plan, minus the part that does not exist. The licence terms are
satisfiable at this revenue, but v8 is out of maintenance after 2026 and the
outbox would have to be written by hand anyway — a table, a relay, at-least-once
delivery, poison handling, and the ordering guarantees that come with them.
Writing that correctly is a project; writing it incorrectly is worse than not
claiming an outbox.

**MediatR + MassTransit v9 (commercial).** Removes the maintenance concern and,
from v9.1, reportedly adds Kafka outbox support. Rejected because a reference
architecture whose central pattern requires a commercial licence is a reference
a reader cannot fully reproduce, and because the discount that makes it free is
revenue-dependent and could lapse.

**Debezium change-data-capture from Postgres to Kafka.** The outbox without an
application-level outbox: write the row, let CDC publish it. This is what large
estates often do and it is a legitimate answer. Rejected for scope — it adds
Kafka Connect and a Debezium connector to a stack that already has six
containers, and it moves the interesting part of the decision out of the code
and into infrastructure configuration, where this repository can demonstrate
less about it. Revisit if the outbox relay ever becomes the bottleneck.

**NServiceBus.** Commercial from the start, and priced for estates rather than
for a reference repository. Not evaluated in depth for that reason.

**Brighter.** Open source, has an outbox, supports Kafka. A reasonable option
that was not evaluated in depth; Wolverine won on covering the mediator role in
the same library. Left open rather than closed.

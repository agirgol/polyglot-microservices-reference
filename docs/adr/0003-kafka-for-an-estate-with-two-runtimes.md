# ADR 0003: Kafka rather than RabbitMQ

**Status:** Accepted
**Date:** 2026-08-21

## Context

Two services in two runtimes have to agree on how a message gets from one to the
other. The choice shapes what the consumer can do — whether it can be added
later and catch up, whether ordering means anything, whether a second consumer
group is free or a redesign.

## Decision

**Kafka**, with one topic per event type.

## Consequences

**A consumer added later sees what it missed.** The log is retained, so
`auto-offset-reset: earliest` gives a new consumer the history rather than
whatever happens to arrive next. This is not theoretical here: the notification
service was written after the producer, and the first time it started it
consumed events that had been published before it existed. On a queue those
messages would have been delivered to nobody and dropped.

**Reprocessing is a consumer-group id, not a replay tool.** Rebuilding a
projection or testing a new consumer against real traffic means a new group,
not asking the producer to send anything again.

**Heavier than a queue.** One broker, one controller quorum, partitions,
consumer group coordination — against RabbitMQ's exchange and queue. For two
services this is more machinery than the problem needs, and it is the cost of
the two properties above.

**Ordering is per partition, and nothing here sets a partition key.**
`OrderEventKafkaMapper` writes the value and the headers; it does not set
`Message.Key`. With one partition that is invisible — every event for every
order is ordered. With two it would not be: `order.placed` and
`order.confirmed` for the same order could land on different partitions and be
processed out of order. Keying on the order id fixes it and has not been done.
This is a known gap, recorded rather than discovered later.

**No per-message acknowledgement or redelivery.** A consumer commits offsets;
it does not nack an individual message back onto the topic. Failure handling is
retry-in-place, a retry topic, or a dead letter topic — see ADR 0006 for what
this system actually does.

## Alternatives considered

**RabbitMQ.** Lighter, better ergonomics for work queues, real per-message
acknowledgement, and dead-lettering built in rather than assembled. It would
have been the better choice if the requirement were "hand work to a worker".
It is the worse choice for "tell anyone who cares, including whoever is written
next month", because a message delivered to an empty exchange is gone.

**NATS JetStream.** Lighter than Kafka with retention, and genuinely good.
Rejected on ecosystem: the .NET and Java client stories are thinner, and the
point of this repository is to be recognisable to people who work in these two
stacks.

**Azure Service Bus.** Would tie a reference architecture to one cloud and make
`docker compose up` impossible. That alone settles it here.

**HTTP calls between the services.** No broker, no operational surface, and the
notification service becomes a dependency of placing an order — down means
orders fail. The whole reason for the outbox in ADR 0002 is not to have that.

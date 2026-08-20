# Architecture decision records

Why this system is shaped the way it is. Each record states what forced a
choice, what was chosen, what it costs, and which alternatives lost — the last
part being the one that makes it a decision rather than a description.

A record is never edited to reflect a change of mind. When a decision is
replaced, its status becomes `Superseded by ADR-NNNN` and a new record explains
why. Being able to see what was tried and abandoned is most of the value.

| | Decision | Status |
|---|---|---|
| [0001](0001-wolverine-over-mediatr-and-masstransit.md) | Wolverine for dispatch and messaging, not MediatR plus MassTransit | Accepted |
| [0002](0002-transactional-outbox-over-dual-write.md) | An order and its event commit together, or neither does | Accepted |

Planned, in the order they are likely to be written:

| | Question |
|---|---|
| 0003 | Kafka rather than RabbitMQ for an estate with two runtimes |
| 0004 | YARP rather than Ocelot at the edge |
| 0005 | Why the notification service is Java |
| 0006 | How consumers stay idempotent under at-least-once delivery |
| 0007 | The observability stack, and what it does not collect |
| 0008 | Testcontainers rather than mocks at the boundaries |

[0000-template.md](0000-template.md) is the shape to follow.

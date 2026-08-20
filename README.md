# polyglot-microservices-reference

Three services, two runtimes, one trace. A reference architecture where every
structural choice has a decision record behind it — including the ones that were
rejected, and the one that turned out to be impossible as originally planned.

| Service | Runtime | Role |
|---|---|---|
| `orders` | .NET 10 | Domain service — CQRS, EF Core 10, transactional outbox |
| `notifications` | Spring Boot 4.1 | Kafka consumer, idempotent delivery |
| `gateway` | .NET 10 / YARP 2.3 | Reverse proxy, auth, rate limiting, resilience |

The Java service is not there to prove Java. It is there because a Kafka
contract has to be language-neutral to be worth anything, and because a trace
that stops at a runtime boundary is not distributed tracing. One request through
the gateway should produce one trace in Jaeger, spanning both stacks.

## The decisions are the deliverable

`docs/adr/` is the point of this repository. The code exists to make those
decisions concrete and checkable — open an ADR, open the code it describes, and
the two should agree.

The first one is already the most useful. The plan was MediatR for dispatch and
MassTransit for Kafka messaging, which is close to a .NET default. It does not
work: **MassTransit's transactional outbox does not support Kafka** — Kafka is a
rider there, not a transport, and the outbox covers transports only. Both
libraries also moved to commercial licences during 2026. The replacement is
Wolverine, which is MIT and does both jobs against Kafka.

→ [ADR 0001: Wolverine over MediatR and MassTransit](docs/adr/0001-wolverine-over-mediatr-and-masstransit.md)

## Running it

```sh
docker compose up -d
```

That is the whole setup. Every image is pinned to an exact version rather than a
moving tag, because a reference architecture that behaves differently depending
on the day you cloned it is not a reference.

| | |
|---|---|
| Jaeger | http://localhost:16686 |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 |
| Postgres | `localhost:5432`, `orders` / `orders` |
| Kafka | `localhost:29092` from the host, `kafka:9092` inside |
| Redis | `localhost:6379` |

Kafka advertises two listeners on purpose: one address is reachable from inside
the compose network and a different one from your machine, and a single listener
cannot serve both.

## Status

Infrastructure is up and verified — Postgres 18 answers, a message round-trips
through Kafka, Redis responds, and all three consoles serve. The services are
not built yet.

| | |
|---|---|
| Compose stack: Postgres, Kafka, Redis, Jaeger, Prometheus, Grafana | ✅ |
| ADR 0001 — messaging library | ✅ |
| `orders` — CQRS, EF Core 10, migrations | ⬜ |
| Transactional outbox to Kafka | ⬜ |
| `notifications` — Spring Boot Kafka consumer | ⬜ |
| `gateway` — YARP, resilience policies | ⬜ |
| One trace across both runtimes | ⬜ |
| Testcontainers integration tests | ⬜ |
| NBomber load profile | ⬜ |
| Kubernetes manifests and Helm chart | ⬜ |

## Versions

Pinned as of August 2026 and chosen deliberately: .NET 10 LTS, EF Core 10,
Spring Boot 4.1, Kafka 4.3, Postgres 18, Redis 8.2, YARP 2.3, Jaeger 2.10,
xUnit v3, Wolverine.

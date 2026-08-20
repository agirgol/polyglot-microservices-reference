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
| `orders` — domain rules, EF Core 10, migrations, HTTP surface | ✅ |
| Transactional outbox to Kafka | ✅ |
| `notifications` — Spring Boot Kafka consumer | ⬜ |
| `gateway` — YARP, resilience policies | ⬜ |
| One trace across both runtimes | ⬜ |
| Testcontainers integration tests | ⬜ |
| NBomber load profile | ⬜ |
| Kubernetes manifests and Helm chart | ⬜ |

## The outbox, and how we found out it was not on

An order and its event commit together or neither does. Outgoing messages are
written to the same Postgres transaction that writes the order, then forwarded
to Kafka.

Configuring the storage is not the same as using it. Wolverine's sending
endpoints are buffered in memory by default: the tables exist, stay empty, and
a message that was "published" is gone if the process dies before the transport
takes it. The setting that matters is the third line, and it was missing:

```csharp
opts.PersistMessagesWithPostgresql(connectionString, schemaName: "wolverine");
opts.UseEntityFrameworkCoreTransactions();
opts.Policies.UseDurableOutboxOnAllSendingEndpoints();   // <- this one
```

The way that surfaced is the way it should: stop Kafka, post an order, look at
the outbox table. It was empty and the order was committed — an order that
exists with nothing downstream ever hearing about it, which is the exact failure
the outbox is for.

```sh
./ops/prove-outbox.sh
```

Stops the broker, posts an order, shows the event waiting in the outbox, starts
the broker, shows the row clear and the message arrive. The claim is the script;
if it stops passing, the ADR is wrong.

→ [ADR 0002: An order and its event commit together, or neither does](docs/adr/0002-transactional-outbox-over-dual-write.md)

## What goes on the wire

Wolverine puts the message body on Kafka as plain JSON with no envelope around
it, which is what makes a Java consumer possible without a shared library:

```
{"orderId":"01a020d4-…","customerId":"acme","currency":"TRY","total":59.7000,"lineCount":1,"placedAt":"…"}
```

Metadata rides in Kafka headers. One of them is a problem worth stating before
it is solved: the trace context is written to a header named `parent-id`, in W3C
`traceparent` format but not under that name. OpenTelemetry's Java
instrumentation looks for `traceparent`, so a trace will stop dead at the
runtime boundary unless something bridges the two. That is a task for the
tracing milestone, not a surprise to be discovered then.

## Running `orders` on its own

It is not in the compose file yet — that comes with its container image, in the
messaging milestone. Until then:

```sh
docker compose up -d postgres kafka
dotnet ef database update --project services/orders/src/Orders \
  --connection "Host=localhost;Port=5432;Database=orders;Username=orders;Password=orders"
ConnectionStrings__Orders="Host=localhost;Port=5432;Database=orders;Username=orders;Password=orders" \
Kafka__BootstrapServers="localhost:29092" \
  dotnet run --project services/orders/src/Orders
```

The service refuses to start without that connection string rather than falling
back to a default, for the same reason the compose file has no datasource
guesses in it.

```sh
dotnet test          # 12 domain tests, no database, no containers
```

## Versions

Pinned as of August 2026 and chosen deliberately: .NET 10 LTS, EF Core 10,
Spring Boot 4.1, Kafka 4.3, Postgres 18, Redis 8.2, YARP 2.3, Jaeger 2.10,
xUnit v3, Wolverine.

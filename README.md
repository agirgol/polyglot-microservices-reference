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

That is the whole setup: ten containers, three of them built from this
repository, healthy in under twenty seconds. Every image is pinned to an exact
version rather than a moving tag, because a reference architecture that behaves
differently depending on the day you cloned it is not a reference.

```sh
curl -X POST localhost:8080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"acme","currency":"TRY",
       "lines":[{"sku":"WIDGET-1","quantity":3,"unitPrice":19.90}]}'

curl localhost:8082/notifications      # what the Java consumer did with it
open http://localhost:16686            # and the one trace it all happened on
```

**[→ docs/walkthrough.md](docs/walkthrough.md)** checks every claim on this page
from a terminal in about fifteen minutes — the outbox surviving a broker outage,
the rate limiter's exact split, a dead destination failing in milliseconds, and
one trace crossing both runtimes. Each step says what would have to be false for
it to fail.

| | |
|---|---|
| **Gateway — the way in** | http://localhost:8080 |
| Orders, directly | http://localhost:8081 |
| Notifications, directly | http://localhost:8082 |
| Jaeger | http://localhost:16686 |
| Prometheus | http://localhost:9090 |
| Grafana | http://localhost:3000 |
| Postgres | `localhost:5432`, `orders` / `orders` |
| Kafka | `localhost:29092` from the host, `kafka:9092` inside |
| Redis | `localhost:6379` |

Services wait for what they need to be *healthy*, not merely started. Kafka
accepts connections well before it accepts a produce, and a service that starts
into that window fails in a way that reads like a bug in the service.

Kafka advertises two listeners on purpose: one address is reachable from inside
the compose network and a different one from your machine, and a single listener
cannot serve both.

## Status

Infrastructure is up and verified — Postgres 18 answers, a message round-trips
through Kafka, Redis responds, and all three consoles serve. The services are
not built yet.

| | |
|---|---|
| Compose stack, all three services included, one command | ✅ |
| ADR 0001 — messaging library | ✅ |
| `orders` — domain rules, EF Core 10, migrations, HTTP surface | ✅ |
| Transactional outbox to Kafka | ✅ |
| `notifications` — Spring Boot Kafka consumer, idempotent | ✅ |
| `gateway` — YARP, rate limiting, destination health | ✅ |
| One trace across both runtimes | ✅ |
| Testcontainers integration tests | ✅ |
| NBomber load profile | ✅ |
| Kubernetes manifests | ✅ |
| Helm chart | ⬜ |

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

## One trace, two runtimes

The claim the repository exists to support. One `POST` through the gateway:

```
+  0.0 ms  gateway        POST /orders/{**catch-all}
+  0.8 ms  gateway        POST                          ← YARP forwarding
+  1.1 ms  orders         POST /orders/
+  1.5 ms  orders         Orders.Features.PlaceOrder    ← Wolverine handler
+ 15.7 ms  orders         send                          ← published via the outbox
+282.0 ms  notifications  orders.placed process         ← Java consumer
+284.9 ms  notifications  set                           ← Redis claim
```

Seven spans, three services, one trace id, in Jaeger at
[localhost:16686](http://localhost:16686).

The 282 ms gap is not latency to fix. The event is written to Postgres inside
the order's transaction and forwarded by a background sweep, so the trace shows
the cost of the guarantee rather than hiding it.

It nearly did not work, and would not have said so. Wolverine writes the trace
context to a Kafka header called `parent-id` — W3C `traceparent` format, under a
name that is not `traceparent`. OpenTelemetry's Java instrumentation reads the
standard name, finds nothing, and starts a new trace. Both services report
spans. Both look healthy. Every request lives in two traces.

The fix belongs on the producer: W3C Trace Context exists so a consumer does not
need to know what produced a message.

→ [ADR 0007: One trace across two runtimes, and the header that nearly stopped it](docs/adr/0007-one-trace-across-two-runtimes.md)

## One address for telemetry, two backends behind it

Every service exports OTLP to a collector, which sends traces to Jaeger and
holds metrics for Prometheus to scrape.

That was not the first design. Each service exported straight to Jaeger, which
is fine for traces and silently wrong for metrics: Jaeger is a trace store. The
Java service was caught pushing metrics at it and getting a 404 once a minute.
The .NET services were quieter — they had no metrics endpoint at all, so theirs
reached nothing, and the only symptom was Prometheus listing them as `down`.

The local fix would have been a `/metrics` endpoint in .NET, whose package is
still prerelease. The collector is the fix that does not need one, and makes
both runtimes do the same thing.

→ [ADR 0008: A collector after all](docs/adr/0008-a-collector-after-all.md),
which revisits the alternative [ADR 0007](docs/adr/0007-one-trace-across-two-runtimes.md)
rejected when there was only one backend.

## What goes on the wire

Wolverine puts the message body on Kafka as plain JSON with no envelope around
it, which is what makes a Java consumer possible without a shared library:

```
{"orderId":"01a020d4-…","customerId":"acme","currency":"TRY","total":59.7000,"lineCount":1,"placedAt":"…"}
```

Every header is written deliberately by `OrderEventKafkaMapper` rather than
inherited from the framework's defaults, because this is a cross-language
contract and a contract nobody wrote down is whatever the library happened to do
that release:

```
content-type: application/json
event-type:   order.placed
traceparent:  00-5c7af58143f340974623d19af986fd47-…-01
```

`event-type` rather than `message-type: Orders.Domain.OrderPlaced` — a consumer
in another language has no business knowing this one's namespace, and moving a
class here should not rename anything there.

## Across the boundary

An order placed against the .NET service becomes a notification in the Java one,
with no shared library and no code generation between them. The only thing
holding the two together is the JSON on the topic, which is the point: the Java
records are hand-written from the contract, so a field renamed on the .NET side
does not fail to compile — it fails at runtime, and a test is what catches it.

`total` crosses as `99.9000` and is read into a `BigDecimal`. A `double` would
not hold it, on either side.

Delivery is at-least-once, so the consumer has to be idempotent. It claims each
event in Redis under the business fact — the order id and what happened to it —
rather than under the producer's envelope id, which would only recognise a
redelivery of that exact message. Measured on a run where the same order was
confirmed twice:

```
orders.confirmed events on the topic     2
notifications_handled_total              2      (placed + confirmed)
notifications_suppressed_total           1
```

Which raised the better question: why were there two events? Confirming an
already-confirmed order changed nothing, and the service announced it anyway —
an event claiming a confirmation at a time the order was not confirmed. The
consumer deduplicated it, and a safety net catching a lie does not make it
true. `Order.Confirm` now reports whether anything changed, and the handler
returns an empty `OutgoingMessages` when it did not. The retry still answers
200; it just no longer announces anything.

→ [ADR 0006: Idempotency keyed on the business fact](docs/adr/0006-idempotent-consumers.md),
which also records what happens when Redis is down — the consumer stalls for a
minute per message, loses nothing, and reports itself healthy the whole time.
The first two are acceptable and the third is not.

## The edge

`gateway` is YARP 2.3: one address in front of the services, rate limiting, and
destination health.

Its resilience is not a retry policy, and that was a correction rather than a
choice. The plan called for `Microsoft.Extensions.Http.Resilience`, which hangs
Polly pipelines off `IHttpClientFactory` — and YARP forwards through its own
`IForwarderHttpClientFactory`, so the extension methods have nothing to attach
to. Injecting a handler anyway is possible and is the wrong shape: retrying at
that layer sends the same request back to the destination that just failed. A
proxy should send it elsewhere and remember the first one is sick.

So: active probes, passive marking, a reactivation period. Measured with the
orders service stopped —

```
attempt 1: HTTP 502  (0.009s)
attempt 2: HTTP 502  (0.003s)
attempt 3: HTTP 502  (0.003s)
```

— fast failure rather than a hang, and 200s again within two seconds of the
service coming back.

Rate limiting is a fixed window per client address. 150 concurrent requests
against a 100-per-10-second window:

```
100  200
 50  429      each carrying Retry-After: 10
```

The package that could not be used was removed from the version list rather than
left in place looking used.

→ [ADR 0004: YARP at the edge, and resilience that is not a retry policy](docs/adr/0004-yarp-and-what-resilience-means-at-the-edge.md)

## Running `orders` on its own

It is not in the compose file yet — that comes with its container image, in the
messaging milestone. Until then:

```sh
docker compose up -d postgres kafka otel-collector
dotnet ef database update --project services/orders/src/Orders \
  --connection "Host=localhost;Port=5432;Database=orders;Username=orders;Password=orders"
ConnectionStrings__Orders="Host=localhost;Port=5432;Database=orders;Username=orders;Password=orders" \
Kafka__BootstrapServers="localhost:29092" \
Otlp__Endpoint="http://localhost:4317" \
  dotnet run --project services/orders/src/Orders
```

The service refuses to start without that connection string rather than falling
back to a default, for the same reason the compose file has no datasource
guesses in it.

```sh
dotnet test          # 13 domain tests, no database, no containers
```

## Running the gateway

```sh
dotnet run --project services/gateway/src/Gateway     # http://localhost:5178
curl -X POST localhost:5178/orders -H 'Content-Type: application/json' -d '…'
```

It proxies `/orders/**` to the orders service and needs it running.

## Running `notifications`

```sh
docker compose up -d kafka redis otel-collector
cd services/notifications
KAFKA_BOOTSTRAP_SERVERS=localhost:29092 REDIS_HOST=localhost \
  OTLP_ENDPOINT=http://localhost:4318 ./gradlew bootRun
```

```sh
curl localhost:8080/notifications          # what it would have sent
curl localhost:8080/actuator/prometheus    # handled and suppressed counters
```

Two Spring Boot 4 changes worth knowing before they cost an afternoon. Jackson 3
moved its package from `com.fasterxml.jackson` to `tools.jackson` — only the
annotations stayed. And auto-configuration now lives in per-technology
artifacts: `org.springframework.kafka:spring-kafka` puts the classes on the
classpath but registers no listener container, so `@KafkaListener` is ignored
and the service starts perfectly while consuming nothing. The starter is
`org.springframework.boot:spring-boot-starter-kafka`.

## Tests

```sh
dotnet test                       # 16: 13 domain, 3 against real containers
cd services/notifications && ./gradlew build
```

The 13 run in milliseconds against nothing — a type that needs infrastructure to
prove its own rules has the rules in the wrong place. The 3 start Postgres and
Kafka and take about three minutes, and what they assert is mostly the wire
contract: `event-type: order.placed` rather than a .NET type name, a
`traceparent` header in W3C form, `59.7000` surviving the round trip at its
scale, and one `orders.confirmed` event for two confirmations.

They check the contract because the contract was wrong for several commits, and
a mocked broker would have agreed with it.

→ [ADR 0009: Real containers at the boundaries, not mocks](docs/adr/0009-testcontainers-over-mocks.md)

CI runs both stacks, then builds all three images and scans them — a Dockerfile
that only works on the author's machine fails there rather than on the first
person to clone it.

## Under load

```sh
docker compose up -d
dotnet run --project services/orders/tests/Orders.LoadTests
```

Injected arrival rates rather than a thread count — what a service experiences
is requests arriving, not workers spinning. One machine, Docker Desktop,
everything on one laptop:

| | rate | p50 | p95 | p99 | max | failed |
|---|---|---|---|---|---|---|
| `place_order` | 20→60/s | 2.9 ms | 7.0 ms | 9.4 ms | 14 ms | 0 |
| `read_order` | 100/s | 1.7 ms | 4.9 ms | 8.3 ms | 12 ms | 0 |

`place_order` writes the order, its line and an outbox envelope in one
transaction, so every order placed is also a message durably stored. That it
costs about a millisecond more at p50 than a read is the price of ADR 0002,
measured.

The profile runs against the orders service directly, not the gateway. The
gateway rate-limits to 100 requests per 10 seconds per client, so load through
it measures the limiter — which is the limiter working, and not a fact about the
service. Point `ORDERS_URL` at port 8080 to watch the 429s arrive on schedule
instead.

The first run of this reported a read p99 of 80 ms against a p50 of 1.4 ms,
which was the measurement and not the service: warming through `/health` jitted
the health endpoint and left each scenario paying its first-call cost inside the
measured window. Each scenario warms its own path now, and the read maximum went
from 295 ms to 12 ms.

Reports land in `load-reports/` as HTML, Markdown and CSV, and are gitignored —
they are numbers from one machine on one day.

## On a cluster

```sh
docker compose build
kubectl apply -f deploy/k8s/
kubectl -n polyglot-reference wait --for=condition=complete --timeout=180s job/orders-migrate
```

Verified on Docker Desktop's Kubernetes 1.34: the same request through the same
gateway produces the same seven-span trace across the same three services.

Migrations run in a Job rather than on startup, which is what the comment in
`Program.cs` has been pointing at — every replica migrating is a race that EF's
advisory lock survives and startup does not.

Three things worked in compose and failed here, none of them saying why:
Kafka could not reach its own controller through a Service that forwards one
port; Spring refused to start because Kubernetes injects `REDIS_PORT` as a URL
where it wants an integer; and a second `notifications` replica would have been
an idle pod, because the consumer group divides partitions and there is one.

→ [deploy/k8s/README.md](deploy/k8s/README.md)

## Versions

Pinned as of August 2026 and chosen deliberately: .NET 10 LTS, EF Core 10,
Spring Boot 4.1, Kafka 4.3, Postgres 18, Redis 8.2, YARP 2.3, Jaeger 2.10,
xUnit v3, Wolverine.

# Seeing it work

Every claim in the README, checked from a terminal in about fifteen minutes.
Nothing here is a demo script: each step is a thing that could be false, and
what to look at to find out.

Numbers below are from a MacBook Pro running Docker Desktop. Yours will differ;
the shapes should not.

## 1. Cold start

```sh
docker compose up -d --build
docker compose ps
```

Ten containers, three of them built here. All should reach `healthy`. First run
builds the images and takes a few minutes; after that it is under a minute.

**What it proves:** the setup story is one command. Services wait for what they
need to be *healthy* rather than merely started — Kafka accepts connections well
before it accepts a produce, and a service that starts into that window fails in
a way that reads like a bug in the service.

## 2. Place an order through the gateway

```sh
curl -X POST localhost:8080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"acme","currency":"TRY","lines":[
       {"sku":"WIDGET-1","quantity":3,"unitPrice":19.90},
       {"sku":"BOLT-9","quantity":10,"unitPrice":2.55}]}'
```

```json
{"orderId":"01a02148-794b-73b6-9a41-164fd33b0c55","total":85.2000,"currency":"TRY"}
```

Keep that `orderId`. Later steps need it.

`85.2000` is 3 × 19.90 + 10 × 2.55, at the scale of the column that stores it.
The four decimal places are deliberate: a price given as `19.90` would otherwise
serialise as `85.20` when created and `85.2000` when read back, and a client
comparing the two would see a change that did not happen.

## 3. The Java service, across the boundary

```sh
curl localhost:8082/notifications
```

The same order id, in a notification produced by a Spring Boot service that
shares no library and no generated code with the .NET one. The only thing
holding them together is the JSON on the topic.

## 4. Refusals

```sh
curl -X POST localhost:8080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"a","currency":"TRY","lines":[]}'

curl -X POST localhost:8080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"a","currency":"TRY","lines":[{"sku":"X","quantity":1,"unitPrice":1.23456}]}'
```

Both `422`, both with a `detail` naming what to change. The second is the more
interesting one: a price finer than its currency is refused rather than rounded,
because the digits that would be dropped are somebody's money.

They are 422 and not 400 — the body parsed and the types were right. What failed
is the rule.

Each problem document carries a `traceId`. Paste the middle section into
Jaeger's *Lookup by Trace ID* and the failed request has a trace too.

## 5. Confirm twice, announce once

```sh
ID=<the orderId from step 2>

curl -X POST localhost:8080/orders/$ID/confirmation
curl -X POST localhost:8080/orders/$ID/confirmation
sleep 8

echo "orders.confirmed events for this order: $(
  docker compose exec -T kafka /opt/kafka/bin/kafka-console-consumer.sh \
    --bootstrap-server localhost:9092 --topic orders.confirmed \
    --from-beginning --timeout-ms 12000 2>/dev/null | grep -c "$ID"
)"
```

Two `200`s and **one** event.

The caller asked for a state and got it, both times — a retry should not have to
distinguish "failed" from "already done". But the second call changed nothing,
so nothing was announced. An event saying an order was confirmed at a time it
was not is a false statement on a topic other services act on, and a consumer
deduplicating it does not make it true.

The consumer deduplicates anyway, because delivery is at-least-once:

```sh
curl -s localhost:8082/notifications | grep -o '"kind":"[^"]*"'
```

One `order-placed`, one `order-confirmed`.

> `ID` is the **order** id, not the `traceId` from step 4's error body. The route
> takes a GUID, so a trace id gives a 404 and an empty count.

## 6. Rate limiting

```sh
seq 1 150 | xargs -P 20 -I{} curl -s -o /dev/null -w '%{http_code}\n' \
  localhost:8080/orders/00000000-0000-0000-0000-000000000000 | sort | uniq -c
```

```
 100 404
  50 429
```

Exactly the configured window: 100 permits per 10 seconds, per client address.
The 404s are the requests that got through — that order id does not exist, which
is the point of using it.

Each 429 carries `Retry-After: 10`. A 429 without one leaves clients to guess,
and they guess immediately.

> The requests have to be concurrent. Sequential `curl` calls are slower than
> the window, so the limit never fills and everything returns 404.

## 7. The outbox, with the broker down

```sh
sleep 11          # let the rate-limit window clear
./ops/prove-outbox.sh
```

Stops Kafka, posts an order, shows the order committed and its event waiting in
`wolverine.wolverine_outgoing_envelopes`, starts Kafka, shows the row clear and
the event arrive on the topic.

**What it proves:** an order and its event commit together or neither does. If
the outbox were not durable, the order would be in the database and its event
would be gone — permanently, with nothing anywhere to say so. That is not
hypothetical: it is what this system did until the third line of
`opts.Policies.UseDurableOutboxOnAllSendingEndpoints()` was added, and stopping
the broker is how it was found.

Note the outbox row: the message type is `order.placed`, not
`Orders.Domain.OrderPlaced`. The neutral name reaches all the way down.

## 8. When a destination dies

```sh
docker compose stop orders
curl -w '\n%{http_code} — %{time_total}s\n' \
  localhost:8080/orders/00000000-0000-0000-0000-000000000000
docker compose start orders
```

`502` in under twenty milliseconds — fast failure, not a hang. Within a few
seconds of the service returning, the gateway serves it again.

The gateway's resilience is destination health, not request retries. Retrying at
the proxy sends the same request back to the destination that just failed; what
a proxy should do is go elsewhere and remember the first one is sick. See
[ADR 0004](adr/0004-yarp-and-what-resilience-means-at-the-edge.md).

## 9. One trace, two runtimes

```sh
sleep 10
curl -X POST localhost:8080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"trace","currency":"TRY","lines":[{"sku":"T","quantity":1,"unitPrice":1.00}]}'
sleep 20
open http://localhost:16686
```

In Jaeger, set **Service** to `gateway` and **Operation** to
`POST /orders/{**catch-all}`, then *Find Traces*. Open the most recent one.

The header should read **Services 3 · Depth 6 · Total Spans 7**, and the
waterfall:

```
gateway        POST /orders/{**catch-all}
  gateway      POST                          ← YARP forwarding
    orders     POST /orders/
      orders   Orders.Features.PlaceOrder    ← the Wolverine handler
      orders   send                          ← written to the outbox, 122µs
      notifications  orders.placed process   ← the Java consumer
        notifications → Redis  set           ← the idempotency claim
```

Without the `Service`/`Operation` filter the list is mostly single-span traces
from step 6 — the rate-limited requests, which never reached `orders` at all.
That is itself worth seeing: the limit cuts at the edge, and the service behind
it never pays for the load.

**What it proves:** the trace context crosses a runtime boundary. Wolverine
writes it to a Kafka header called `parent-id`, in W3C `traceparent` format
under a name that is not `traceparent`; OpenTelemetry's Java instrumentation
reads the standard name, finds nothing, and starts a fresh trace. Both services
report spans, both look healthy, and every request lives in two traces. The
producer now writes the standard header. See
[ADR 0007](adr/0007-one-trace-across-two-runtimes.md).

### Reading the timings

On a first request after `docker compose start orders`, expect something like
997 ms total with `POST /orders/` at 731 ms. That is a cold start: Wolverine
compiles its handlers on first use, EF builds its model, the connection pool
opens. Place a second order and compare — the same trace shape came back at
319 ms total with `POST /orders/` at 50 ms.

Of that 50 ms, about 45 is the handler writing the order, its line, and the
outbox envelope in one transaction and committing. On Docker Desktop those
writes go through a VM filesystem layer where fsync is expensive; measure on
real hardware before drawing conclusions from it.

The remaining ~265 ms before the consumer's span is the outbox sweep. It is not
latency to fix. The event is not sent during the request — it is written to
Postgres inside the order's transaction and forwarded afterwards, and the gap is
the cost of the guarantee in [ADR 0002](adr/0002-transactional-outbox-over-dual-write.md),
shown rather than hidden.

## 10. Metrics

```sh
open http://localhost:9090/targets      # prometheus and services, both up
open http://localhost:3000              # Grafana, datasources already provisioned
```

Both runtimes' metrics arrive by the same route: every service exports OTLP to
the collector, which holds them for Prometheus to scrape. Try
`notifications_handled_total` and `aspnetcore_rate_limiting_requests_total` —
one from Java, one from .NET, no per-runtime scrape endpoint on either.

## 11. Stop

```sh
docker compose down -v
```

---

## What this did not check

The load profile and the Kubernetes manifests, because they do not exist yet —
see the status table in the README.

`notifications_suppressed_total` stays at `0` through this walkthrough, and that
is correct. Suppression only fires on a genuine duplicate delivery, and the
producer no longer emits an event for a confirmation that changed nothing. To
see the counter move you would have to replay the topic.

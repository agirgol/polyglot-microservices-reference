# Kubernetes

The same system as `docker compose up`, on a cluster. Verified on Docker
Desktop's single-node Kubernetes 1.34.

```sh
docker compose build                     # the images this deploys
kubectl apply -f deploy/k8s/
kubectl -n polyglot-reference wait --for=condition=complete --timeout=180s job/orders-migrate
kubectl -n polyglot-reference get pods

curl -X POST localhost:8080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"acme","currency":"TRY","lines":[{"sku":"W","quantity":2,"unitPrice":25.00}]}'
```

`imagePullPolicy: Never`, because the images are built locally and pushed
nowhere. A cluster that pulls will look for them in a registry and fail.

## What is here, and what should not be

`20-infrastructure.yaml` runs Postgres, Kafka and Redis as single replicas with
`emptyDir` volumes. That is so the services have something to talk to on a
laptop, and it is not how any of them should be run: a real deployment uses an
operator — Strimzi, CloudNativePG — or a managed service, because failover,
rebalancing, backup and upgrade are exactly what a Deployment does not do.

It is a separate file so that pointing at real infrastructure means deleting one
file and editing the addresses in `10-config.yaml`.

Prometheus and Grafana are not here. They are how telemetry is looked at rather
than part of the system, and a cluster that would run this already has them.

## Migrations run once

`orders-migrate` is a Job. Every replica applying migrations on startup is a
race: all of them see pending migrations, all try, EF's advisory lock stops that
corrupting anything, and the losers block their own startup on a migration they
are not running. `Migrations__ApplyOnStartup` is `false` in the ConfigMap for
that reason.

The Job runs the same image with `args: ["migrate"]` — no second image, no
migration bundle. That command builds a host containing a DbContext and nothing
else, which is a correction: the first version ran after the application had
been built, so the Job also started a message bus, elected itself leader, and
sat retrying a Kafka broker it had no business talking to. It never exited.

## Three things that differ from compose

Each of these worked in compose and failed here, and each failed in a way that
did not name its cause.

**Kafka could not find its own controller.** `KAFKA_CONTROLLER_QUORUM_VOTERS`
pointed at `kafka:9093`, and a Service only forwards the ports it declares —
9092. The broker timed out registering with itself and exited 1. In compose,
containers reach each other on any port. Single-node KRaft should use
`localhost:9093`; the controller is in the pod.

**Spring would not start because of a variable nobody set.** Kubernetes injects
`REDIS_PORT=tcp://10.96.0.1:6379` for every Service in the namespace, a
convention from before cluster DNS. Spring reads `spring.data.redis.port` from
`REDIS_PORT` and refuses to bind a URL to an int. `enableServiceLinks: false` on
the pods removes those variables; nothing here discovers services that way.

**Two replicas of `notifications` would be one replica and one idle pod.** They
share a consumer group, the group divides partitions, and there is one partition
per topic. It is set to one replica with a comment, rather than left at two
looking scaled. See [ADR 0003](../../docs/adr/0003-kafka-for-an-estate-with-two-runtimes.md).

## Probes

`readinessProbe` and `livenessProbe` hit the same endpoint with different
patience. Readiness taking a pod out of rotation is cheap and reversible;
liveness restarts it, and a restart during a slow moment makes the slow moment
worse — so liveness is slower to fire and tolerates more failures.

The notification service's readiness probe has a known problem, recorded in
[ADR 0006](../../docs/adr/0006-idempotent-consumers.md) and not fixed here: its
health endpoint waits on Redis, and Lettuce's command timeout is measured in
tens of seconds. A Redis outage stalls the consumer while the probe is still
answering.

## Teardown

```sh
kubectl delete namespace polyglot-reference
```

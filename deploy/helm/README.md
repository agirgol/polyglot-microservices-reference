# Helm

The same manifests as `deploy/k8s/`, with the one thing plain manifests cannot
express: an ordering.

```sh
docker compose build
helm install polyglot deploy/helm/polyglot-reference \
  --namespace polyglot --create-namespace --wait

curl -X POST localhost:8080/orders -H 'Content-Type: application/json' \
  -d '{"customerId":"acme","currency":"TRY","lines":[{"sku":"W","quantity":3,"unitPrice":11.00}]}'
```

Verified on Docker Desktop's Kubernetes 1.34, install and upgrade.

## What the chart is for

Migrations. `orders-migrate` is a Helm hook, so on every upgrade Helm runs the
Job and waits for it to complete before it rolls the Deployments. That is the
case that matters: an upgrade is when a schema change and a code change arrive
together, and getting the order wrong means new code against an old schema.

A directory of manifests cannot say that. You apply the Job, watch it, then
apply the rest — which is a runbook, and runbooks are followed inconsistently at
three in the morning.

Confirmed on `helm upgrade`, from the namespace's events:

```
SuccessfulCreate   job/orders-migrate    Created pod: orders-migrate-hzgl5
Completed          job/orders-migrate    Job completed
```

The Job is gone afterwards — `hook-delete-policy: hook-succeeded`. A *failed*
one is deliberately left behind, because its logs are the only thing that
explains why the release stopped.

## Two things this took to get right

**A pre-install hook runs before the chart's own resources exist.** The Job used
`envFrom: orders-config`, and that ConfigMap is an ordinary chart resource — so
the pod sat in `CreateContainerConfigError` with "configmap not found" until the
release timed out, and the release never got far enough to create it. The Job
now carries its own connection string, which is also the honest shape: a
migration wants a database, not the settings of a service it is not starting.

**With `infrastructure.enabled=true` the hook cannot be `pre-install` at all**,
because the Postgres it would migrate is also a chart resource. The annotation
is conditional: `post-install` when this chart brings its own database,
`pre-install` when it does not. The consequence of the first is visible on a
fresh install — the services start before the schema exists, answer `/health`
without it, and restart once or twice until the Job finishes. That is the price
of bundling a database with the application that uses it, and it is why the
production shape is `infrastructure.enabled=false`.

## Values worth knowing

| | |
|---|---|
| `infrastructure.enabled` | `true` bundles Postgres, Kafka and Redis for a laptop. `false` points at whatever is running them properly, via the addresses below it. |
| `telemetry.enabled` | `false` leaves the OTLP endpoint empty, and the services skip exporting rather than retrying against nothing. |
| `replicas.notifications` | Stays at 1 until the topics have more than one partition. Replicas share a consumer group and the group divides partitions; a second one is assigned nothing and idles while looking healthy. |
| `images.pullPolicy` | `Never`, because these images are built locally and pushed nowhere. |

## Teardown

```sh
helm uninstall polyglot -n polyglot
kubectl delete namespace polyglot
```

# ADR 0004: YARP at the edge, and resilience that is not a retry policy

**Status:** Accepted
**Date:** 2026-08-21

## Context

Something has to sit in front of the services: one address for callers, one
place for rate limiting, one place where a request is first seen and last seen.
In .NET the two candidates are Ocelot and YARP.

The plan also called for `Microsoft.Extensions.Http.Resilience` here. That turned
out not to be a thing you can do, and the reason is more interesting than the
package.

## Decision

**YARP 2.3** as the gateway.

Its resilience is destination health, not request retries: active probes against
each destination's `/health`, passive marking on transport failures, and a
reactivation period after which a destination is tried again.

Rate limiting uses ASP.NET Core's built-in partitioned limiter, keyed per client
rather than globally.

## Why not `Microsoft.Extensions.Http.Resilience`

It is the current, correct answer for a service calling another service over
`HttpClient` — Polly v8 pipelines attached through `IHttpClientFactory`.

YARP does not use `IHttpClientFactory`. It forwards through its own
`IForwarderHttpClientFactory`, so the resilience extension methods have nothing
to attach to.

The workaround — injecting a Polly handler into YARP's forwarder — is available
and is the wrong shape anyway. A retry at that layer sends the same request to
the *same destination* that just failed. What a proxy should do is send it
somewhere else and remember that the first one is unhealthy. Retrying a dead
destination faster is not resilience; it is load.

Nothing else in this system makes service-to-service HTTP calls, so the package
is not referenced anywhere. It was removed from the version list rather than
left in place looking used.

## Consequences

**A dead destination fails fast and says so.** Measured with the orders service
stopped: 502 in 2–9 ms rather than a hang, while the active probe keeps testing.
When the service came back the gateway was serving 200s again within two
seconds.

**No cross-destination failover yet, because there is one destination.** The
health checking is configured and correct; it has nothing to fail over *to*.
That becomes real when a second replica exists, and the configuration does not
change when it does.

**Rate limiting is keyed on the socket address.** Correct only because nothing
sits in front of this gateway. Behind a load balancer the key has to come from a
forwarded header that the balancer sets and the client cannot spoof, or every
caller shares one bucket.

**429 carries `Retry-After`.** Measured: 150 concurrent requests against a
100-per-10s window gave exactly 100 × 200 and 50 × 429, each with
`Retry-After: 10`. A 429 without it leaves clients to guess, and they guess
immediately.

## Alternatives considered

**Ocelot.** The older .NET gateway, and still maintained. Rejected on direction
rather than defect: YARP is Microsoft's, is what Azure's own edge is built from,
and its configuration model maps onto ASP.NET Core middleware the rest of this
repository already uses. Ocelot's feature set is broader out of the box —
aggregation, its own rate limiter — which is a reason to prefer it if those are
needed and a reason to avoid it if they are not.

**Envoy or NGINX.** Both better proxies than either .NET option, and both mean
the edge is configured in a language nothing else here is written in. For an
estate this size that trade is not worth it. At a larger one it would be, and
this is the decision to revisit first.

**No gateway; call the services directly.** Defensible for two services. It puts
rate limiting inside each service, where the request has already cost what it
was going to cost.

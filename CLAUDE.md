# Working agreements

## Scope

**Three services. Do not propose a fourth.** `orders` (.NET), `notifications`
(Java), `gateway` (.NET). A fourth service would demonstrate nothing the third
does not, and would double the surface that has to keep working.

**Ask before adding a pattern.** Not before adding a test, a health check, or a
log line — before adding a concept a reader has to learn to follow the code:
a new abstraction layer, a second persistence strategy, an event-sourced
aggregate, a saga, a service mesh.

**The ADRs are the deliverable.** The code exists to make the decisions in
`docs/adr/` concrete and checkable. When the two disagree, the ADR is wrong or
the code is — say which, and fix that one. Do not let code accumulate that no
ADR accounts for.

## What this repository is proving

That architectural choices were made rather than defaulted into, and that the
consequences were measured. A reviewer should be able to read one ADR, open the
code it describes, and find exactly what the ADR said would be there —
including the parts that turned out worse than hoped.

The rejected alternatives are the point. An ADR that only lists what was chosen
is a description, not a decision.

## Claims

No claim in a README or an ADR without something that checks it. If the text
says a trace spans both stacks, there is a test or a screenshot that shows one
trace ID crossing them. If it says the outbox survives a crash, something kills
the process mid-transaction and asserts the message still arrives.

This is the standing rule, not a nice-to-have: a claim nothing verifies is the
defect, whatever the code does.

## Versions

Pinned deliberately, current as of August 2026: .NET 10 LTS, EF Core 10,
Spring Boot 4.1, Kafka 4.3, Postgres 18, YARP 2.3, xUnit v3, Wolverine.
Do not bump one to "latest" as a side effect of another change.

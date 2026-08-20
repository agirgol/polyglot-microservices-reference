# Working agreements

## Scope

**Three services. A fourth needs an ADR arguing for it.** `orders` (.NET),
`notifications` (Java), `gateway` (.NET). A fourth would demonstrate nothing the
third does not, and would double the surface that has to keep working.

**Raise a new pattern before adding it.** Not a test, a health check, or a log
line — a concept a reader has to learn to follow the code: another abstraction
layer, a second persistence strategy, an event-sourced aggregate, a saga, a
service mesh.

**The ADRs are the deliverable.** The code exists to make the decisions in
`docs/adr/` concrete and checkable. When the two disagree, one of them is wrong
— say which, and fix that one. Code that no ADR accounts for should not
accumulate.

## What this repository is proving

That architectural choices were made rather than defaulted into, and that the
consequences were measured. A reviewer should be able to read one ADR, open the
code it describes, and find exactly what the ADR said would be there —
including the parts that turned out worse than hoped.

The rejected alternatives are the point. An ADR that only lists what was chosen
is a description, not a decision.

## Claims

No claim in a README or an ADR without something that checks it. If the text
says a trace spans both stacks, there is a test or a captured trace that shows
one trace id crossing them. If it says the outbox survives a broker outage,
something stops the broker and asserts the message still arrives — see
`ops/prove-outbox.sh`.

This is the standing rule, not a nice-to-have: a claim nothing verifies is the
defect, whatever the code does.

## Records

An ADR is never edited to reflect a change of mind. When a decision is replaced,
its status becomes `Superseded by ADR-NNNN` and a new record explains what
changed. ADR 0008 revising ADR 0007 is the worked example: the earlier reasoning
was right about the system it described, and is left as written.

## Versions

Pinned deliberately, current as of August 2026: .NET 10 LTS, EF Core 10,
Spring Boot 4.1, Kafka 4.3, Postgres 18, YARP 2.3, xUnit v3, Wolverine.
Do not bump one to "latest" as a side effect of another change. Versions live in
`Directory.Packages.props` and `services/notifications/gradle/libs.versions.toml`,
nowhere else.

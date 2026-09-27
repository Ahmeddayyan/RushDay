# 2. .NET 10 with ASP.NET Core minimal APIs

Date: 2026-09-27

## Status

Accepted

## Context

The project needs a current, widely used backend stack that employers recognise, with strong async
support, since the whole point is behaviour under concurrent load.

## Decision

Target .NET 10 (the current long-term-support release). Use ASP.NET Core minimal APIs organised into
endpoint classes per resource, typed results for explicit status codes, and EF Core 10 for data access.

## Consequences

- Minimal APIs keep the request pipeline visible, which matters when reasoning about latency.
- `TypedResults` make every status code a compile-time fact, so tests and OpenAPI stay honest.
- Warnings are errors and nullable reference types are on from day one.

# 1. Record architecture decisions

Date: 2026-09-27

## Status

Accepted

## Context

RushDay exists to show engineering judgement, not just working code. Every non-obvious choice should be
traceable to a reason, and every performance fix should be traceable to a measurement.

## Decision

Record architecture decisions as short Markdown files in `docs/adr/`, numbered in order. Each records
context, the decision, and its consequences. Performance-related ADRs link to the k6 run that motivated them.

## Consequences

Slightly more writing per change. In return, the repository tells its own story to a reviewer.

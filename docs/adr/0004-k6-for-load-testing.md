# 4. Grafana k6 for load testing

Date: 2026-09-27

## Status

Accepted

## Context

We need repeatable load scenarios with clear pass/fail thresholds and exportable numbers, so that
"before" and "after" can be quoted side by side.

## Decision

Use k6. Scenarios are JavaScript files in `load/k6/`. The results-day scenario uses an arrival-rate
executor because a real spike is defined by requests arriving per second, not by a fixed user count.
Every run exports a JSON summary to `load/results/`.

## Consequences

- One small tool to install, scriptable, runs the same on a laptop and in CI.
- Thresholds encode the service-level targets the fixes are measured against.

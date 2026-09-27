# 5. Build a naive baseline before optimising anything

Date: 2026-09-27

## Status

Accepted

## Context

The purpose of RushDay is to show *why* university portals fall over on results day and *how* each
class of failure is fixed. Starting from an already-hardened design would leave nothing to demonstrate.

## Decision

Version 0 is written the way a first, straightforward implementation usually is:

- The dashboard endpoint loads a student's data one query at a time.
- Enrolment reads the current count, decides, then writes, with no concurrency control.
- The module catalogue is fetched from the database on every request.
- No caching, no rate limiting, no request timeouts, default connection pool.

This baseline is tagged `v0-naive`. Each later change must cite a load run that shows the problem and
a load run that shows the improvement.

## Consequences

- Early commits contain code we know is wrong under load. That is deliberate and documented here.
- Reviewers can diff any fix against the baseline and read the numbers that justified it.

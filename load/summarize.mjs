#!/usr/bin/env node
// Reads load/results/runs.json and each k6 JSON summary it references, and writes
// src/RushDay.Web/public/data/load-results.json in the shape the SPA's /story and /admin/ops pages
// read (05-frontend.md section 8, 04-performance-and-ops.md section 8).
//
//   node load/summarize.mjs           regenerate and write the committed JSON
//   node load/summarize.mjs --check   exit non-zero when the committed JSON is out of date (CI)
import { readFile, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const here = path.dirname(fileURLToPath(import.meta.url))
const resultsDir = path.join(here, 'results')
const runsPath = path.join(resultsDir, 'runs.json')
const outputPath = path.join(here, '..', 'src', 'RushDay.Web', 'public', 'data', 'load-results.json')

// This is a description for the reader, not a secret or a machine identifier: keep it generic.
const MACHINE = 'Developer laptop: Windows 11, .NET 10, PostgreSQL 18 (native)'

const checkOnly = process.argv.includes('--check')

/** Builds one LoadRun (05-frontend.md section 8) from a runs.json entry and its k6 JSON summary. */
function summarise(entry, result) {
  const metrics = result.metrics ?? {}
  const duration = metrics.http_req_duration ?? {}
  const failed = metrics.http_req_failed ?? {}
  const reqs = metrics.http_reqs ?? {}
  const dropped = metrics.dropped_iterations

  const run = {
    id: path.basename(entry.file, '.json'),
    scenario: entry.scenario,
    version: entry.version,
    label: entry.label,
    ranAt: entry.ranAt,
    source: `load/results/${entry.file}`,
  }
  if (entry.targetRate !== undefined) run.targetRate = entry.targetRate
  if (entry.mode !== undefined) run.mode = entry.mode
  if (entry.notes !== undefined) run.notes = entry.notes

  run.metrics = {
    requests: reqs.count ?? 0,
    failedRate: failed.value ?? 0,
    p50Ms: duration.med ?? 0,
    p95Ms: duration['p(95)'] ?? 0,
    maxMs: duration.max ?? 0,
  }
  // p99Ms is optional for every run: the committed v0 enrolment-rush and results-day summaries did
  // not set summaryTrendStats: [..., 'p(99)'], so it is only emitted when the field exists.
  if (duration['p(99)'] !== undefined) run.metrics.p99Ms = duration['p(99)']
  if (reqs.rate !== undefined) run.metrics.achievedRate = reqs.rate
  if (dropped?.count !== undefined) run.metrics.droppedIterations = dropped.count
  if (metrics.enrolments_accepted) run.metrics.accepted = metrics.enrolments_accepted.count
  if (metrics.enrolments_rejected_full) run.metrics.rejectedFull = metrics.enrolments_rejected_full.count
  if (metrics.enrolments_errored) run.metrics.errored = metrics.enrolments_errored.count
  if (metrics.shed_503) run.metrics.shed = metrics.shed_503.count
  if (metrics.logins_ok) run.metrics.loginsOk = metrics.logins_ok.count
  if (metrics.logins_rate_limited) run.metrics.loginsRateLimited = metrics.logins_rate_limited.count

  return run
}

function normalise(text) {
  return text?.replace(/"generatedAt": ".*?"/, '"generatedAt": ""') ?? null
}

async function main() {
  const runEntries = JSON.parse(await readFile(runsPath, 'utf8'))

  const runs = []
  for (const entry of runEntries) {
    const result = JSON.parse(await readFile(path.join(resultsDir, entry.file), 'utf8'))
    runs.push(summarise(entry, result))
  }

  const output = { generatedAt: new Date().toISOString(), machine: MACHINE, runs }
  const json = JSON.stringify(output, null, 2) + '\n'

  if (checkOnly) {
    const existing = await readFile(outputPath, 'utf8').catch(() => null)
    if (normalise(existing) !== normalise(json)) {
      console.error(
        'public/data/load-results.json is out of date. Run `node load/summarize.mjs` and commit the result.',
      )
      process.exitCode = 1
      return
    }
    console.log('public/data/load-results.json is up to date.')
    return
  }

  await writeFile(outputPath, json)
  console.log(`Wrote ${path.relative(process.cwd(), outputPath)} from ${runs.length} run(s).`)
}

main().catch((error) => {
  console.error(error)
  process.exitCode = 1
})

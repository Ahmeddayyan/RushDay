#!/usr/bin/env node
// Fails the build when the part of the bundle every visitor downloads grows past budget, or when a
// route meant to stay lazy (recharts on /story and /admin/ops, qrcode on the MFA setup page) leaks
// into the code every visitor downloads. Reads wwwroot/.vite/manifest.json (05-frontend.md section 2
// and section 14).
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { gzipSync } from 'node:zlib'

const here = path.dirname(fileURLToPath(import.meta.url))
const wwwroot = path.resolve(here, '..', '..', 'RushDay.Api', 'wwwroot')
const manifestPath = path.join(wwwroot, '.vite', 'manifest.json')
const BUDGET_BYTES = 220 * 1024

async function gzipSize(relativeFile) {
  const buffer = await readFile(path.join(wwwroot, relativeFile))
  return gzipSync(buffer).length
}

function findEntry(manifest) {
  const entry = Object.values(manifest).find((chunk) => chunk.isEntry)
  if (!entry) throw new Error('No entry chunk found in the Vite manifest.')
  return entry
}

/** Every manifest chunk statically reachable from the entry, following `imports` but not `dynamicImports`. */
function staticGraph(manifest, entry) {
  const seen = new Set()
  const stack = [entry]
  while (stack.length > 0) {
    const chunk = stack.pop()
    if (seen.has(chunk)) continue
    seen.add(chunk)
    for (const importKey of chunk.imports ?? []) {
      const imported = manifest[importKey]
      if (imported) stack.push(imported)
    }
  }
  return seen
}

function findNamed(chunks, name) {
  return [...chunks].find((chunk) => path.basename(chunk.file).startsWith(`${name}-`))
}

async function main() {
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'))
  const entry = findEntry(manifest)
  const reachable = staticGraph(manifest, entry)

  const budgetChunks = [entry, findNamed(reachable, 'react'), findNamed(reachable, 'query')].filter(
    Boolean,
  )

  let totalBytes = 0
  for (const chunk of budgetChunks) {
    totalBytes += await gzipSize(chunk.file)
    for (const cssFile of chunk.css ?? []) totalBytes += await gzipSize(cssFile)
  }

  const leaked = [...reachable].filter((chunk) => /charts|qrcode/i.test(chunk.file))

  const problems = []
  if (totalBytes > BUDGET_BYTES) {
    problems.push(
      `entry + react + query is ${(totalBytes / 1024).toFixed(1)} KB gzip, over the ${(BUDGET_BYTES / 1024).toFixed(0)} KB budget.`,
    )
  }
  if (leaked.length > 0) {
    problems.push(
      `these chunks are statically reachable from the entry and must stay lazy: ${leaked.map((c) => c.file).join(', ')}`,
    )
  }

  if (problems.length > 0) {
    console.error('Bundle budget failed:')
    for (const problem of problems) console.error(`  - ${problem}`)
    process.exitCode = 1
    return
  }

  console.log(`Bundle budget OK: entry + react + query = ${(totalBytes / 1024).toFixed(1)} KB gzip.`)
}

main().catch((error) => {
  console.error(error)
  process.exitCode = 1
})

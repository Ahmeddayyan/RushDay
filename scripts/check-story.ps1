<#
Fails the build if the owner's story sentence pair (docs/spec/00-overview.md section 1) is not verbatim in every
required location, or if any file under src/RushDay.Web/src uses dangerouslySetInnerHTML (RushDay renders no HTML
from data anywhere; a raw-HTML escape hatch appearing later would be a real vulnerability and a real break of that
promise, so this check treats one as a build failure rather than a style note).

Whitespace inside the sentence (including line wraps in JSX text or Markdown) and the JSX entity &apos; for a
straight apostrophe are normalised before comparison, so the same literal can wrap naturally in prose or markup.

Run from anywhere; exits non-zero (and lists every failure, not just the first) when a check fails.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $root

$story = "I built RushDay because my own university's portal fell over every results day and enrolment window. " +
         "I wanted to understand why that happens and to build a portal that doesn't crash under the same load."

function Normalize([string]$text) {
    $normalized = $text -replace "&apos;", "'"
    $normalized = $normalized -replace "\s+", " "
    return $normalized.Trim()
}

$normalizedStory = Normalize $story

$locations = @(
    "README.md",
    "src/RushDay.Api/Endpoints/IndexEndpoints.cs",
    "src/RushDay.Web/src/features/auth/LoginPage.tsx",
    "src/RushDay.Web/src/features/story/StoryPage.tsx",
    "src/RushDay.Web/index.html",
    "docs/adr/0007-identity-and-cookie-sessions.md"
)

$failures = @()

foreach ($path in $locations) {
    $fullPath = Join-Path $root $path
    if (-not (Test-Path $fullPath)) {
        $failures += "MISSING FILE: $path"
        continue
    }

    $content = Normalize (Get-Content -Raw -Path $fullPath)
    if ($content -notlike "*$normalizedStory*") {
        $failures += "STORY NOT FOUND (verbatim, whitespace-insensitive): $path"
    }
}

$webSrc = Join-Path $root "src/RushDay.Web/src"
if (Test-Path $webSrc) {
    $offenders = Get-ChildItem -Path $webSrc -Recurse -File -Include "*.ts", "*.tsx" |
        Select-String -Pattern "dangerouslySetInnerHTML" -SimpleMatch |
        Select-Object -ExpandProperty Path -Unique
    foreach ($offender in $offenders) {
        $relative = $offender.Substring($root.Length + 1)
        $failures += "dangerouslySetInnerHTML FOUND: $relative"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "check-story.ps1: FAILED" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  - $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host "check-story.ps1: OK (story sentence pair verbatim in $($locations.Count) locations; no dangerouslySetInnerHTML under src/RushDay.Web/src)." -ForegroundColor Green
exit 0

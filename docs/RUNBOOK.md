# Estate Runbook

The operating loop for scanning a real estate, distilled from the 2026-10
validation cycles. Every command here is the tested shape; PowerShell 5.1-safe
(assume arrays where you expect values, and wrap method calls in per-item
iteration — see the traps at the bottom).

## The loop (RUN ORDER)

```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8   # FIRST — PS 5.1 without this decodes UTF-8
$OutputEncoding = [System.Text.Encoding]::UTF8             #   tool output as legacy OEM codepage and
                                                           #   mangles →/—/box chars into odd glyphs
$ua = "src/UpgradeAuthority/bin/Release/net10.0/ua.dll"   # build first: dotnet build src/UpgradeAuthority -c Release

# 0) tracemap — pull dev, rebuild, PROVE the dll is fresh (a stale dll re-scans nothing and lies)
cd C:\tracemap
git checkout dev; git pull
dotnet build src/dotnet/TraceMap.sln -c Release
Get-Item src/dotnet/TraceMap.Cli/bin/Release/net10.0/tracemap.dll | Select-Object LastWriteTime

# 1) full estate scan — worktree mode; capture ALL streams (transient failures leave receipts)
dotnet $ua scan-estate --repos-root <repos-root> `
  --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
  --scope <repos-root>\repos.txt `
  --out C:\ua-estate --parallel 8 --worktree *> C:\ua-estate\scan-run-log.txt
# WARNING (live 2026-11-09): WITHOUT --worktree, in-place scans YOUR CHECKOUT AS-IS —
#        local commits ahead of origin, working-tree edits, experiment changes: the evidence
#        reads LOCAL HEAD, not origin (and --allow-stale turns would-be skips into exactly
#        these scans). Run the loop in --worktree mode so evidence always means origin tip.
# notes: a ci-hints.v1.json sidecar in the OUT DIR enables the ci producer lane by itself
#        (loud notes both ways); a non-zero tracemap exit retries 3x with backoff, and the
#        skip line carries tracemap's stderr tail — silence is never the answer shape;
#        the first run needs --self <teamId> ONCE (ownership bootstrap), or an existing ownership.v0.json
# "--archived <file>" excludes archived repos from waves (evidence retained)

# 2) vintage census before ingest — the whole estate must be ONE vintage (count of stale = 0)
$dll = (Get-FileHash C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll -Algorithm SHA256).Hash
Get-ChildItem C:\ua-estate\scans -Directory | ForEach-Object {
  $m = Get-Content "$($_.FullName)\scan-estate.json" -Raw | ConvertFrom-Json
  if ($m.tracemapSha256 -ne $dll) { $_.Name }
} | Measure-Object | ForEach-Object Count

# 3) registry (step 1's FULL-estate scan-estate ALREADY built the fixture — never re-ingest
#    manually after it; a SCOPED one-repo run is different, see "One-repo refresh": a standalone
#    ingest drops state in three ways (review receipt): without --scope it re-discovers EVERY scan
#    child on disk, resurrecting out-of-scope cached scans; without --delta it REMOVES
#    input/delta.json (plan/report/apply break downstream); without --archived the annotation is
#    lost and archived repos appear active again)
dotnet $ua registry C:\ua-estate\fixture --out C:\ua-estate\registry.json
# RE-INGEST only after a sidecar edit, and carry EVERY carrier the original run carried:
#   dotnet $ua ingest --scans-root C:\ua-estate\scans --scope <repos-root>\repos.txt `
#     --producer C:\ua-estate\producer-evidence.v0.json `
#     --ownership C:\ua-estate\ownership.v0.json `
#     --delta C:\ua-estate\delta.json `
#     --archived C:\ua-estate\archived.v0.json --out C:\ua-estate\fixture
(Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).provenanceSummary
(Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).boundary.internalPackages.Count
(Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).boundary.unknownPackages.Count
```

## Part 2 — the campaign (after the registry reads true)

The scan loop proves the estate; the campaign moves it. Both halves read the same
`C:\ua-estate\fixture` — no re-scan between them. Steps 1–3 above keep the evidence
current; steps 4–7 execute one upgrade at a time (V0 plans ONE package delta).

# 4) the delta — the ONE upgrade the campaign plans around. Three sources (README
#    "Where the delta comes from"): hand-write the six lines into C:\ua-estate\delta.json,
#    --delta-package/--delta-old/--delta-new bootstrapped the template on first run (swap in
#    real values), or drift --emit-deltas generates candidates from feed truth.
#    With the delta in place, RE-RUN the ingest so the fixture carries it (seconds —
#    the original full-run carriers, every one):
dotnet $ua ingest --scans-root C:\ua-estate\scans --scope <repos-root>\repos.txt `
  --producer C:\ua-estate\producer-evidence.v0.json `
  --ownership C:\ua-estate\ownership.v0.json `
  --archived C:\ua-estate\archived.v0.json --out C:\ua-estate\fixture
# (omit --delta / --archived only if the estate genuinely has none — an explicit path
#  that does not exist is refused)

# 5) plan + report — who moves, in what order, uncertainty visible. READ the report before
#    anything touches a repo: waves, affected/unknown split, per-repo findings.
dotnet $ua plan C:\ua-estate\fixture --out C:\ua-estate\plan.json
dotnet $ua report C:\ua-estate\fixture --out C:\ua-estate\report.md

# 6) apply — plan edit sites → verified patches. EVERY site is re-checked against the
#    actual checkout (package id + attribute + old version must match, in one place);
#    drifted evidence refuses rather than overwrites. Run it for the checkout you point at:
dotnet $ua apply C:\ua-estate\fixture --repo <repo-checkout> --out C:\ua-estate\patches

# 7) push — branch + gated commit per edit-bearing wave, optional PR. ONE REPO CHECKOUT
#    PER PUSH RUN: a plan whose edits span multiple repos is split by running apply/push
#    per repo (per-repo plan filtering is not yet a command). ua NEVER merges — a human does.
#    --dry-run shows the push plan without touching git:
dotnet $ua push C:\ua-estate\fixture --repo <repo-checkout> --base main --dry-run
dotnet $ua push C:\ua-estate\fixture --repo <repo-checkout> --base main --pr
# waves execute in the plan's order (producer before consumer); report-only waves carry
# plan-level information in the PR body and have nothing to commit in that repo.

## One-repo refresh (the fast path)

Refresh exactly one repo's scan, then rebuild the FULL estate fixture. The other
scans stay on disk, but the scoped `scan-estate` run ingests only THIS run's
scans and atomically replaces `C:\ua-estate\fixture` with a one-repo fixture
(and, with a delta, a one-repo `plan.json`/`report.md`). Running registry on
that fixture reports an estate missing every other repo. The full re-ingest
below is mandatory, not optional; run the vintage census afterwards:

```powershell
Set-Content C:\ua-estate\one-repo.txt "<that-repo>"
dotnet $ua scan-estate --repos-root <repos-root> `
  --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
  --scope C:\ua-estate\one-repo.txt `
  --out C:\ua-estate --parallel 1 --worktree
# REBUILD the full estate fixture: the ORIGINAL estate scope (never one-repo.txt) and EVERY
# carrier per step 3's receipt — omit --delta / --archived only if the estate has none
# (an explicit path that does not exist is refused)
dotnet $ua ingest --scans-root C:\ua-estate\scans --scope <repos-root>\repos.txt `
  --producer C:\ua-estate\producer-evidence.v0.json `
  --ownership C:\ua-estate\ownership.v0.json `
  --delta C:\ua-estate\delta.json `
  --archived C:\ua-estate\archived.v0.json --out C:\ua-estate\fixture
# with a delta: replace the one-repo plan/report the scoped run wrote
dotnet $ua plan C:\ua-estate\fixture --out C:\ua-estate\plan.json
dotnet $ua report C:\ua-estate\fixture --out C:\ua-estate\report.md
dotnet $ua registry C:\ua-estate\fixture --out C:\ua-estate\registry.json
```

## The ci-hints sidecar (census-derived — its only sanctioned birth)

`ci-hints.v1.json` names the shared CI workflows that mean "this repo
publishes". NEVER hand-type it — derive it from the estate's own yml files so
it cannot drift from reality (a hint that matches nothing is a silent no-op;
two spellings in the wild are the DEFAULT failure shape):

```powershell
$uses = Get-ChildItem <repos-root> -Directory | ForEach-Object {
  $f = Join-Path $_.FullName '.github\workflows\release.yml'
  if (Test-Path $f) { (Select-String -Path $f -Pattern 'uses:\s*(\S+)' -AllMatches).Matches | ForEach-Object { $_.Groups[1].Value.Trim() } }
}
$shared = @($uses | Where-Object { $_ -like '*.github/workflows/*' } | Sort-Object -Unique)
"$($shared.Count) shared workflow spellings:"; $shared
$entries = @(); foreach ($u in $shared) { $entries += @{ uses = [string]$u } }
@{ schemaVersion = 'ci-hints.v1'; packWorkflows = $entries } | ConvertTo-Json -Depth 5 | Set-Content C:\ua-estate\ci-hints.v1.json -Encoding ascii
# ASSERT the shape before trusting it (single-element collapse is a real trap):
$j = Get-Content C:\ua-estate\ci-hints.v1.json -Raw | ConvertFrom-Json
"type: $($j.packWorkflows.GetType().Name)  count: $($j.packWorkflows.Count)"   # must be Object[] + N
```

The file enables the ci producer lane automatically when present in the scan
out dir; `--index-ci-producers` forces it on with no sidecar.

**Optional per-entry field — `configuration` (tracemap 0.4.10+):** if a repo's
conditioned identity group (e.g. an Alt.* variant under a `ReleaseBench` condition) is
hiding an otherwise-resolvable head identity, add `"configuration": "Release"` to that
workflow's entry — the operator assertion of the configuration the shared workflow packs
at (the shared workflow itself lives in another repo, so the scanner cannot read it).
tracemap 0.4.10 then releases provably-false branches and resolves the head identity
(the direct `-c` pack argument works with no flag at all). Leave the field OFF where
you don't know the configuration — the honest refusal stands.

## Failure reading

- `skip <repo> (tracemap exited N after M attempt(s): <stderr tail>)` — the
  receipt. Transient classes (contention, AV races, api blips) die on retry;
  a persistent skip names its cause in the tail.
- `scan-run-log.txt` — the always-captured full stream; the skip context grep:
  `Select-String C:\ua-estate\scan-run-log.txt -Pattern 'skip ' -Context 2,0`
- A MISSING scan folder for a repo = the repo never completed a scan this
  vintage (check the manifest status + the log).
- An absent producer in the registry with its consumers in `unknownPackages`:
  find the producing repo, read its gap lines in
  `scans\<repo>\facts.ndjson` (gap facts name the PROJECT, not the package).

## PowerShell 5.1 traps (all paid for live)

- Encoding: PS 5.1 redirects (`>`, `*>`) and the console decode UTF-8 tool output with
  the legacy OEM codepage — arrows, dashes, and box-drawing chars arrive as mojibake
  (the "odd chars" in captured logs and census output). Fix at session start:
  `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8`.
  Better: prefer the tool's `--out` (canonical bytes, encoding-independent) over
  redirection wherever one exists.
- `(no-match).Line` is `$null` — a collected array of results contains nulls
  that explode on method calls. Guard: `if ($m) { $m.Line }`.
- An array's `.Length` is its ELEMENT COUNT, not characters —
  `@('a','b').Substring(0, 2)` truncates to two chars. Iterate per line.
- `-ceq` on arrays FILTERS (returns matches, prints blank), it does not
  compare. Wrap `@(...)` and check `.Count`.
- `ConvertTo-Json` collapses a single-element pipeline into an object, not an
  array. Build entries explicitly (`$entries = @(); foreach ... +=`).
- `Set-Content` writes UTF-16/BOM by default — use `-Encoding ascii` (or
  utf8) for machine-read files; strict readers reject the BOM.

# debug.md — estate verification commands

**LAW: every command the agent hands over lives here, not in chat. Is it in debug.md? is the correct challenge before running anything.**

## COMPAT-CACHE COLD START — the upgrade-plan layer's warm-up (2026-11-13)

The order on a fresh machine: join (estate knowledge) → probe (TFM knowledge) → view (the
worklist). The cache dir is PERMANENT warm state (like scans/) — same `--cache` path on
every verb that takes one. Step 0 assumes the estate scan already ran (C:\ua-estate\fixture
exists). `$ua` = the dotnet invocation you use elsewhere in this file.

```powershell
# 0) the join — fills usedVersions/usedByRepos/InUse for every consumed package.
#    Everything reads compat-blocked after this: that is the worklist, not an error.
dotnet $ua discover-max --join-fixture C:\ua-estate\fixture --out C:\ua-estate\compat-cache

# 1) flatten the NuGet restore cache into a folder of nupkgs (the build server's own
#    restore puts every pinned version here; -Force overwrites case-duplicate names).
#    If nothing has been restored on this machine yet, run your normal restore/build once.
New-Item -ItemType Directory -Force C:\ua-estate\nupkg-feed | Out-Null
Get-ChildItem "$env:USERPROFILE\.nuget\packages" -Recurse -Filter *.nupkg | ForEach-Object { Copy-Item $_.FullName ("C:\ua-estate\nupkg-feed\" + $_.Name) -Force }

# 2) the probe — real targetFrameworks records from the nupkg lib/** folders.
#    Never rewrites probed records; never destroys the join's back-references.
dotnet $ua discover-max --from-folder C:\ua-estate\nupkg-feed --out C:\ua-estate\compat-cache

# 3) the estate view — per TFM group x package: pins, potentialMax, allowedMax, status.
dotnet $ua upgrade-plan C:\ua-estate\fixture --cache C:\ua-estate\compat-cache --out C:\ua-estate\upgrade-plan.json

# IF EVERYTHING STILL READS (tfm-unknown) after the fix: does the lockfile channel carry anything?
$lf = Get-Content C:\ua-estate\fixture\input\lockfile-rows.v2.json -Raw | ConvertFrom-Json
"$($lf.repos.Count) repos in the lockfile channel; with tfm evidence: " + (($lf.repos | Where-Object { $_.rows | Where-Object { $_.tfm } } | Measure-Object).Count)
# 0 repos or 0 with tfm = the estate ships no lockfiles (or deps.json was not indexed) — the
# frameworks are then unevidenced and (tfm-unknown) is the honest state until lockfiles exist.

# READ BACK TO THE AGENT: the status counts, one line —
(Get-Content C:\ua-estate\upgrade-plan.json -Raw | ConvertFrom-Json).groups.PSObject.Properties | ForEach-Object { $_.Value } | ForEach-Object { $_.status } | Group-Object | Sort-Object Count -Descending | ForEach-Object { "$($_.Count)x $($_.Name)" }
```

Known limit while the feed input is slice 3: potentialMax only knows versions that were in
the nupkg folder (the restore cache holds what was pinned, not what is newest) — some
`current` rows are actually behind. `would-update` rows are real and safe either way: a
ceiling computed from actual TFM assets.

## GAP-KIND CENSUS — the extractor-change diff (2026-11-10)

What it answers: which gap kinds exist across the whole estate's scans, one line per
kind with counts. Run it after ANY extractor change (dll hash moved) and diff against
the previous run's output — that diff is the extractor's honest-change receipt.

```powershell
Get-ChildItem C:\ua-estate\scans -Directory | ForEach-Object {
  $f = Join-Path $_.FullName 'facts.ndjson'
  if (Test-Path $f) { Select-String $f -Pattern '"gapKind":\s*"([^"]+)"' -AllMatches | ForEach-Object { $_.Matches } }
} | ForEach-Object { $_.Groups[1].Value } | Group-Object | Sort-Object Count -Descending | ForEach-Object { "$($_.Count)x $($_.Name)" }
```

Expected after 0.4.7/0.4.8:
- NEW gaps = the `ci-hint-id-unevidenced` family — the hint sweep now derives identities
  for project-lane-claimed repos too, and honestly refuses the unprovable ones. Receipts,
  not failures.
- GONE gaps = duplicate-identity and spelling-variant claims collapsed to their winner
  (0.4.7's one-project-one-identity).
- An UNFAMILIAR kind = a finding — bring the kind name and one repo.

## CI-LANE GAP DETAILS — repo + kind + reason, the validity read (2026-11-10)

What it answers: for the two actionable ci-lane families, WHICH repo, WHICH refusal
shape, and the workflow file — so the operator can judge each gap valid or a hole.
Read back per line: the repo name + the message's leading clause (the refusal family).
Employer paths in the output stay on your screen — read me the family, not the paths.

```powershell
Get-ChildItem C:\ua-estate\scans -Directory | ForEach-Object {
  $repo = $_.Name
  $f = Join-Path $_.FullName 'facts.ndjson'
  if (-not (Test-Path $f)) { return }
  $m = Select-String $f -Pattern '"gapKind":\s*"(ci-hint-id-unevidenced|ci-producer-id-unevidenced)"'
  if ($m) {
    foreach ($line in @($m)) {
      try {
        $j = $line.Line | ConvertFrom-Json
        "$repo | $($j.properties.gapKind) | $($j.properties.workflowPath) | $($j.properties.message)"
      } catch { "$repo | unparseable gap line" }
    }
  }
}
```

Verdicts so far:
- ci-producer-id-unevidenced (2x): targetless pack calls — the hint-flags spec
  (`pack: "solution"`) is the designed fix; not a bug.
- ci-hint-id-unevidenced (5x): per-repo refusal families unknown until this script runs.
  Valid refusals = permanent honest gaps; a shared unexpected shape = a derivation hole.

## The estate scan loop

docs/RUNBOOK.md (Part 1: scan → census → registry; Part 2: delta → plan → report →
apply → push). This file carries only ad-hoc diagnostics; the runbook carries the loop.

## PowerShell 5.1 traps (standing — all paid for live)

- `(no-match).Line` is `$null` — a collected array holds nulls that explode on method
  calls. Guard: `if ($m) { $m.Line }`.
- An array's `.Length` is its ELEMENT COUNT, not characters — `@('a','b').Substring(0,2)`
  truncates to two chars. Iterate per line.
- `-ceq` on arrays FILTERS (returns matches), it does not compare. Wrap `@(...)` and
  check `.Count`.
- `ConvertTo-Json` collapses a single-element pipeline into an object. Build entries
  explicitly (`$entries = @(); foreach ... +=`).
- `Set-Content` writes UTF-16/BOM by default — `-Encoding ascii` for machine-read files.

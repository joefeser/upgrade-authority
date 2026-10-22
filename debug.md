# debug.md — trial-branch living debug log

Purpose: append-only log of the live validation loop on this branch. Built up
as we debug; tossed when the branch is purged. This is a PUBLIC branch: no
real repo names, URLs, fact ids, or estate-identifying content — generic
labels only ("the 3 shared repos", "the estate machine").

Format per entry: date — symptom / evidence / hypothesis / next diagnostic.

## RUN ORDER — current (supersedes older command blocks below)

```powershell
# 1) update THIS tool (pull without rebuild = stale code, the 10-07 trap)
cd <your upgrade-authority clone>    # on trial/scope-prework
git pull
dotnet build src/UpgradeAuthority -c Release

# 2) refresh tracemap + PROVE the dll is fresh
cd C:\tracemap
git checkout dev          # the producer tiers MERGED via PR #900 — off the feature branch
git pull
dotnet build src/dotnet/TraceMap.sln -c Release
Get-Item C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll | Select-Object LastWriteTime

# 3) full estate rescan — WORKTREE mode (behind-tip/dirty checkouts are irrelevant there).
#    If the out dir has NO ownership.v0.json yet, bootstrap needs --self <teamId> ONCE
#    (teams are never invented); if the sidecar already exists --self is unnecessary.
dotnet $ua scan-estate --repos-root <repos-root> `
  --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
  --scope <repos-root>\repos.txt `
  --out C:\ua-estate --parallel 8 --worktree --self <teamId>
#    (a rerun after this error costs nothing: all completed scans reuse from cache instantly)
#    NOTE (shipped 2026-10-21): the ownership precondition is now checked UP FRONT — a
#    missing ownership + no --self refuses BEFORE any scanning instead of after 124 scans.
#    An EXISTING ownership.v0.json (pre---scope is fine) needs no --self: scan-estate
#    validates it and UPDATES it to the scanned set (new-in-scope repos join the file's
#    self team; entries for trimmed repos linger inertly).

# 4) VINTAGE CENSUS before ingest — every scan must carry the CURRENT dll hash.
#    (A one-repo spot check only proves the dll; a repo that happened to be in the
#    last partial run passes while the rest of the estate stays stale.)
$dll = (Get-FileHash C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll -Algorithm SHA256).Hash
Get-ChildItem C:\ua-estate\scans -Directory | ForEach-Object {
  $m = Get-Content "$($_.FullName)\scan-estate.json" -Raw | ConvertFrom-Json
  if ($m.tracemapSha256 -ne $dll) { $_.Name }
} | Measure-Object | ForEach-Object Count

# 5) ingest + registry (paste the totals + unknown count back)
dotnet $ua ingest --scans-root C:\ua-estate\scans `
  --producer C:\ua-estate\producer-evidence.v0.json `
  --ownership C:\ua-estate\ownership.v0.json --out C:\ua-estate\fixture
dotnet $ua registry C:\ua-estate\fixture
```

Step 4 prints the count of STALE-evidence repos: 0 = the whole estate is
one vintage, proceed to step 5. Anything > 0 = rerun step 3 (the worktree
pass rescans them) — do not ingest a mixed-vintage estate.
For a stale-repo NAME list, drop the `| Measure-Object | ForEach-Object Count`.
Expected in step 3: narrated fetch (no hangs — git kill-timer), worktree
scans, a skip-reason tally + INCOMPLETE warning if coverage is poor.

## 2026-10-21 — REMOTE FALLBACK shipped: origin is the default, no longer a hard wall

- Live case: at the operator's workplace SSH is blocked; two repos carry an
  extra https remote just to remain workable, and the hard origin-only
  assumption skipped them.
- Shipped on this branch (selftest 165):
  1. No origin + exactly ONE remote → that remote is used as origin, with
     a per-repo warning ("no origin remote — treating the only remote
     '<name>' as origin").
  2. Origin present but its transport dead → fetch falls back to an
     alternate remote ONLY when its URL folds to the same repository
     identity (git@host:org/repo.git ≡ ssh://git@host/org/repo.git ≡
     https://host/org/repo.git — case-variant paths stay distinct), with a
     warning; the trunk follows the remote that actually fed the run.
  3. No origin + MULTIPLE remotes → still refused (never guess which
     remote is authoritative), reason now says to rename the authoritative
     one to origin. Zero remotes → its own typed reason.
- No flag needed; the fallback is automatic and warned in the fetch lines.
  After re-pulling this branch + rebuilding ua, the two repos scan in the
  worktree pass like every other repo.

## 2026-10-21 — "filename too long" RESOLVED (the two-repo saga closes)

- The two persistently failing worktree repos were Windows MAX_PATH victims:
  one repo contains a ~140-character auto-generated TEST FILENAME
  (Method_Scenario_Expectation x3 concatenated), and the satellite prefix
  pushed its checkout path past 260. The second repo's "Could not reset
  index file" was the same failure one step later.
- Fixes: (operator) `git config --global core.longpaths true` — confirmed
  working live; (branch, 5f77838) hex-only satellite leaves (~47 chars of
  path budget back, spec §3.2 alignment; sweep recognizes both shapes).
- Ladder for the record: "Preparing worktree" (progress preamble, useless)
  -> "Could not reset index file" (real error, wrong depth) -> "unable to
  create file <monster.cs>" -> root cause. Each layer shipped a fix.
- Post-fix state: both repos scan under worktree mode; ownership update
  reports honestly (123 scanned / 131 total / absent assignments named).

## 2026-10-21 — REGISTRY: boundary.unknownPackages = 223 (producer-extraction gap)

- Registry live: producers=2 (both project-declared), internal=2,
  external=[] (delta-less bootstrap templated it empty), anomalies clean.
- Owner fact: ALL internal packages are produced IN this estate (library
  repos are nuget-only producers; no cross-team dependencies). So internal
  packages in unknownPackages = PRODUCER FACTS NOT EXTRACTED — only 2
  PackageProduced facts arrived from 125 repos.
- Root cause (tracemap source): ReadProducedPackages reads ONLY csproj
  PropertyGroups — no props files, no import chain. SDK-style libs that
  don't restate PackageId/AssemblyName in the csproj (declared centrally,
  same culture as the CPM flag) are skipped. The 2 found declare in-file.
- Diagnostic (run on the estate machine; pick a repo that produces one of
  the internal library packages):

  ```powershell
  $r = Get-ChildItem C:\ua-estate\scans -Directory -Filter '*<lib-fragment>*' | Select-Object -First 1
  Select-String "$($r.FullName)\facts.ndjson" -Pattern 'PackageProduced' | Measure-Object | ForEach-Object Count
  Get-ChildItem "<that repo's checkout>\src" -Filter *.csproj -Recurse -Depth 3 | Select-Object -First 1 | Get-Content
  Get-Content "<that repo's checkout>\Directory.Build.props" -ErrorAction SilentlyContinue
  ```

  Expected: count 0; a csproj without PackageId/AssemblyName; the central
  props carrying them. That output is the evidence for the tracemap fix.
- Moves: (a) no-code stopgap — operator-declare producers in
  producer-evidence.v0.json (operatorDeclared is first-class) and declare
  the public-feed externals; (b) proper fix in tracemap — props-aware
  produced extraction (merge nearest Directory.Build.props).

## 2026-10-21 — PRODUCER SHAPE CONFIRMED: SDK defaults + pack metadata, declared nowhere

- Diagnostic (live): the library repo's csproj carries Authors/Company/
  Product/Description/PackageTags/Copyright — pack metadata ONLY — and NO
  PackageId, NO AssemblyName, NO IsPackable; Directory.Build.props does
  not exist in that repo. Package identity rides SDK defaults: packable
  by default, id defaults to AssemblyName, which defaults to the csproj
  FILE NAME.
- The extractor's "never invent from filenames" rule blocks exactly the
  MSBuild default contract. Fix tiers (tracemap):
  1. explicit PackageId (today's behavior),
  2. AssemblyName with $(MSBuildProjectName) interpolation evaluated
     (file-derived, deterministic),
  3. SDK-style + pack-metadata-present and nothing else -> producer with
     provenance "derived-default" — a DISTINCT label, never presented as
     declared,
  4. refuse as today (legacy non-SDK / zero signals),
  5. ci-defined cross-check: workflows containing the pack step are the
     authoritative confirmation.
- Naming reality (operator spec): repo != sln != folder != csproj !=
  assembly != namespace; app projects interpolate AssemblyName from
  $(MSBuildProjectName); in-repo ProjectReference consumption is NOT
  package consumption (call-graph territory, correctly out of ua's
  boundary). Test estate (ua-xyz) must encode one repo per tier row
  plus the edge zoo (archived, ssh twin, dirty, CPM-in-props).

## 2026-10-21 — POST-FIVE-TIER REGISTRY: verification diagnostics (producers 2 -> 143)

- Wild result: `projectDeclared: 143` (was 2); internalPackages populated.
  Three residual classes before the branch merges:
  (a) odd-prefixed package families — real convention vs tier bug?
  (b) the something-stupid checklist (test projects / sample libraries
  appearing as producers — legit only with a literal IsPackable=true);
  (c) zero-signal sinks staying unknown = tier-3 honesty, NOT a failure
  (their fix path is the ci-defined lane or operator declaration).
- All diagnostics (run on the estate machine; substitute the fragments):

  ```powershell
  # 1) odd-prefixed family — OCR-FRIENDLY FORM (short labeled lines, not the raw JSON line;
  #    the work machine cannot copy text out, so photograph THESE five lines at a larger font)
  $r = Get-ChildItem C:\ua-estate\scans -Directory -Filter '*<repo-fragment>*' | Select-Object -First 1
  $m = Select-String "$($r.FullName)\facts.ndjson" -Pattern '<odd-prefix>\.' | Select-Object -First 1
  $m.Line | ConvertFrom-Json | Select-Object `
    @{n='id';e={$_.targetSymbol}}, `
    @{n='provenance';e={$_.properties.packageIdProvenance}}, `
    @{n='source';e={$_.properties.packageIdSource}}, `
    @{n='signals';e={$_.properties.packSignals}}, `
    @{n='file';e={$_.evidence.filePath}} | Format-List
  # derived-default + the csproj literally named <odd-prefix>.* = truth about a naming convention;
  # interpolated + EMPTY signals = AssemblyName-only path = false-producer candidate (naming, not packing)

  # 2) checklist nominees: legit ONLY with a literal IsPackable=true
  Select-String -Path (Get-ChildItem "<repos-root>\<repo>" -Recurse -Filter *.csproj).FullName `
    -Pattern 'AssemblyName|IsPackable|PackageId'

  # 3) full internal list to eyeball (the something-stupid pass)
  dotnet $ua registry C:\ua-estate\fixture --out C:\ua-estate\registry.json
  (Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).boundary.internalPackages
  ```

- Merge rule: PR the branch once every internalPackages name is either
  known-legit or explained; odd-but-true beats tidy-but-wrong.

## 2026-10-22 — CI-HINTS VERIFICATION PASS (the ~100-package completion; supersedes older blocks)

- Producer story: estate declares nothing (one IsPackable in-repo; ids from
  filenames; version from GitVersion); the shared CI workflow call is the
  publication signal. Tracemap branch feature/ci-derived-package-id
  (ci-workflow/0.2.0, includes BOTH the derived-id local pack tier and the
  ci-hints lane) + ua auto-loading this sidecar.
- Drop the hints file at C:\ua-estate\ci-hints.v1.json (adjust the uses
  path to the REAL org/repo from release.yml; @ref omitted — refs ignored):

  ```json
  {
    "schemaVersion": "ci-hints.v1",
    "packWorkflows": [
      { "uses": "<org>/<shared-workflows-repo>/.github/workflows/dotnet-release-library.yml", "note": "internal release tooling - a call means this repo publishes" }
    ]
  }
  ```

- And the archived list at C:\ua-estate\archived.txt (plain names, one per
  line) for repos that cannot take PRs.
- Then the verification pass:

  ```powershell
  cd C:\tracemap
  git fetch
  git checkout feature/ci-derived-package-id
  dotnet build src/dotnet/TraceMap.sln -c Release
  Get-Item C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll | Select-Object LastWriteTime

  dotnet $ua scan-estate --repos-root <repos-root> `
    --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
    --scope <repos-root>\repos.txt `
    --out C:\ua-estate --parallel 8 --worktree --archived C:\ua-estate\archived.txt --index-ci-producers

  dotnet $ua registry C:\ua-estate\fixture --out C:\ua-estate\registry.json
  (Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).provenanceSummary
  ```

- Watch for: `note: ci-hints loaded (...)` before the fetch phase (proves the
  sidecar engaged); then provenanceSummary projectDeclared/ciDefined — the
  ciDefined count + internalPackages near ~100 closes the producer saga.
  Dll+flags changed = one full rescan (the usual honesty tax).

## 2026-10-22 — FIND THE LOST PRODUCERS (per-repo sweep; pick the double-scan victim)

- You don't need to guess which producers vanished — count them per repo:

  ```powershell
  Get-ChildItem C:\ua-estate\scans -Directory | ForEach-Object {
    [pscustomobject]@{ repo = $_.Name; producers = (Select-String "$($_.FullName)\facts.ndjson" -Pattern 'PackageProduced' -AllMatches).Count }
  } | Sort-Object producers, repo | Format-Table -AutoSize
  ```

- Read top-down: every 0 you KNOW is a shipping library = a lost producer
  (apps are 0 legitimately). Small-but-shrunk counts are drift too. Pick one
  lost-library zero for the DETERMINISM CHECK double-scan below.
- Optional second signal: zeros correlating with FailedOrPartial build
  status / Reduced analysis level (scan-manifest.json per repo) points at
  coverage-gating rather than pure nondeterminism.

## 2026-10-22 — DETERMINISM CHECK (the projectDeclared drift 57->55->44 across runs with no project-lane change — Joe's oldest tracemap fear, now with receipts; THE gate before any merge)

- Run C numbers: project 44, ci 56 (chain recovered +30 of the 88), internal 111,
  customimports 0. The ci movement is explained; projectDeclared silently
  losing 13 producers across two runs is NOT — suspects: load-dependent
  extraction (the #857 regex-timeout family, Reduced-analysis level) or
  coverage-gating dropping producer facts on partial scans.
- The decisive test — same repo, same dll, twice:

  ```powershell
  $t = "C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll"
  dotnet $t scan --repo "<repos-root>\<one-repo-that-lost-producers>" --out C:\det1
  dotnet $t scan --repo "<repos-root>\<same-repo>" --out C:\det2
  (Select-String C:\det1\facts.ndjson -Pattern 'PackageProduced').Count
  (Select-String C:\det2\facts.ndjson -Pattern 'PackageProduced').Count
  ```

- EQUAL counts -> deterministic; the 44-vs-57 is a real code interaction —
  freeze #902 and diff the project-lane path (no merge on drifted numbers).
- DIFFERENT counts -> nondeterminism — NOTHING merges until tracemap earns
  "same input, same facts"; this becomes the only priority.

## 2026-10-22 — ROUND-3 VERIFY PASS (0.3.1: custom-import guard — likely last recount)

- Head bd398f268: imports/linked-metadata refuse to derive (typed gaps),
  gaps anchored to the uses: call site, strict-UTF-8 hints. ci-workflow 0.3.1.
- Pull + rebuild + the SAME command (the 0.3.1 bump forces the honest
  rescan — count expected to HOLD at 142; any drop is an import-carrying
  project naming itself in the new custom-imports gaps):

  ```powershell
  cd C:\tracemap
  git pull
  dotnet build src/dotnet/TraceMap.sln -c Release
  dotnet $ua scan-estate --repos-root <repos-root> `
    --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
    --scope <repos-root>\repos.txt `
    --out C:\ua-estate --parallel 8 --worktree --archived C:\ua-estate\archived.txt --index-ci-producers

  dotnet $ua registry C:\ua-estate\fixture --out C:\ua-estate\registry.json
  (Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).provenanceSummary
  (Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).boundary.internalPackages.Count
  # new gaps worth one glance:
  Get-ChildItem C:\ua-estate\scans -Directory | ForEach-Object { Select-String "$($_.FullName)\facts.ndjson" -Pattern 'custom-imports' } | Measure-Object | ForEach-Object Count
  ```

- Verdict rule unchanged: count holds (±0) -> merge; small drop with every
  dropout named in a custom-imports gap -> still merge (each explained);
  anything else -> paste the gaps.

## 2026-10-22 — PRODUCER EYEBALL PASS (the finish gate; post-guard numbers)

- Final numbers: projectDeclared=57, ciDefined=114, internalPackages=142
  (26 test ghosts removed by the guard; 29 packages dual-claimed by
  metadata+workflow — counted once in internal, visible with both
  provenances in producers).
- The INVERSE views — judge "should this be here?" per row:

  ```powershell
  # per-producer with repo + provenance (spot test apps by repo name)
  $reg = Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json
  $reg.producers | Sort-Object packageId | Format-Table packageId, @{n='repo';e={Split-Path $_.repo -Leaf}}, provenance -AutoSize

  # full evidence join — every producer with WHERE the claim came from
  Get-ChildItem C:\ua-estate\scans -Directory | ForEach-Object {
    $repo = $_.Name
    Select-String "$($_.FullName)\facts.ndjson" -Pattern 'PackageProduced' | ForEach-Object {
      $f = $_.Line | ConvertFrom-Json
      [pscustomobject]@{ pkg=$f.targetSymbol; repo=$repo; prov=$f.properties.packageIdProvenance; signals=$f.properties.packSignals; wf=$f.properties.workflowPath }
    }
  } | Sort-Object repo, pkg | Format-Table -AutoSize
  ```

- Reading test apps: derived-default + ci-hint-workflow rows in repos you
  know are test apps. Three-way call per row: genuinely ships (test utility
  packages are real) / repo calls the release workflow but shouldn't
  (estate-hygiene ticket at work) / a shape the guard should learn (report
  the row). Single-digit stragglers + everything else recognized = PASS.

## 2026-10-21 — THE CLEAN PASS (saga complete)

- Final run: `2 scanned, 123 reused, 0 SKIPPED, 14 out-of-scope` — both
  MAX_PATH repos scanned under worktree mode with longpaths enabled;
  chain completed delta-less to a clean prompt.
- `ingested: 4759 consumer facts, 0 lockfile rows, 125 repos`; ownership
  updated (125 scanned / 131 total, absent assignments named).
- The original residual wall is GONE: the 3 shared repos that started
  this saga no longer warn — their pins correlated once their scans were
  fresh-vintage. Full-estate verification of the CPM fix.
- Remaining constraint residuals: exactly 3 facts in 2 repos (2x one
  OnPrem repo, 1x a PTT repo referencing AWSSDK.DynamoDBv2 — notable
  because that pin previously reported UNUSED in the stale-scan era;
  worth one look next session, possibly a pin/reference shape nuance).
- Owed next: `ua registry` — the unknown count vs the hand-verified
  baseline.

## 2026-10-19 — swap-retry warnings during a large real ingest (RESOLVED, as designed)

- Symptom: three IOException retry warnings during the fixture swap on a
  real estate ingest.
- Evidence (local photo OCR of the console): all three retries were on the
  ASIDE move (old fixture → `.old-` backup); the run then completed normally
  with the normal "ingested:" summary and a clean prompt. No rollback, no
  loss. Likely cause: something briefly holding the old fixture (editor or
  scanner).
- Outcome: absorbed by the bounded-retry armor exactly as designed.
  Hardening shipped in d68f6b7 anyway: exponential budget (500ms..4s), the
  exception MESSAGE now printed in retry warnings (type alone cannot
  distinguish "in use by another process" from anything else), and operator
  guidance on exhaustion.

## 2026-10-20 — CPM warning flood collapse verified (RESOLVED by 41ec879)

- Symptom: pre-fix build flooded "constraint not evidenced (CPM/central
  version or redacted)" across most repos.
- Evidence: post-fix ingest shows the flood collapsed; residual warnings
  confined to 3 shared-library repos with the new wording "(no version on
  the reference and no central pin for it)" and the repo named.
- Context: pins correlate only within the same repo (each
  Directory.Packages.props governs its own tree — NuGet semantics).

## 2026-10-21 — pins EXIST but warnings claim "no central pin" (ROOT CAUSE FOUND — stale tracemap dll; fix staged below)

- Symptom: residual warnings say "no central pin for it" for packages
  (Polly, Confluent.Kafka, StyleCop.Analyzers, Microsoft.Extensions.*,
  internal shared packages) in the 3 shared repos.
- Evidence (local photo OCR of one repo's `Directory.Packages.props`):
  - The file EXISTS at the repo root; CPM is enabled
    (`ManagePackageVersionsCentrally` declared inside the props file).
  - The warned packages ARE pinned there (Polly 8.6.6, Confluent.Kafka
    2.14.0, StyleCop.Analyzers 1.2.0-beta.556, MsExt Configuration/DI/
    Hosting 10.0.5, plus internal shared packages).
- Diagnostic result (operator ran the Select-String below): the repo's
  scan facts contain MANY "Polly" rows and ZERO
  `CentralPackageVersionDeclared` rows.
- Root cause: tracemap's CPM pin extraction landed 2026-10-03
  (commit 3d9c87498, "index central package management pins and
  VersionOverride"). Scans made by an older dll emit no pin facts for ANY
  repo. The estate's scans folder is mixed-vintage: repos scanned or
  refreshed after that date carry pins (the unused-pin notes and the
  flood collapse prove it); the 3 shared repos' scan entries predate the
  feature. The current extractor parses every MSBuild props file in the
  inventory unconditionally (no CPM-flag gate), so a fresh scan emits
  these pins. Not a ua bug; not a repo-shape gap.
- Fix (operator, estate machine — substitute the 3 repo folder names
  exactly as they appear in the warnings, and your repos root):

  ```powershell
  # 0) refresh the evidence provider
  cd C:\tracemap
  git pull
  dotnet build src/dotnet/TraceMap.sln -c Release

  # 1) rescan ONLY the 3 affected repos into the existing estate out dir
  #    (plain-list scope file; one repo folder name per line)
  @'
  <repo-folder-1>
  <repo-folder-2>
  <repo-folder-3>
  '@ | Set-Content C:\ua-estate\rescan-scope.txt -Encoding ASCII

  dotnet $ua scan-estate --repos-root <repos-root> `
    --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
    --out C:\ua-estate --scope C:\ua-estate\rescan-scope.txt

  # 2) confirm pin facts now exist
  $scan = Get-ChildItem C:\ua-estate\scans -Directory -Filter '*Kafka*' | Select-Object -First 1
  Select-String "$($scan.FullName)\facts.ndjson" -Pattern 'CentralPackageVersionDeclared' | Measure-Object | ForEach-Object Count

  # 3) re-run the ingest exactly as before (up-arrow history is fine),
  #    then the registry measurement below
  ```

  Expected: step 2 reports > 0 and the 3-repo warning wall collapses on
  re-ingest. If step 2 still reports 0 after a fresh scan, the extractor
  investigation reopens (inventory classification of the props file) —
  record it here and stop.

- Impact: the fix should collapse the 3-repo residual warning wall and
  flip those repos' classification; until then, registry unknown counts
  carry a known discount for exactly these 3 repos.

## 2026-10-21 — full-estate rescan (the durable fix for mixed-vintage scans)

- Operator timeline: the scans were taken 10-07 from the then-current
  tracemap head, and source WAS pulled. The CPM feature reached tracemap
  main on 2026-10-03 (33fe647cf, #804 promotion), so 10-07 SOURCE had it
  — the dll that actually produced the 10-07 scans was almost certainly
  a stale build artifact (pull without rebuild). Combined with
  movers-only cache refresh on later runs, the scans folder now mixes
  vintages: some repos' scans carry pin facts, quiet repos (the 3 shared
  ones) kept their pre-CPM entries.
- SHOULD YOU RESCAN EVERYTHING? YES — and in WORKTREE MODE (see the
  corrected command). What happened to the first full pass (37 of 126
  scanned, the rest skipped): in-place mode requires every checkout's
  HEAD to equal the freshly-fetched trunk tip AND a clean working tree.
  An actively-developed estate fails both at scale — today's fetch moved
  the tips (every not-yet-rebased checkout reads "behind tip" → skip),
  and earlier in-place scans left obj/ output inside checkouts (any repo
  not ignoring bin/obj reads "working tree not clean" → skip forever).
  `--worktree` materializes an isolated worktree per repo AT the fetched
  tip, so behind-ness and checkout dirt are both irrelevant. That is the
  mode an estate rescan should always use.
- The run now also self-reports coverage honestly (shipped with this
  branch): skip reasons are tallied ("skip reasons: 85x working tree not
  clean; 4x fetch failed") and a poor-coverage run prints
  "warning: only N/M repos have fresh evidence — the fixture is
  INCOMPLETE" instead of reading like success.
- Cost expectations: this is the "real run" price — the fetch phase
  (network, per-repo) plus a tracemap scan for every stale entry. It
  narrates progress per repo; Ctrl+C is safe mid-run and a re-run
  continues from the cache. `--parallel 8` speeds the fetch phase.
  Worktrees land under `C:\ua-estate\.worktrees\ua\` (override with
  --worktree-root). Note: switching to worktree mode changes the cache
  key (scanMode), so the pass rescans the estate — which is the point.
- Command (estate machine; substitute your repos root):

  ```powershell
  cd C:\tracemap
  git pull
  dotnet build src/dotnet/TraceMap.sln -c Release
  # confirm the artifact is FRESH — pull without rebuild is the exact trap above
  Get-Item C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll | Select-Object LastWriteTime

  dotnet $ua scan-estate --repos-root <repos-root> `
    --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
    --scope <repos-root>\repos.txt `
    --out C:\ua-estate --parallel 8 --worktree
  ```

  `--scope` matters: without it the run scans EVERYTHING under the repos
  root (all repos, including any deliberately trimmed ones). The plain
  list (one repo folder name per line, `#` comments allowed) is
  include-mode — exactly the curated set. Adjust the path if your list
  lives elsewhere.

  Then re-run the ingest exactly as before (up-arrow history), then the
  registry measurement below. The 3-repo scoped rescan higher up remains
  a valid shortcut if a number is needed before the full pass fits the
  calendar.

## 2026-10-21 — long silent stretch after the fetch summary (observed live; alive-vs-hung check)

- Observed: after the "estate: fetch phase done" line, 10+ minutes of
  blinking cursor, no output, no folders in the scans dir. EXPECTED on
  big estates: the next phase is freshness-per-repo (a local
  `git status`/rev-parse on every checkout — no network, but under
  real-time AV that is seconds per repo, so ~130 repos is a legitimate
  6–15 minute silent stretch on EVERY build). Scan folders appear only
  as each repo's scan completes and swaps in — an empty scans dir until
  the first "scanning <repo> (1/N)" line is normal.
- Alive-vs-hung check (run twice, 30s apart — CPU seconds rising =
  working; frozen = hung):

  ```powershell
  Get-Process dotnet,git -ErrorAction SilentlyContinue | Select-Object Name,Id,@{n='CPUsec';e={[int]$_.CPU}}
  Start-Sleep 30
  Get-Process dotnet,git -ErrorAction SilentlyContinue | Select-Object Name,Id,@{n='CPUsec';e={[int]$_.CPU}}
  ```

- If one `git` PID sits with frozen CPU: prime suspect is a hidden git
  CREDENTIAL PROMPT (Git Credential Manager window behind the terminal
  or on another monitor — the run waits forever). Check the taskbar
  before killing anything.
- FROZEN (confirmed live 2026-10-21 — both CPU snapshots identical):
  1. Alt-tab / taskbar for a credential window; answering it resumes
     the run unaided.
  2. None → Ctrl+C once; if ignored: `Get-Process git,dotnet -
     ErrorAction SilentlyContinue | Stop-Process` (safe — per-repo
     atomic swaps; nothing half-installed when nothing completed).
  3. State check after the kill:

     ```powershell
     Get-ChildItem C:\ua-estate -Force -Directory | Select-Object Name
     Get-ChildItem C:\ua-estate\scans -Force -Directory -ErrorAction SilentlyContinue | Measure-Object | Select-Object Count
     ```

     Expected: the old cache intact (same count as before the run). If
     scans is EMPTY and a dot-named sibling exists (`.scans.old-*` /
     `.scans.tmp-*`), the hung run moved the cache aside — restore it
     by moving that sibling back to `scans` before anything else, and
     record the sibling name here.
- Blame boundary (how to tell ua-side vs tracemap-side without guessing):
  the "scanning <repo> (i/N)" line prints immediately BEFORE each tracemap
  subprocess launch. NO scanning lines = tracemap never started = the hang
  is in ua's freshness stretch or the environment (blocked git credential
  prompt). Hang AFTER scanning lines = tracemap side — and with a freshly
  built dll carrying the past week of tracemap promotions, the bisect is:
  build tracemap at a known-good older tag and rerun the same 3-repo
  scope; one variable, about a minute.
- Ctrl+C is safe at any point (per-repo atomic swaps; completed repos
  stay cached; a re-run continues). Fallback strategy that fits a day:
  Ctrl+C → run the 3-repo scoped rescan higher up (about a minute) →
  confirm pin facts → re-ingest → registry number; schedule the full
  harmonization pass for a window when you can watch it.
- Backlog linkage: this stretch is silent spot (c) in the activity-output
  candidate — banner + per-repo counter + elapsed for EVERY long phase.

## 2026-10-21 — GIT ARMOR SHIPPED: prompts fail fast + kill timer (a blocked git can never wedge a run again)

- Incident, closed: the estate run froze ~10 min in the silent freshness
  stretch on blocked git processes; killing them by hand resumed the run
  (operator did exactly that). Root shape: an unattended git call can
  block forever on a credential prompt (hidden window) or hung remote.
- Shipped on this branch: every scan-estate git invocation now runs with
  `GIT_TERMINAL_PROMPT=0` + `GCM_INTERACTIVE=never` (a credential problem
  FAILS FAST with "terminal prompts disabled" instead of popping a
  hidden window) and a kill timer — default 120s, `--git-timeout <s>`
  to change, 0 disables — that kills the WHOLE process tree (credential
  manager children included) and skips that repo with a typed reason.
- Verified locally: a fake `git` that sleeps 300s on fetch, with
  `--git-timeout 3` — each repo killed at 3s, per-repo
  `fetch ... FAILED (git fetch timed out after 3s (killed — likely a
  credential prompt or hung remote))`, typed skip lines, and the run
  COMPLETED in 3.1s instead of hanging. Selftest 163 green.
- Note for a run already in flight: the armor applies after re-pulling
  this branch and rebuilding ua — a currently-running process keeps the
  old behavior (manual kill still works).
- Footnote: local toy repos showed "working tree not clean" after an
  in-place scan because tracemap leaves `obj/` behind and the toys have
  no .gitignore; real repos that ignore bin/obj are unaffected.

## 2026-10-21 — LOCAL E2E PASS: the whole chain verified off the estate machine

- Setup: two tiny local git repos with LOCAL bare origins (fetch runs
  offline against a real remote); one repo reproduces the estate's exact
  CPM shape — `ManagePackageVersionsCentrally` declared INSIDE
  `Directory.Packages.props`, PackageVersion pins (Polly, Confluent.Kafka,
  StyleCop.Analyzers), versionless PackageReferences in the csproj.
- Run: current tracemap (post-10-03 CPM extraction, incl. the past week
  of promotions) + this branch's ua build, `scan-estate --scope` over
  both repos.
- Results:
  - Fetch banner, per-repo fetch lines, elapsed summary, freshness
    instant, `scanning <repo> (i/N)` lines, both scans installed —
    the hang did NOT reproduce locally (environmental on the estate
    machine: still pointing at a blocked git/credential).
  - `CentralPackageVersionDeclared` facts emitted for every pin
    (manifestKind Directory.Packages.props, extractor project-file/0.3.0)
    alongside versionless PackageReferenced facts.
  - Ingest over those scans: ZERO "constraint not evidenced" warnings —
    the versionless refs correlated with their pins; the unreferenced
    StyleCop pin correctly reported as unused (honest, not a gap).
- Conclusion: a rescan of the 3 shared repos with a freshly built dll
  WILL collapse the warning wall. No ua fix needed; no tracemap
  regression from the past week.
- Build-command correction (applies to BOTH rescan blocks above): the
  tracked solution is `src/dotnet/TraceMap.sln` — a fresh clone has NO
  root solution, so bare `dotnet build -c Release` at the clone root
  fails (MSB1003, reproduced). Correct dll path:
  `C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll`.
  (The README's root-build instruction needs the same fix — logged.)
- Gap found (does NOT affect the rescan into an existing out dir):
  scan-estate into a FRESH out dir still requires the
  `--delta-package/--delta-old/--delta-new` trio to template delta.json
  and exits 1 at bootstrap AFTER the scans are installed (scans and
  snapshot remain usable via --scans-root). Estate-first gap: fresh-out
  bootstrap should be delta-less like ingest. Logged as a candidate.

## 2026-10-21 — ESTATE-FIRST GAP FIXED: scan-estate completes delta-less (the operator just hit the gap live)

- Live hit: the full estate rescan COMPLETED (all scans installed,
  snapshot written) and then exited 1 with "missing --delta-package
  --delta-old --delta-new" — the estate folder never had a delta.json
  (the delta-optional flow doesn't create one), and bootstrap demanded
  the trio anyway. The scans were safe the whole time.
- Shipped on this branch: no delta.json + NO flags → the run completes
  delta-less — note printed, sidecars bootstrapped (producer template
  empty, no delta target to seed), ingest builds the fixture, and
  plan/report are SKIPPED with a teaching note naming the three delta
  sources. PARTIAL flags remain a typed error (operator mistake, not an
  implied delta-less run). Verified locally end to end + selftest 164
  (new case: scan-estate-delta-less).
- Immediate operator impact: the scans from the errored run are already
  installed — re-running ingest + registry needs NO rescan. The error
  was cosmetic to the evidence; it just looked fatal.

## Open measurement (not a bug)

- Registry unknown count vs the hand-verified baseline (copy/paste;
  add `--out C:\ua-estate\registry.md` to keep a file copy):

  ```powershell
  dotnet $ua registry C:\ua-estate\fixture
  ```

  Record the totals here when available.

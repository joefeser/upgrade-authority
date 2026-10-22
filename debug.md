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

## LAW — the runbook's runbook (binding; overrides habit)

**Every command lives HERE before it exists in chat.** The inverse view, the
determinism check, the lost-producer sweep, the zeroer greps — every
diagnostic of this saga shipped to this file the moment it was invented; the
standing rule is now LAW because a diagnostic that lives only in chat is a
diagnostic that is gone when you need it most (the same lesson as the old
scan folders: evidence and tooling must survive the moment of their creation).
Consequence for the operator loop: when the agent gives you a command in
chat, the correct response is "is that in debug.md" BEFORE running it.

## 2026-10-22 — ZEROER v6: the missing 11 is probably operatorDeclared (the THIRD bucket — never quoted); "1k" sized the SIDECAR

- Operator readback: "producer evidence is 1k" — that is the SIDECAR
  (C:\ua-estate\producer-evidence.v0.json): ~1k bytes = a handful of
  entries = "pretty much empty", exactly as v4 said. It is NOT the fixture
  input the v5 count targeted (111 entries cannot fit in 1k).
- Code facts (verified in source today):
  1. provenanceSummary counts ENTRIES: every producer entry bumps exactly
     one of projectDeclared / operatorDeclared / ciDefined (a bad value is
     a typed refusal, never a silent skip).
  2. internalPackages counts DISTINCT ids across those same entries.
  3. So 44 + 56 = 100 vs internal = 111 closes if operatorDeclared = 11:
     44 + 56 + 11 = 111 entries, all distinct. The third field is the
     missing 11 — it was never quoted.
  4. Ingest stamps every sidecar entry lacking the tracemap scan marker as
     operator-declared — a ~1k sidecar holding ~11 entries is exactly that
     bucket.
- One paste settles which world we are in (sizes BOTH files, decomposes
  the fixture entries by provenance, prints the registry summary on one
  line):

  ```powershell
  $side = 'C:\ua-estate\producer-evidence.v0.json'
  $fix  = 'C:\ua-estate\fixture\input\producer-evidence.v0.json'
  "SIDECAR bytes: $((Get-Item $side).Length)"
  "FIXTURE bytes: $((Get-Item $fix).Length)"
  $fj = Get-Content $fix -Raw | ConvertFrom-Json
  "FIXTURE entries: $($fj.producers.Count)"
  $fj.producers | Group-Object provenance | ForEach-Object { "  $($_.Name): $($_.Count)" }
  "FIXTURE distinct: $(@($fj.producers.packageId | Sort-Object -Unique).Count)"
  "SUMMARY: $((Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).provenanceSummary | ConvertTo-Json -Compress)"
  ```

- Read it as:
  - entries 111 / buckets 44+56+11 / distinct 111 → arithmetic CLOSED,
    no counting bug. The only live bug is the per-repo fact loss (project
    57→44 AND ci 140→56 = both lanes lost in the SAME repos).
  - entries 111 but distinct < 111 → duplicate ids (multi-repo or case
    variants) — overlap, still no counting bug.
  - FIXTURE also ~1k with ~10 entries → the LAST ingest merged almost
    nothing → find the "note: N producer(s) ... merged" line in that
    console (N = what the merge received) and the hunt moves to ingest.
- THEN the v3 discriminator on ONE zeroed repo (still unanswered; bypasses
  the ua cache and ingest entirely):

  ```powershell
  $t = "C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll"
  dotnet $t scan --repo "<repos-root>\<zeroed-repo>" --out C:\z1 --index-ci-producers --ci-hints C:\ua-estate\ci-hints.v1.json
  "FACT LINES: $((Get-Content C:\z1\facts.ndjson).Count)"
  "PRODUCED:   $((Select-String C:\z1\facts.ndjson -Pattern 'PackageProduced' -AllMatches).Matches.Count)"
  ```

  - PRODUCED > 0 → tracemap finds them; ua's cache/ingest eats them → ua fix.
  - PRODUCED = 0 with many fact lines → extractor refusing quietly →
    tracemap dig (paste that repo's csproj top 20 lines into chat, not here).
  - FACT LINES ≈ 0 → the repo was not parsed at all.

## 2026-10-22 — ZEROER v7: rescan decision tree (answers "rescan now or tracemap fix?" — NEITHER yet)

- Why not rescan now: the scans on disk ARE the rescan — fresh 11:02pm,
  right flags, current dll (v3 receipts). Same bits in, same zeroed facts
  out. A rescan only pays AFTER whichever fix the discriminator orders.
- The order:
  1. v6 paste (one paste, right above) → closes 44+56+11=111: counting
     bug or not.
  2. direct-scan bypass on ONE zeroed repo (v6 block, 3 lines) → THE
     tracemap-vs-ua answer:
     - PRODUCED > 0 → NO tracemap fix; ua cache/ingest eats facts →
       ua-side fix, then rescan.
     - PRODUCED = 0 + gap lines naming why → tracemap 0.4.0 guard
       refusing (chain/import/test guards) → fix-or-by-design call,
       likely a small 0.4.1 patch, then rescan.
     - PRODUCED = 0 + NO gaps → extractor silence → paste that repo's
       csproj (top 20 lines) into CHAT, not here; tracemap dig.
- Before any fix + full rescan (a tracemap fix bumps extractor versions →
  whole-estate rescan), preserve the zeroed vintage as evidence:

  ```powershell
  Copy-Item C:\ua-estate\scans C:\ua-estate\scans-zeroed-vintage -Recurse
  ```

- #902 stays MERGE FROZEN until step 2 says tracemap is innocent.

## 2026-10-22 — ZEROER v22: fingerprints decoded (all-distinct = og file's hint ARRAY differs — @main/extra entries — NOT proof of broken; matching normalizes refs) — z4 decides

- Receipt hashes RAW loaded entries; matching strips @ref. An og entry
  with @main (or extra entries) yields a different fingerprint that
  still MATCHES. So z1-scope != z2-scope no longer implies broken hints.
- z4: the PRODUCTION hints file against the CURRENT (post-cleanup) repo:

  ```powershell
  $t = "C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll"
  dotnet $t scan --repo "<repos-root>\<that-repo>" --out C:\z4 --index-ci-producers --ci-hints C:\ua-estate\ci-hints.v1.json
  "z4 ROWS: $((Select-String C:\z4\facts.ndjson -Pattern '"sourceKind":"ci-workflow"' -AllMatches).Matches.Count)"
  "z4 GAPS: $((Select-String C:\z4\facts.ndjson -Pattern '"gapKind":"ci-' -AllMatches).Matches.Count)"
  git -C "<repos-root>\<that-repo>" diff HEAD~1 --stat
  ```

- Read: z4 ROWS > 0 -> the og file matches fine; the PRE-CLEANUP repo
  state silenced z1 AND the estate scan -> the diff --stat names the
  edit that unblocked it (expect: the yml touched during cleanup). z4
  0/0 -> the og entries genuinely never match (spelling) -> replace the
  og file with the z1-hints content, estate rescan, done.

## 2026-10-22 — ZEROER v21: let the RECEIPT fingerprints testify — hints-AS-LOADED compared across z1/z2/z3 (repo content does not enter the hash)

- Receipt code: scope fingerprint = base scope + indexCiProducers +
  ciHintWorkflows AS LOADED (null when zero entries). BC text-compare
  cannot see encoding; the hash cannot lie. Operator note: report.md
  packages produced 3 -> 5 across the two direct scans (the xunit pair
  now evidenced). Calibrate the zero-hints hash with a no-hints scan:

  ```powershell
  $t = "C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll"
  dotnet $t scan --repo "<repos-root>\<that-repo>" --out C:\z3 --index-ci-producers
  "z1 scope (silent, og hints):   $((Get-Content C:\z1\scan-receipt.json -Raw | ConvertFrom-Json).authorizedScopeFingerprint)"
  "z2 scope (fired, fresh hints): $((Get-Content C:\z2\scan-receipt.json -Raw | ConvertFrom-Json).authorizedScopeFingerprint)"
  "z3 scope (no hints at all):    $((Get-Content C:\z3\scan-receipt.json -Raw | ConvertFrom-Json).authorizedScopeFingerprint)"
  ```

- Read: z1 == z3 (and != z2) -> z1 loaded ZERO hints: the og file yields
  nothing as-loaded despite BC == (encoding/BOM invisible to text
  compare) -> recreate the og file (copy z1-hints.json over it), estate
  rescan, done. z1 == z2 (and != z3) -> hints identical as-loaded; the
  REPO's prior state was the silencer -> git checkout the pre-cleanup
  commit, scan, confirm 0/0, bisect the edits. z1 unique -> partial
  load; byte-dump the og file.

## 2026-10-22 — ZEROER v20: receipt-0 reinterpreted (extractor list = facts-derived; all-zeros = RAN-AND-EMITTED-NOTHING) — test the HINTS FILE with a fresh minimal one

- Code receipt: ScanExecutionReceipt builds extractorVersions from
  result.Facts — a silent lane leaves no receipt trace either. The two
  silent sweep exits remain: hints empty after load, or no call matched.
- The estate run matched hints last night; the hints file has lived
  through a day of edits since. Bypass it entirely — write a fresh
  minimal hints file and rescan:

  ```powershell
  $t = "C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll"
  # <shared-workflow-path> = the SAME uses value your release.yml carries
  '{"packWorkflows":[{"uses":"<shared-workflow-path>"}]}' | Set-Content C:\z1-hints.json
  dotnet $t scan --repo "<repos-root>\<that-repo>" --out C:\z2 --index-ci-producers --ci-hints C:\z1-hints.json
  "ROWS: $((Select-String C:\z2\facts.ndjson -Pattern '"sourceKind":"ci-workflow"' -AllMatches).Matches.Count)"
  "GAPS: $((Select-String C:\z2\facts.ndjson -Pattern '"gapKind":"ci-' -AllMatches).Matches.Count)"
  ```

- Read: ROWS > 0 -> the PRODUCTION hints file is the culprit (changed
  since last night's estate run — bytes, not content semantics); diff or
  re-create it and the estate rescan completes. STILL 0/0 -> the repo
  itself silences the sweep with known-good hints; the v18 yml diff
  lines + a hexdump of the uses region become the trail.
- Side quest closed by operator: the dead csproj was the FailedOrPartial
  culprit (gaps 207 -> 7 in the fresh scan; the 200 compilation gaps
  died with it).

## 2026-10-22 — ZEROER v19: probe the ARTIFACTS, not the facts — receipt carries the lane's version; healthy shapes known

- Operator receipts: direct-scan facts 2834 -> 2822 (csproj edits shifting
  counts), CI rows 0 gaps 0 STILL. Healthy control (green repro): the
  scan receipt contains "ci-workflow/<version>" when the lane
  participates, and facts reference release.yml via the ci row.
- Three probes against the SILENT direct-scan output (C:\z1):

  ```powershell
  "receipt lane: $((Select-String C:\z1\scan-receipt.json -Pattern 'ci-workflow' -AllMatches).Matches.Count)"
  "manifest ci : $((Select-String C:\z1\scan-manifest.json -Pattern 'ci-workflow' -AllMatches).Matches.Count)"
  "yml in facts: $((Select-String C:\z1\facts.ndjson -Pattern 'release\.yml' -AllMatches).Matches.Count)"
  ```

- Read: receipt lane 0 -> the LANE NEVER PARTICIPATED (flag/hints plumbing
  bug — nothing parser-level); the count-difference vs the estate scan's
  receipt is the comparison. receipt 1 + yml in facts 0 -> lane ran, saw
  files, emitted NOTHING anywhere -> deepest parser silence, yml diff (v18)
  becomes the only trail.

## 2026-10-22 — ZEROER v18: DIRECT SCAN = TRUE SILENCE (0 rows, 0 gaps, fresh bits, no cache) — cache theory dead; DIFF the two repos' release.yml

- Operator receipts: fact count 2834 before and after the pull (stable
  output); CI ROWS 0, CI GAPS 0 on the direct scan of the real repo with
  the round-4 bits. The estate scan was NOT a cache artifact — the real
  repo silences the lane on current bits. My repro of the photo content
  fires; the uses lines are byte-equal to a working repo's; so the
  divergence lives in the surrounding lines. Compute it exactly:

  ```powershell
  $a = Get-Content "<repos-root>\<that-repo>\.github\workflows\release.yml"
  $b = Get-Content "<repos-root>\<one-working-repo>\.github\workflows\release.yml"
  "a lines: $($a.Count)  b lines: $($b.Count)"
  Compare-Object $a $b | Select-Object -First 24 | ForEach-Object { "$($_.SideIndicator) $($_.InputObject)" }
  ```

- Read: the printed diff lines (=> only-in-b, <= only-in-a) ARE the
  delta between a firing repo and a silent one — whatever construct
  appears only in the silent file is the parser's blind spot. Line-count
  mismatch with a small diff also hints at reordering/blank lines.

## 2026-10-22 — ZEROER v17: everything file-level is EXONERATED (clean indent brute-force: baseline fires, all variants loud) — run the DIRECT SCAN (bypasses the ua cache; the never-run v6 discriminator)

- Clean brute-force on fresh repo (faithful content): baseline 2/4/6 fires
  (ci-rows=1); every other indent scheme is LOUD (1 gap). Combined with
  v13-v16: content, bytes, ignore rules, casing, indentation — every
  file-level failure is green or loud. The real repo's SILENCE cannot
  come from the files. Operator logic check ("not a parse issue or all 4
  would be missing"): the two FOUND packages are PROJECT-lane (csproj
  pack metadata, no yml needed); the yml was only ever the witness for
  the zero-signal two — a yml-lane failure removes exactly those two.
- Remaining theory: the on-disk scan is a STALE-VINTAGE cache artifact —
  ua re-stamps scan-estate.json flags (indexCiProducers=true) and mtimes
  even when serving an older lane-less cached scan. The direct scan
  bypasses the cache entirely and discriminates:

  ```powershell
  $t = "C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll"
  dotnet $t scan --repo "<repos-root>\<that-repo>" --out C:\z1 --index-ci-producers --ci-hints C:\ua-estate\ci-hints.v1.json
  "CI ROWS: $((Select-String C:\z1\facts.ndjson -Pattern '"sourceKind":"ci-workflow"' -AllMatches).Matches.Count)"
  "CI GAPS: $((Select-String C:\z1\facts.ndjson -Pattern '"gapKind":"ci-' -AllMatches).Matches.Count)"
  ```

- Read: CI ROWS > 0 -> current bits find the producers on the real repo;
  the estate scan was a stale-cache artifact -> ua cache-key dig (v3
  resurrected, now with the evidence it lacked). ROWS 0 + GAPS > 0 -> the
  loud refusal names itself. ROWS 0 + GAPS 0 -> true silent repro on the
  real repo; the yml diff continues from its bytes.

## 2026-10-22 — ZEROER v16: CASE is the suspect (matcher is ordinal; OCR cannot tell DevOps/Devops apart — photos read it as Dev0ps); restore works (expired-key theory dead)

- v15 receipts: tabs 0; first 3 bytes 110,97,109 = "nam" (no BOM); all
  spaces. Operator: dotnet restore WORKS on the repo — the Artifactory
  key is fine; FailedOrPartial is a real solo-build failure of some
  project (the 200 compiler gaps are its receipts) and does NOT gate the
  CI lane (unconditional in code).
- Remaining invisible variable: CASE. HintMatches compares ordinal
  (case-sensitive); a one-character case difference in the shared-workflow
  path between repos makes the hint silently not match (call-null return,
  gap-less by design). Photos cannot carry case (they read Dev0ps).
  OCR-proof comparison (hex + -ceq):

  ```powershell
  $a = (Select-String -Path "<repos-root>\<this-repo>\.github\workflows\release.yml" -Pattern 'uses:').Line
  $b = (Select-String -Path "<repos-root>\<one-working-repo>\.github\workflows\release.yml" -Pattern 'uses:').Line
  "equal: $($a -ceq $b)"
  "a-hex: $([BitConverter]::ToString([Text.Encoding]::UTF8.GetBytes($a)).Substring(0,80))"
  "b-hex: $([BitConverter]::ToString([Text.Encoding]::UTF8.GetBytes($b)).Substring(0,80))"
  (Get-Content C:\ua-estate\ci-hints.v1.json -Raw)
  ```

- Read: equal False -> case difference found; fix = add THIS repo's exact
  spelling to ci-hints.v1.json packWorkflows (one line, no code). equal
  True -> fall back to the v15 indentation dump; parser dig continues.

## 2026-10-22 — ZEROER v15: the silent gate FOUND in code (sweep returns gap-less when no call matches); the last mile = INVISIBLE INDENTATION — lead-space signature dump

- Code receipts: CiWorkflowProducerExtractor.ReadWorkflows enumerates the
  LIVE .github/workflows dir (not the git inventory; a missing dir is a
  LOUD gap); SweepHintedProjects exits silently when no captured job call
  matches the hints ("if (call is null) return") — zero rows + zero gaps
  is BY DESIGN for call-not-seen. The parser is whitespace-sensitive and
  photos cannot carry leading spaces, so the real file's indentation is
  the last untested variable. Print it:

  ```powershell
  Get-Content <repos-root>\<that-repo>\.github\workflows\release.yml | ForEach-Object { $lead = ($_ -replace '^(\s*).*$','$1').Length; $t = $_.TrimStart(); "{0,3}|{1}" -f $lead, $t.Substring(0, [Math]::Min(55, $t.Length)) }
  (Select-String -Path <repos-root>\<that-repo>\.github\workflows\release.yml -Pattern "\t" -AllMatches).Matches.Count
  ([System.IO.File]::ReadAllBytes("<repos-root>\<that-repo>\.github\workflows\release.yml")[0..2]) -join ','
  ```

- Line 1 prints every line as LEAD-SPACES|trimmed-text (52 short lines,
  photo-friendly); line 2 = tab count (expect 0); line 3 = first 3 bytes
  (239,187,191 = BOM). With the true indentation the repro rebuild is
  exact and the miss reproduces (or the mystery deepens to the parser's
  job-level uses capture itself).

## 2026-10-22 — ZEROER v14: every kill-mode is LOUD (BOM+CRLF / tabs / no-final-newline / gitignore all leave ci gaps) — the real repo is SILENT; read the scan's own analyzer.log

- Faithful two-file repro (release.yml + build.yml, byte-faithful to the
  operator photos) FIRES: hint matched, rows emitted. Byte variants that
  kill rows all emit 2 ci-workflow gaps (loud). gitignore variants: same,
  loud. The real repo has ZERO ci rows AND ZERO ci gaps — the extractor
  contributed nothing as if it never ran.
- Every scan writes logs/analyzer.log (scanId, commitSha, buildStatus,
  facts count, capability lines). The real repo's copy — with its 200
  compilation gaps — is the next read:

  ```powershell
  Get-Content "C:\ua-estate\scans\<that-repo>\logs\analyzer.log"
  ```

- Read: buildStatus != Succeeded, a missing CI capability line, or any
  error state = the extractor (or its prerequisite) failed for this repo
  while the scan continued — the silent-zero mechanism. Match against a
  WORKING repo's log (any repo that contributed ci entries) if unclear.

## 2026-10-22 — PRE-COMMIT CLEAN-GATE installed (the morning leak's structural fix; operator suggestion)

- push-trial.sh guards PUSHES; a pre-commit hook guards the COMMIT itself
  — any agent, any shell, 1am included. Self-tested in a scratch repo
  (clean passes, employer path refused with the line cited). Installed
  locally on BOTH repos (ua main repo — shared with this worktree — and
  tracemap). Tracked copy: tools/pre-commit-cleangate.
- Work-machine clone (hooks are per-clone, nothing to pull):

  ```powershell
  Copy-Item C:\work\upgrade-authority\tools\pre-commit-cleangate C:\work\upgrade-authority\.git\hooks\pre-commit
  ```

  (after pulling this branch; any clone of tracemap can do the same with
  its copy from the trial branch once synced)

## 2026-10-22 — ZEROER v13: LOCAL REPRO FIRES at BOTH versions (head 1f568e69 AND production cac77b55) — code is innocent for this shape; the difference is the REAL repo. Ghost-project vs yml-bytes splitter

- Scratch repo (reconstructed yml: job-level uses of the exact shared
  workflow + ${{ }} with-conditionals; one signal csproj + one zero-signal
  xunit-flavored csproj): BOTH producer rows emit, the xunit one via
  ci-hint-workflow, zero gaps — at the production commit too. The lane,
  matcher, and sweep handle this shape.
- Operator scan metadata (v12): indexCiProducers true, staleScan false,
  cache.v2, worktree mode — the lane ran for this repo.
- Remaining candidates: (1) the 2 missing csproj are NOT in the scanned
  inventory (ghost projects — sweep has nothing; silence with no gaps is
  exactly this signature), (2) the real yml bytes differ beyond OCR reach.
- Splitter (one paste; run from anywhere):

  ```powershell
  (Select-String "C:\ua-estate\scans\<that-repo>\facts.ndjson" -Pattern '<zero-signal-project>\.csproj' -AllMatches).Matches.Count
  git -C <repos-root>\<that-repo> ls-files "*xUnit*"
  ```

- Read: count > 0 -> the project IS in the pipeline (yml-bytes chase:
  photo the yml TOP half — the on:/jobs: region above the uses line).
  count = 0 + ls-files lists them -> inventory/exclusion bug (tracemap
  dig). count = 0 + ls-files EMPTY -> the csproj are not committed on
  trunk (branch-only or local-only): the scan told the trunk truth and
  the "missing" producers live only outside the scanned history.

## 2026-10-22 — ZEROER v12: release.yml MATCHES the hint (uses: the exact shared workflow, job-level, 52 lines) and no drop-gap exists — check THIS repo's scan metadata (never checked; all flag checks were on zeroed repos)

- WEE read of the operator photo (local only): uses = the exact hinted
  dotnet-release-library.yml@main, job-level with/secrets, 2,285 bytes.
  Textually matches; no ci-workflow-invalid/unsupported gap in facts ->
  the file was not dropped with a receipt. Two suspects left: the scan's
  own flags/vintage for THIS repo, or a same-bits parser behavior (local
  repro from the reconstructed yml).
- The zeroed-repo flag checks (v3) never covered this repo. One paste:

  ```powershell
  Get-Content "C:\ua-estate\scans\<that-repo>\scan-estate.json" -Raw
  ```

- Read: indexCiProducers false or a missing/stale ci-hints hash on THIS
  repo -> the lane never ran for this scan -> ua cache-key dig (why did
  THIS repo's key differ?). All true + matching hashes -> the lane RAN
  and still emitted nothing for a matching file -> tracemap dig; the yml
  gets reconstructed locally from the OCR (52 lines) and repro'd against
  head to find the silent drop.

## 2026-10-22 — ZEROER v11: both present rows are project-lane derived-default; CI lane contributed NOTHING to this repo — the missing 2 are zero-signal projects whose only rescue (the hint sweep) never fired

- WEE read of the operator photo (local Apple Vision only; hosted: none):
  both facts are PROJECT-lane rows — packageIdProvenance derived-default
  (filename id), packSignals "Authors, Description, PackageTags,
  Product", extractor project-file/0.4.2, version empty. Gap tally:
  compilation 200 (noise), PropertyMappingShapeUnsupported 4,
  unknown-candidate 1 — ZERO ci-* rows or gaps: the CI lane contributed
  nothing to this repo. Zero-signal csproj (no pack metadata) refuse
  silently in the project lane BY DESIGN; their only producer path is
  the CI hint sweep, which never fired here (no call seen at all — not
  even a ci-workflow-unsupported/invalid gap for a dropped file).
- v9's regex "returned 0" because the fact property is "package", not
  "packageId" — audit the real shape before pattern-matching (v10's raw
  dump now pins it).
- Fill in the 2 missing names (VS/NuGet) and run:

  ```powershell
  $facts = "C:\ua-estate\scans\<that-repo>\facts.ndjson"
  "MISSING1 anywhere: $((Select-String $facts -Pattern '<missing-package-1>').Matches.Count)"
  "MISSING2 anywhere: $((Select-String $facts -Pattern '<missing-package-2>').Matches.Count)"
  "in registry unknown: $((Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).boundary.unknownPackages -match '<missing-package-1>|<missing-package-2>')"
  "repo has workflows dir: $(Test-Path '<repos-root>\<that-repo>\.github\workflows')"
  ```

- Read: anywhere=0 -> never extracted at all (inventory dig). Registry
  unknown hit -> the registry already flags consumed-but-unproduced —
  unknownPackages intersected with names-known-ours becomes the
  ESTATE-WIDE missing-producer detector. Workflows dir False -> the CI
  lane had nothing to read (root cause in one line).
- PASTES into CHAT (never this file): the repo's release.yml + top
  halves of the 2 missing csproj.

## 2026-10-22 — ZEROER v10: the repo really has 4 packages, the scan found 2 — REAL tracemap gap (v9's "4" was 2 lines x 2 matches)

- Operator receipts: VS/NuGet show 4 packages; 4 csproj files produce;
  facts.ndjson holds 2 PackageProduced LINES (names correct); the old
  count of 4 was AllMatches counting the string twice per line. v9's
  regex returned 0 — the id property is NOT named "packageId" in the
  fact JSON; v10 prints raw lines instead of guessing the shape.
- The 2 missing packages: either REFUSED with a gap naming why (guards),
  or SILENCE (the lane never swept this repo). One paste:

  ```powershell
  $facts = "C:\ua-estate\scans\<known-producer-repo>\facts.ndjson"
  "--- produced lines:"
  (Select-String $facts -Pattern 'PackageProduced' -AllMatches).Line | ForEach-Object { $_.Substring(0, [Math]::Min(300, $_.Length)) }
  "--- gap lines:"
  $gl = (Select-String $facts -Pattern 'AnalysisGap').Line
  "GAP COUNT: $(@($gl).Count)"
  $gl | ForEach-Object { if ($_ -match '"[^"]*[Kk]ind[^"]*"\s*:\s*"([^"]+)"') { $Matches[1] } } | Group-Object | Sort-Object Count -Descending | ForEach-Object { "$($_.Name): $($_.Count)" }
  ```

- Read: gap kinds naming ci-producer-id-unevidenced / ci-hint-id-
  unevidenced for the 2 missing projects -> guarded refusals -> paste
  those gap lines; GAP COUNT 0 -> silence -> the lane never ran for this
  repo (hint call not matched / workflow dropped) -> tracemap dig on
  the repo's .github/workflows + the 2 missing csproj (paste their
  csproj top halves into chat, never into this file).

## 2026-10-22 — ZEROER v9: "only 2 show up not all 4" — facts vs DISTINCT packages (one paste decides)

- Operator: v8.1 listed 2 fixture entries for the known producer; the
  fact spot-check had counted 4. Almost certainly correct, not loss: a
  package claimed by BOTH lanes (project file + CI workflow) = 2 FACTS
  for 1 package; the fixture dedups by (repo, package) by design. 2
  packages x 2 lanes = 4 facts -> 2 entries.
- Decider — print the packageId of EVERY PackageProduced fact line for
  that repo, then count distinct names:

  ```powershell
  $ids = (Select-String "C:\ua-estate\scans\<known-producer-repo>\facts.ndjson" -Pattern 'PackageProduced' -AllMatches).Line | ForEach-Object { if ($_ -match '"packageId"\s*:\s*"([^"]+)"') { $Matches[1] } }
  $ids
  "DISTINCT: $(@($ids | Sort-Object -Unique).Count)"
  ```

- 2 distinct (each twice) -> both lanes see the same 2 packages: CORRECT,
  nothing lost, saga stays closed. 4 distinct -> 2 packages really lost
  between facts and fixture -> ingest dig (ua side), fix immediately.

## 2026-10-22 — ZEROER v8: NO-ZEROER reframe (operator: every zero-list repo truly does not produce) — close-out checks

- Operator verdict on the zero list: "every line with 0 does not produce a
  package. so the zero list is correct." If that holds, THERE IS NO ZEROER:
  the falling counts were the 0.4.0 guards (test guard, parsed-XML
  eligibility, chain strictness) retiring FALSE producers the older
  over-broad versions credited. The crisis inverts — nothing real was lost;
  the old 142-era numbers were inflated, not the new ones deficient.
- Arithmetic already CLOSED (v6 receipts): current 55 project + 56 ci +
  0 operator = 111 entries, 111 distinct, internal = 111. The old "44" was
  a stale registry.json vintage. No counting bug anywhere.
- Two checks close the hunt for good (both cheap):

  ```powershell
  # a) CURRENT internal list — eyeball: any package you KNOW we publish
  #    that is MISSING? (the earlier 142-name list passed the same eyeball)
  (Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).boundary.internalPackages
  # b) spot-check a KNOWN producer's estate facts — 0 rows there = REAL bug
  (Select-String "C:\ua-estate\scans\<known-producer-repo>\facts.ndjson" -Pattern 'PackageProduced' -AllMatches).Matches.Count
  ```

  - (a) complete + (b) > 0 -> NO BUG EVER; the 142->111 story is
    false-positives correctly retired; #902 UNFREEZES.
- FINAL eyeball (v8.1) — the names behind a known producer's rows (paste
  beats a photo: names eyeball instantly, counts don't):

  ```powershell
  $fj = Get-Content C:\ua-estate\fixture\input\producer-evidence.v0.json -Raw | ConvertFrom-Json
  $fj.producers | Where-Object { $_.repo -like '*<repo-fragment>*' } | ForEach-Object { "$($_.repo)  $($_.packageId)  [$($_.provenance)]" }
  ```

  Operator result 2026-10-22: (a) list correct; (b) known producer = 4
  rows (old sweep showed 2 -> the 0.4.0 chain finds MORE real ids, not
  fewer — growth, not loss). Remaining: the 4 NAMES look right ->
  saga CLOSED, no fix, no rescan needed.
  - (a) missing known names -> those names' true producer repos are the
    direct-scan targets (v6 second fence) — that IS the bug trail.
  - (b) = 0 on a repo that really produces -> direct-scan it (v6 fence);
    producers appear = ua pipeline bug; none = tracemap bug.
- The determinism question (57->55->44->55) collapses with the reframe:
  each drop tracked a tracemap behavior change (guard landings, version
  bumps) EXCEPT any same-bits drop — a same-bits rerun of ingest+registry
  printing 55/0/56 again settles it in one command if it ever matters.

## 2026-10-22 — ZEROER v5: accumulator DEAD (operator sidecar empty; merge writes the FIXTURE copy only — code-verified)

- The 111-vs-100 arithmetic needs the full decomposition instead:

  ```powershell
  # full summary incl. operatorDeclared:
  (Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).provenanceSummary
  # the TRUE entry count behind internal=111:
  (Select-String C:\ua-estate\fixture\input\producer-evidence.v0.json -Pattern '"packageId"' -AllMatches).Matches.Count
  ```

- entries(111?) vs summary(100) names the uncounted bucket.

## 2026-10-22 — ZEROER v4: cache DEAD (fresh 11:02pm scans, flag true) — the ARITHMETIC points at the SIDECAR ACCUMULATOR

- Run C math: project 44 + ci 56 = 100 but internal = 111. Eleven extra
  producers came from the producer-evidence SIDECAR — scan-estate merges
  scan facts into it each run PRESERVING prior entries: old-vintage
  producers persist and every registry counts them. The drift may be
  sidecar-vs-facts blending, and the 142 itself may have carried ghosts.
- Expose the accumulator:

  ```powershell
  (Select-String C:\ua-estate\producer-evidence.v0.json -Pattern '"repo"' -AllMatches).Matches.Count
  (Select-String C:\ua-estate\producer-evidence.v0.json -Pattern 'ci-defined').Count
  (Select-String C:\ua-estate\producer-evidence.v0.json -Pattern 'project-declared').Count
  ```

- Sidecar holds 100+ entries while current scans produce fewer -> the
  accumulator is the villain; zeroed repos may ALWAYS have been fact-zero
  (sidecar-resident producers from an older vintage). ua fix: scan-derived
  entries REBUILT fresh every run (stale ones expire loudly); sidecar keeps
  ONLY operator-declared entries permanently.
- Still run the DIRECT SCAN (v3 block) on one zeroed repo — zero facts AND
  zero gaps there = extractor silence (tracemap dig); anything else = sidecar.

## 2026-10-22 — ZEROER v3: TOTAL SILENCE (no yml, no ci-workflow, no imports) = the CI lane never ran for these scans; suspect = CACHE served a flag-less vintage

- Every guard leaves a mark; these facts have none. One thing is totally
  silent: the lane not running at all. Check the cached scan's own metadata:

  ```powershell
  Get-Content "C:\ua-estate\scans\<zeroed-repo>\scan-estate.json"
  # "indexCiProducers": true or false??  false = cache bug (ua side)
  ```

- Corroborate with a direct scan, bypassing the estate cache:

  ```powershell
  $t = "C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll"
  dotnet $t scan --repo "<repos-root>\<zeroed-repo>" --out C:\z1 --index-ci-producers --ci-hints C:\ua-estate\ci-hints.v1.json
  (Select-String C:\z1\facts.ndjson -Pattern 'PackageProduced').Count
  ```

- Direct scan produces producers where the estate had zero -> cache bug,
  ua-side, fix immediate.

## 2026-10-22 — ZEROER v2: no .yml and no ci-workflow ANYWHERE in the zeroed facts — the CI lane never ran for these repos

- Implication: these were PROJECT-lane producers (csproj metadata tier-3, no
  yaml traces) — and the project lane is what zeroed the family (finally
  explains projectDeclared 57->44 too).
- CORRECTION: the earlier 'customimports 0' count grepped the DECISION
  STRING which never appears in facts — the guard is ALIVE as prime suspect.
  The Shared family centralizing via explicit <Import> in every csproj would
  trip it family-wide. The REAL greps:

  ```powershell
  $z = Get-ChildItem C:\ua-estate\scans -Directory -Filter '*EnterpriseLogging*'
  Select-String "$($z.FullName)\facts.ndjson" -Pattern 'imports a \.props' | Select-Object -First 3
  Select-String -Path "<repos-root>\<the-logging-lib-repo>\*\*.csproj" -Pattern '<Import' | Select-Object -First 3
  ```

- Lights up -> the custom-imports guard over-fired; fix = follow the explicit
  Import when its target is INSIDE the repo (bounded-evaluate; refuse only
  what is genuinely unevaluable / outside).

## 2026-10-22 — ZEROER UPDATE: props hypothesis DEAD (no Directory.Build.props in the family); new prime suspect = parser-backed hint calls

- Operator check: zeroed repos have NO Directory.Build.props — inheritance
  cannot be the zeroer. New prime suspect: the Shared family shares a
  release.yml TEMPLATE; hint calls now require the file to parse cleanly
  through the bounded structural YAML parser — ONE rejected construct in
  the template (anchor / block scalar / flow collection / unsupported
  shape) zeroes EVERY repo using it simultaneously.
- Diagnostics:

  ```powershell
  # parser rejections leave ci-workflow gaps in the zeroed repo's facts:
  $z = Get-ChildItem C:\ua-estate\scans -Directory -Filter '*EnterpriseLogging*'
  Select-String "$($z.FullName)\facts.ndjson" -Pattern 'ci-workflow' | Select-Object -First 3

  # the delta names the construct: zeroed vs still-producing release.yml
  Get-Content "<repos-root>\<zeroed-repo>\.github\workflows\release.yml" -Raw
  Get-Content "<repos-root>\<still-producing-repo>\.github\workflows\release.yml" -Raw
  ```

- ci-workflow-unsupported / ci-workflow-invalid in the zeroed repo = case
  closed: the fix is either teaching the parser the template's construct or
  (honest fallback) a gap that names the file so the hint can't silently
  vanish. NOTE: a hint call that silently vanishes because its file didn't
  parse is EXACTLY the "assertion silently dropped" class the strict-UTF-8
  fix closed — same law, new door.

## 2026-10-22 — NAME THE ZEROER (whole-repo producer loss; prime suspect = root Directory.Build.props identity)

- Whole-repo zeroing can't be per-project policy. Prime suspect: a ROOT-level
  Directory.Build.props declaring AssemblyName/PackageId — the inherited-identity
  guard then refuses EVERY project in that repo at once; repos without root
  props keep theirs ("some lost all"). One grep on a zeroed victim names it:

  ```powershell
  $z = Get-ChildItem C:\ua-estate\scans -Directory -Filter '<one-zeroed-repo>'
  Select-String "$($z.FullName)\facts.ndjson" -Pattern 'ci-hint-id-unevidenced' | Select-Object -First 3
  Select-String "$($z.FullName)\facts.ndjson" -Pattern 'Directory.Build' | Select-Object -First 3
  Get-Content "<repos-root>\<that-repo>\Directory.Build.props" -ErrorAction SilentlyContinue | Select-Object -First 15
  ```

- Gap says "nearest Directory.Build.props declares a package identity" + the
  props shows patterns -> CONFIRMED: inherited identity must FOLLOW THE CHAIN
  (evaluate what the props declares; $(MSBuildProjectName) interpolates
  per-project) instead of refusing. Same fix shape as the AssemblyName
  recovery, one level up. Likely also explains projectDeclared 57->55->44
  (the project lane's tier-3 shares the inspection).
- Gaps absent + thin facts (Reduced analysis level) -> load degradation
  instead; the DETERMINISM CHECK double-scan below becomes the priority.

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

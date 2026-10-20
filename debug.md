# debug.md — trial-branch living debug log

Purpose: append-only log of the live validation loop on this branch. Built up
as we debug; tossed when the branch is purged. This is a PUBLIC branch: no
real repo names, URLs, fact ids, or estate-identifying content — generic
labels only ("the 3 shared repos", "the estate machine").

Format per entry: date — symptom / evidence / hypothesis / next diagnostic.

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
  dotnet build -c Release

  # 1) rescan ONLY the 3 affected repos into the existing estate out dir
  #    (plain-list scope file; one repo folder name per line)
  @'
  <repo-folder-1>
  <repo-folder-2>
  <repo-folder-3>
  '@ | Set-Content C:\ua-estate\rescan-scope.txt -Encoding ASCII

  dotnet $ua scan-estate --repos-root <repos-root> `
    --tracemap C:\tracemap\bin\Release\net10.0\tracemap.dll `
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
- Should you rescan everything? YES — one full pass harmonizes every
  repo's evidence at one vintage (also picks up the 10-10 CI-producer
  indexing and 10-18 regex hardening). The current cache rule keys reuse
  on HEAD + dll hash + flags, so after this pass stale-vintage mixing
  cannot silently recur: entries not matching the new dll are rescanned,
  matching ones reused.
- Cost expectations: this is the "real run" price — the fetch phase
  (network, per-repo) plus a tracemap scan for every stale entry (likely
  most of the estate). It narrates progress per repo; Ctrl+C is safe
  mid-run and a re-run continues from the cache. `--parallel 8` speeds
  the fetch phase.
- Command (estate machine; substitute your repos root):

  ```powershell
  cd C:\tracemap
  git pull
  dotnet build -c Release
  # confirm the artifact is FRESH — pull without rebuild is the exact trap above
  Get-Item C:\tracemap\bin\Release\net10.0\tracemap.dll | Select-Object LastWriteTime

  dotnet $ua scan-estate --repos-root <repos-root> `
    --tracemap C:\tracemap\bin\Release\net10.0\tracemap.dll `
    --out C:\ua-estate --parallel 8
  ```

  Then re-run the ingest exactly as before (up-arrow history), then the
  registry measurement below. The 3-repo scoped rescan higher up remains
  a valid shortcut if a number is needed before the full pass fits the
  calendar.

## Open measurement (not a bug)

- Registry unknown count vs the hand-verified baseline (copy/paste;
  add `--out C:\ua-estate\registry.md` to keep a file copy):

  ```powershell
  dotnet $ua registry C:\ua-estate\fixture
  ```

  Record the totals here when available.

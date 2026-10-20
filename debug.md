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

## 2026-10-21 — pins EXIST but warnings claim "no central pin" (OPEN — evidence-pipeline bug)

- Symptom: residual warnings say "no central pin for it" for packages
  (Polly, Confluent.Kafka, StyleCop.Analyzers, Microsoft.Extensions.*,
  internal shared packages) in the 3 shared repos.
- Evidence (local photo OCR of one repo's `Directory.Packages.props`):
  - The file EXISTS at the repo root; CPM is enabled.
  - The warned packages ARE pinned there (Polly 8.6.6, Confluent.Kafka
    2.14.0, StyleCop.Analyzers 1.2.0-beta.556, MsExt
    Configuration/DI/Hosting 10.0.5, plus internal shared packages).
  - Notable shape: `ManagePackageVersionsCentrally` is declared INSIDE
    `Directory.Packages.props` itself (a valid NuGet pattern; many repos
    put it in Directory.Build.props instead).
- ua contract: pin evidence arrives as facts of type
  `CentralPackageVersionDeclared` (Ingest.cs, CPM section), joined on
  repo + package. Other repos' pins demonstrably flow (unused-pin notes +
  the flood collapse prove the general path works).
- Hypotheses, as a decision table:
  1. The repo's scan facts contain NO `CentralPackageVersionDeclared`
     rows → tracemap extraction gap. Top suspect: the extractor conditions
     on where the CPM flag lives (e.g. expects Directory.Build.props) and
     misses the flag-inside-Directory.Packages.props shape.
  2. The scan facts DO contain them → ua join bug (repoKey derivation,
     package casing, or origin form mismatch between reference facts and
     pin facts).
- Next diagnostic (operator, on the estate machine — copy/paste,
  PowerShell 5.1-safe; adjust the scans root if yours differs):

  ```powershell
  $scan = Get-ChildItem C:\ua-estate\scans -Directory -Filter '*Kafka*' | Select-Object -First 1
  Select-String "$($scan.FullName)\facts.ndjson" -Pattern 'CentralPackageVersionDeclared','Polly'
  ```

  Paste the result back. No match → hypothesis 1 (tracemap extraction);
  match → hypothesis 2 (ua join).

- Impact: the fix should collapse the 3-repo residual warning wall and
  flip those repos' classification; until then, registry unknown counts
  carry a known discount for exactly these 3 repos.

## Open measurement (not a bug)

- Registry unknown count vs the hand-verified baseline (copy/paste;
  add `--out C:\ua-estate\registry.md` to keep a file copy):

  ```powershell
  dotnet $ua registry C:\ua-estate\fixture
  ```

  Record the totals here when available.

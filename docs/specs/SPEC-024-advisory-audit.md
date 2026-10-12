# SPEC-024 — `advisory-versions.v1` + `ua audit` — the security-intelligence report

**Status:** reviewed — delta round verdict pending v4 folds (records in `docs/reviews/spec-024/`)
**Author:** ZCode (coordinator); design-talk outcome item 3 (`docs/planning/17-design-talk-brief.md`): CVE flagging is a FEED-TRUTH problem — drift's `feed-versions.v1` pattern, never live network.
**Date:** 2026-10-11 (revised same day)
**Depends on:** SPEC-018 §3-4 (the feed-truth pattern, the inventory surface, `--out` conventions, the D-template discipline) · SPEC-020 (the registry boundary incl. `anomalies.producedAndDeclaredExternal`) · SPEC-021 (case-insensitive identity, the estate-spelling rule) · OSV/GHSA export realities (§6's exporter contract)

## 1. Purpose

"Which of OUR packages are affected by known advisories?" is the estate-security question the registry boundary makes answerable: the registry knows what the estate produces vs declares-external vs unknown; drift knows what the estate has INSTALLED. This spec adds the third leg: an operator-provided **advisory truth file** (`advisory-versions.v1` — exported from OSV/GHSA or hand-curated, stated-not-verified-live) and `ua audit`: join installed versions against advisory truth, classify honestly, and report WITH the boundary — "our code exposed" vs "the internet's problem (declared external)" vs "unknown ownership — the honest residue."

No network, ever. No planner behavior change. A report, not a gate: exit 0 for any successfully loaded input; affected findings are content, not failures.

## 2. Advisory truth (`advisory-versions.v1`)

```json
{"schemaVersion":"advisory-versions.v1","source":"operator-provided","asOf":"2026-10-11",
 "advisories":[{"advisoryId":"GHSA-xxxx-xxxx-xxxx","packageId":"Newtonsoft.Json","ecosystem":"NuGet",
                "vulnerableVersions":["12.0.3","13.0.1"],"fixedIn":"13.0.2","severity":"high","title":"..."}]}
```

- `source` ∈ `operator-provided | osv-export | ghsa-export` (validated; anything else ⇒ exit 5 — the drift precedent). `asOf` optional, echoed verbatim, OMITTED when absent (never `null`, never clock-generated).
- Per advisory: `advisoryId` (non-empty), `packageId` (non-empty), `severity`/`title` optional (echoed), and one or both of:
  - `vulnerableVersions` — **two-tier entry matching** (never one rule for all entries): a NUMERIC entry (parseable by the comparer) matches an installed version by `Apply.CompareCore` equality (padding-aware: `12.0.3` matches `12.0.3.0`); a NON-NUMERIC entry (prerelease or otherwise unparseable) matches by **ordinal verbatim equality**. Nothing in the list is ever skipped, and **an exact-list match of either tier WINS over the unclassified rule** — a listed prerelease installed verbatim is affected: the list is exact evidence, not a parse.
  - `fixedIn` — an installed version strictly BELOW `fixedIn` by `CompareCore` is affected; ≥ is not-affected; either side prerelease/unparseable ⇒ `unclassified`.
  - "Usable" = at least one entry of either tier in the list (every string is list-usable), or a parseable `fixedIn`. An advisory with NEITHER ⇒ exit 5 naming the id (a truth file that cannot classify anything is malformed).
- **Identity/dedup**: the dedup key is `(advisoryId, packageId)` — one OSV id legitimately covers multiple packages; the same key twice at identical content collapses silently, at contradictory content ⇒ exit 5. All package-id comparisons OrdinalIgnoreCase; the ESTATE spelling is echoed in findings.
- `ecosystem` when present is compared **OrdinalIgnoreCase** (`"NuGet"` is the OSV spelling); non-nuget rows are skipped with a visible stderr note (warn+skip, the drift folder-feed precedent) BEFORE validation.
- **Missing `--advisories` file ⇒ exit 1 naming the path** (the drift missing-feed precedent); absent flag / bare flag ⇒ exit 1 (usage). Schema/shape violations ⇒ exit 5 naming the file (the LoadFeedFile precedent).

### The exporter contract (soundness, not just shape)

Real OSV/GHSA records express vulnerability as `affected[].ranges[].events` — possibly MULTIPLE windows per package — plus `last_affected` (inclusive, no fix). **A single scalar `fixedIn` cannot soundly represent multi-window or `last_affected` advisories** (collapsing either direction fabricates a clean or dirty verdict silently). The exporter's rules, stated as MUSTs:

1. Multi-window advisories split into one entry per window (each with its own `fixedIn` or an exact `vulnerableVersions` list), **id-suffixed per window** (`.w1`, `.w2`, …) — the `(advisoryId, packageId)` dedup key makes same-key-different-window entries contradictory otherwise.
2. `last_affected` converts to an exact list ONLY when the exporter can enumerate the package's FULL released-version list over `[introduced, last_affected]` — a partial enumeration silently clears unlisted vulnerable installs.
3. A window the exporter cannot soundly represent — open-ended `introduced` with no `fixed`/`last_affected` (vulnerable forever; EOL packages), or an unenumerable range — MUST be exported as an explicit catch-all entry (`vulnerableVersions` listing every installed version the estate could see is impossible, so instead: a synthetic `fixedIn`-absent entry whose title carries `unsound-window: unrepresentable — manual review` and whose list contains a sentinel version pattern is NOT invented) — in short: **the exporter refuses and reports the gap; it never drops the window silently.** Concretely: unsound windows are exported with `severity: "export-gap"` and an empty actionable payload; `ua audit` renders them as `unclassified` findings titled with the export-gap marker (visible absence, never silence).

Flattening to one `fixedIn`, or dropping an unsound window, is an unsound export — the exporter contract, not ua behavior.

**Export-gap entries are EXEMPT from the neither-usable ⇒ exit-5 rule** (they are deliberately empty-payload: `severity: "export-gap"` is the marker, echoed on the finding; `title` remains dropped — the severity value IS the title-equivalent signal). An export-gap entry always renders as an `unclassified` finding against its package (every installation unclassified, reason `export gap: the advisory's vulnerable window could not be soundly exported — manual review`) — visible absence, never a killed report.

## 3. The command

```
ua audit <fixture-dir> --advisories <file> [--out <file>]
```

- Loads drift's exact fixture surface (delta = loader precondition only, content never read; malformed optional scope still exit 3).
- **Inventory = drift's inventory, INCLUDING its null-version rule verbatim**: consumer facts (exact pins) + lockfile-class rows incl. deps.json; **rows with no resolved version are SKIPPED with the same stderr note** (unevidenced resolution never counts — a version join cannot classify a version it cannot see; the packageId-alone rule belongs to the REGISTRY's consumed set, not to audit). This is drift parity, deliberately.
- Each installed version joins every advisory for its package (case-insensitive): **affected** (exact-list match, or strictly-below `fixedIn`), **not-affected** (≥ `fixedIn` with no list match), **unclassified** (prerelease/unparseable in a `fixedIn` comparison — exact-list matches still win per §2).
- **Boundary per package, REGISTRY semantics exactly**: produced ⇒ `internal`; declared-external AND NOT produced ⇒ `external`; neither ⇒ `unknown`. A package BOTH produced and declared-external (the registry's anomaly) ⇒ `internal` **with the anomaly named on the package row** (`"anomaly": "producedAndDeclaredExternal"` — the registry's own spelling) — surfaced, never silently resolved.
- Output spelling: **drift's 3-level rule** (lockfile > deps.json > declared-pin, ordinal-first within each) — audit emits only inventory packages.

## 4. Output — `audit.v1` (canonical JSON, house writer; `--out` per the SPEC-017/018 convention)

```json
{
  "schemaVersion": "audit.v1",
  "advisories": {"source":"operator-provided","asOf":"2026-10-11","advisoryCount":2,"consideredCount":2},
  "summary": {"packagesObserved":3,"packagesListed":2,"affectedInstallations":2,"notAffectedInstallations":1,
              "unclassifiedInstallations":1,"byBoundaryAffected":{"internal":1,"external":1,"unknown":0},
              "advisoriesWithAffected":2},
  "packages": [
    {"packageId":"Newtonsoft.Json","boundary":"external",
     "findings":[{"advisoryId":"GHSA-...","severity":"high",
                  "installations":[{"repo":"billing","version":"12.0.3","evidence":"lockfile","status":"affected"}]}]}
  ]
}
```

Every count pinned (installation arithmetic is over DEDUPED `(repo, version)` rows — drift's exact dedup, evidence class resolved by its precedence, never part of the key — counted once per package no matter how many advisories flag it):

- `advisoryCount` = the file's post-collapse total; `consideredCount` = post-ecosystem-skip (they differ only when rows were skipped — visible absence).
- `packagesObserved` = the inventory size (ALL installed packages — drift's field meaning); `packagesListed` = rows in `packages[]`.
- `affectedInstallations` / `notAffectedInstallations` / `unclassifiedInstallations` = per-PACKAGE deduped installation counts by that installation's WORST status across advisories (affected > unclassified > not-affected) — one install counts once.
- `byBoundaryAffected` = affected installations per boundary (the our-code headline); `advisoriesWithAffected` = advisories with ≥1 affected installation anywhere (an all-clean advisory is the advisories' silence — but see `notAffectedInstallations`, which makes "checked and clean" VISIBLE rather than inferred).
- **`findings[].installations[]` carries the PER-ADVISORY classification** (each install's status under THAT advisory) — this is where an `unclassified` verdict and its A-reason survive even when the package-level summary counts the install under a worse advisory; the merged view is only ever the summary arithmetic. `reason` rides unclassified installation rows ONLY (the drift rule the validator enforces). `title`/`severity` are echoed on the FINDING row (severity) — `title` is dropped (severity is the graded signal; the id already identifies).
- A list-only advisory with NO match and NO `fixedIn` ⇒ not-affected — the exact list is exhaustive.
- `packages[]` admission: any package with ≥1 advisory INTERACTION of any status (affected, unclassified, or not-affected) — all-clean invisibility is thereby avoided for advisories that touch the estate; a package no advisory names is genuinely absent (the advisories' scope, not audit's). Installations ordered `(repo, version)` ordinal; findings by advisoryId ordinal; `anomaly` only when present (value `producedAndDeclaredExternal` — the registry's own spelling).

## 5. Acceptance criteria

1. **F-audit fixture** (hand-written, house style, flagged as a soft divergence from the real-ingest convention): internal producer package with a vulnerable install; declared-external package with installs both sides of `fixedIn` (affected + not-affected); unknown-ownership package with an exact-list match incl. a padding-equality row (`12.0.3` vs listed `12.0.3.0`); a listed-prerelease exact match (affected — the list wins, either tier); an unlisted prerelease vs `fixedIn` (unclassified, reason pinned); the produced∩declared-external anomaly package (internal + anomaly named). **Multi-advisory arithmetic rows (the v3 additions):** one install affected by advisory A and unclassified by advisory B (counts ONCE as affected; the B-row's A-reason survives inside B's findings entry); one advisory entirely not-affected on this estate (`advisoriesWithAffected` < `advisoryCount` — visible clean); one advisory id spanning TWO packages (the `(advisoryId, packageId)` key); one `export-gap` entry (unclassified finding titled with the gap marker). Golden `audit.v1` byte-exact; plan/report goldens keep it a corpus fixture.
2. Truth-file validation (all exit 5 naming the file): bad `source`, schemaVersion mismatch, contradictory duplicate `(advisoryId, packageId)`, empty ids, neither-side-usable; identical duplicates collapse; non-nuget rows skip with a note (selftest).
3. Missing `--advisories` file ⇒ exit 1 naming the path; absent flag ⇒ exit 1 (selftest).
4. Parity (scoped): every `fixedIn` classification is row-by-row `Apply.CompareCore` — the drift-parity structural test; exact-list equality is padding-aware `CompareCore` equality (selftest).
5. Canonical output: `--out` == stdout bytes, no BOM, sanitized identical, bare flag refuses; determinism + permutation invariance; reason templates pinned verbatim (A1 `installed {version} not a plain numeric pin`, A2 `advisory fixedIn {fixedIn} not a plain numeric pin`, A3 `both installed {version} and advisory fixedIn {fixedIn} not plain numeric pins` — the D1-D3 adaptations) (selftest).
6. Validator grows `audit.v1` structural checks (closed key set, status/boundary vocabularies, every summary field's pinned computation, the A-templates) and the golden count rises; existing selftest/goldens untouched except the new fixture's rows; counts swept.
7. The samplerepos harness grows an audit step over a small committed advisory file (the offline OSV-export demo, incl. one multi-window SPLIT example demonstrating the exporter contract); PowerShell 5.1-safe snippets.

## 6. Out of scope (typed, honest, logged)

- **Live OSV/GHSA fetching** — never; the operator exports.
- **Native multi-window/last_affected shapes** — the exporter contract (§2) makes sound exports possible NOW via splitting; a native shape is a later versioned addition.
- **Range syntax beyond exact-list + fixedIn** — the comparer refuses to guess.
- **Transitive-closure attribution beyond the inventory** — the inventory is what drift sees.
- **Blocking/gating semantics** — audit reports; policy is not this spec.
- **npm/other ecosystems** — skipped visibly (warn+note, the drift folder-feed precedent), never guessed.
- **`introduced`-lower-bound windows exported as bare `fixedIn`** — the exporter flattens `[introduced, fixed)` to fixedIn; installs BELOW `introduced` (a prior, unaffected line) read affected. The exporter contract's split rule covers the common case; an `introduced`-aware entry shape is a later version. Receipted at implementation review (a real OSV export shape; the failure is an over-report, never a fabricated clean).
- **Cross-command fixture-generation races** (ingest swapping a fixture mid-audit) — the shared `LoadEngine` surface every command uses; a same-process invariant, receipted (the corpus never mutates fixtures concurrently; the harness serializes its stages).

# SPEC-015 — Gap scope: package-relevant gaps; compile health as notes

**Status:** draft — round 1 review (brief-025)
**Author:** ZCode (coordinator); decision by Joe 2026-10-04 (adopts the coordinator recommendation)
**Date:** 2026-10-04
**Depends on:** SPEC-005 §3 (ingest coverage) · SPEC-007/012 (coverage-gap surfacing) · SPEC-000 §3 (three-state classification honesty)

## 1. Purpose

Which scan problems make a repo's package coverage `gaps` (and therefore block `not-affected`)? Today: **everything** — a broken C# file, a wrong SDK on the scanner, any diagnostic. Because `not-affected` requires complete coverage, real estates (where half the repos have some compile diagnostic) yield plans that say "147 unknown, 3 safe" — honest but useless. The tool's core product is telling you where you *don't* have to work.

**The rule (Joe-approved):** only problems that genuinely undermine the **package evidence** count as gaps — lockfile parse failures, unreadable/unsupported manifests, skipped facts, unevidenced constraints/versions. Compile/build health (broken C# files, workspace/SDK diagnostics, buildStatus, analysisLevel) becomes a **visible informational note** — still in the report, still honest, never blocking.

## 2. Classification (one function, one place)

A single classifier (`ClassifyScanProblem`) maps every coverage problem to `Gap` | `Note`. Inputs are classified STRUCTURALLY (by tracemap `gapKind` property / manifest field), never by message-text parsing; anything unclassifiable defaults to `Gap` (conservative — unknown problems still block).

| Source | Kind | Class |
|---|---|---|
| AnalysisGap fact, `gapKind` contains `packages-lock` (parse/read/unsupported/group/entry) | lockfile evidence loss | **Gap** |
| AnalysisGap fact, `gapKind` = `CompilationDiagnostic` / `WorkspaceDiagnostic` / `SdkResolutionFailed` | compile/build health | **Note** |
| AnalysisGap fact, `diagnosticKind` = `workspace` or `compilation` (any `gapKind` — e.g. `MSBuildRegistrationFailed`) | workspace/compile health by its own structural kind field | **Note** |
| AnalysisGap fact, **no `gapKind`/`diagnosticKind`**, `ruleId` = `repo.manifest.v1`, `message` starts `Compiler diagnostic category:` / `Workspace diagnostic category:` | manifest-extractor echo of compile/build health (real scans carry these message-only facts) | **Note** |
| AnalysisGap fact, **no `gapKind`/`diagnosticKind`**, `ruleId` = `csharp.syntax.*`, any message | syntax-level diagnostic of the C# source (a broken .cs file — the spec's title case) | **Note** |
| AnalysisGap fact, any other shape | unknown | **Gap** (conservative default) |

The message-only rule above is the ONE narrow message prefix match, documented because tracemap's manifest extractor emits diagnostic echoes as bare messages (verified against committed scans); every other classification stays structural. |
| Ingest-generated: `constraint not evidenced`, `resolved version not evidenced`, `producer version unevidenced`, `skipped fact…`, `PackageProduced ecosystem … not supported`, `central pin version not evidenced` | package evidence qualification/loss | **Gap** |
| Ingest-generated: `central pin … no non-lockfile reference consumes it`, `alternate snapshot skipped` | deliberate, non-lossy | **Note** |
| Manifest `buildStatus` ≠ Succeeded | compile/build health | **Note** |
| Manifest `analysisLevel` ≠ Level1SemanticAnalysis | semantic-analysis depth (V0 plans never consume semantic tiers) | **Note** |
| Manifest `knownGaps[]` entry that **matches a fact already classified Note** (exact message equality against a Note-classified AnalysisGap message in the same repo — a structural join, not text parsing: the manifest is RESTATING a fact we already classified) | compile/build health restated | **Note** |
| Manifest `knownGaps[]` entry with no matching fact | scanner-declared, opaque to us | **Gap** (conservative; we cannot introspect what the scanner meant) |
| Repo with zero facts | total evidence loss | **Gap** (unchanged) |

## 3. Shape changes

- **`ScanCoverage` gains `notes: []`** (informational strings, ordinal order; omitted when empty — fixture inputs stay valid without it). `status` derives from `gaps` ONLY: notes never affect it.
- **`PlanRepo` gains `scanNotes: []`** (optional, emitted only when non-empty): the repo's coverage notes ride into the plan so the report can show them verbatim. Added AFTER `confidence` in canonical order. No reasons-template changes — notes are a distinct channel.
- **Report:** a `### Scan notes` subsection under `Uncertainty` (after `Findings`), one bullet per repo WITH notes: `- **{repo}**: {note1}; {note2}` — repos ordinal, notes joined `; `. Compile health is visible, not buried, not blocking.
- **Planner logic unchanged otherwise:** classification, waves, apply, push all read `status` as today — they simply see `complete` more often because compile noise no longer pollutes it.

## 4. Golden impact (fidelity-logged, exemplar-first)

| Fixture | Change | Why |
|---|---|---|
| F9 (svc) | `svc coverage` plan gap detail changes (first remaining gap is now the lockfile-group/constraint gap, not a compiler diagnostic); svc gains `scanNotes` (compiler/workspace/buildStatus/analysisLevel lines → notes); report gains the Scan notes section | compile noise → notes |
| F12 (billing/shipping) | their coverage flips `gaps` → `complete` (compile noise was their ONLY gap source) ⇒ the SPEC-012 `…(lockfile-evidenced)` coverage gaps DISAPPEAR from the plan; billing/shipping gain scanNotes | same |
| F13 (svc) | same svc coverage change as F9: `svc coverage` gap detail changes + scanNotes; OBSERVED-b reason text unchanged (svc still has real gaps: constraint-not-evidenced) | same |
| F10/F11/F14/F15 + all others | **untouched** (fixture-declared coverage: F11's `resolved version not evidenced` is package-relevant and stays a gap) | — |

Expected GOLDEN-CHANGES: F9, F12, F13 (plan + report). Hand-updated exemplar FIRST, then implementation diff; regeneration forbidden without a recorded reason.

## 5. Acceptance criteria

1. Classifier table machine-checked: every row of §2 is a selftest case (structural kind → Gap/Note), including the conservative default AND both reconciliation rules (message-only manifest echo → Note; unmatched knownGaps entry → Gap).
1a. **Committed-scan acceptance:** billing and shipping reach `complete` (their knownGaps carry only the compiler-diagnostic echo that joins a Note fact) — asserted in the real-scan selftest case, not just fixture goldens.
2. F9/F12/F13 goldens updated per §4 with reviewed GOLDEN-CHANGES; F12 asserts billing/shipping `complete` + zero lockfile-evidenced coverage gaps in the plan + scanNotes present; F9/F13 assert the remaining gap cites a package-relevant problem and scanNotes carry the compiler line.
3. Real-scan check: `scans/svc` and `scans/billing`+`scans/shipping` ingested fresh — svc keeps `gaps` (constraint/lockfile-group), billing/shipping reach `complete`, both carry notes (selftest, committed scans).
4. Report renders `### Scan notes` only when some repo has notes; existing fixtures' reports otherwise byte-identical (F10 etc. untouched).
5. Permutation + canonical: `notes`/`scanNotes` omitted-when-empty; byte-determinism holds; validator accepts the new optional arrays.
6. Planner behavior diff confined to coverage-driven paths: classification/wave/prereq logic untouched (code-review-verified; no template changes).
7. Existing 89 cases otherwise green.

## 6. Out of scope

- Changing WHAT tracemap emits (this spec reclassifies at ingest; tracemap's diagnostics remain as-is).
- Surfacing notes in `ua apply`/`ua push` outputs (they are scan health, not edit-site facts).
- Semantic-tier honesty beyond analysisLevel-as-note (V0 never consumes semantic evidence).
- Manifest `knownGaps` introspection (stays conservative; revisit if tracemap ever structures them).

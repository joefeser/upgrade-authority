# SPEC-021 — GOLDEN-CHANGES (adjudicated BEFORE capture, house rule)

**Date:** 2026-10-10 · **Branch:** `feature/pkg-case`

## The pre-implementation sweep (acceptance 6)

The corpus was swept for case-variant package spellings BEFORE any code change — every `packageId` /
`packageName` / `package` / delta-target string in `fixtures/*/input/*.json` was folded and grouped;
**zero case variants exist in the corpus** (independently re-verified by the round-1 apply/e2e
reviewer). A delta-vs-estate cross-check per fixture also found zero variant pairs. Pure widening
was therefore the expected outcome.

## The byte-verification run (after implementation)

`ua selftest` compares EVERY golden byte-exactly — plan, report, drift, registry, **apply.v1,
patches, push dry-run** included — and passed **139/139 before the new cases were added**: every
pre-existing golden is byte-identical under the new matching. The two mid-implementation selftest
failures (F8/F-gate) were a keying bug in the change itself (`_pubStatus` lookups not folding with
their now-folded insert keys), NOT golden churn — fixed before anything was captured.

## New goldens captured (adjudicated)

| Fixture | Golden | Adjudication |
|---|---|---|
| `F-case` (new fixture) | `golden/plan.json` | Both case-variant repos `affected` (rule-b), one conditional wave (producer unknown ⇒ T8 names caseA — ordinal-first, see below), unknown-producer gap; the delta echo keeps `NEWTONSOFT.JSON`. New fixture — no prior bytes. |
| `F-case` | `golden/report.md` | Rendering of the above; new fixture. |
| `F-case` | `golden/apply.v1.json` | Both edit sites derived from a third-spelling delta; each edit carries its OWN evidenced `packageId` (the SPEC-021 conditional emission — new field, emitted only when ≠ delta.packageName). New fixture. |
| `F-case` | `golden/patches/{caseA,caseB}/wave-1.patch` | Real-CLI per-repo runs against the committed `testdata-ingest/tracemap-rich/sources/case{A,B}/` checkouts; each patch edits its own spelling, case preserved, `Serilog` untouched. New fixture. |

## Drive-by determinism fix (pre-existing bug EXPOSED by F-case — not golden churn)

F-case's permutation case caught a pre-existing planner bug: the producer-unknown wave condition
(T8) and the unpublished-dep condition (T7) named `g.First()` from an unordered group — the named
repo could depend on input FILE ORDER (no existing fixture had 2+ symmetric same-wave repos with
producerUnknown, so the corpus never caught it). Fixed to ordinal-first
(`Planner.cs`, wave-condition block). No existing golden changed (verified: all 139 pre-existing
cases byte-identical in the same run).

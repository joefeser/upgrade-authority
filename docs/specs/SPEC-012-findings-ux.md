# SPEC-012 — Findings UX: from-version labeling, one trailer per repo, rule-(c) coverage gaps

**Status:** draft — round 1 review (brief-019)
**Author:** ZCode (coordinator)
**Date:** 2026-10-03
**Depends on:** SPEC-007 (findings, T10/T10a) · SPEC-003 (gaps) · Kiro PR-#8 advice #1/#5 + Qodo PR-#11 re-flag

## 1. Purpose

Three report-readability changes at estate scale, all reviewer-driven:

1. **Findings say which side is the delta's from-version** (Kiro #1): the delta says `12.0.3 → 13.0.3`; today's finding lists `12.0.3 … ; 13.0.1 …` neutrally, leaving the reader to work out that only Worker is on the version being upgraded *from*. A reader deciding what to change needs that call-out.
2. **The unsupported-groups trailer appears once per repo** (Kiro #5): at estate scale × N packages, repeating the full T10a sentence on every finding of the same repo is noise.
3. **Coverage gaps reach `plan.gaps` for lockfile-evidenced (rule-c) repos** (Qodo, flagged on PR #11 and earlier): today only direct-fact (rule-b) repos get a `{repo} coverage` gap; a repo affected purely via its lockfile can carry FailedOrPartial scans whose gaps never surface in the plan — visible on F12 (billing/shipping) and F9.

**Declared moot:** Kiro #4 (unit-line "edit site not evidenced" note) — `ua apply`/`ua push` now own edit-site evidence end-to-end; the report points at apply, not at half-information.

## 2. Changes

### 2.1 From-version marking in T10 resolutions (template revision)

**Scope: findings for the delta package only** — a disagreement on an unrelated package whose version coincidentally equals `oldVersion` is NOT about the delta and stays unmarked. Within a qualifying finding, each resolution entry that equals the delta's `oldVersion` (numeric-core equality, as apply compares) is suffixed ` [delta from-version]`:

> `svc resolves Newtonsoft.Json to 2 versions across its lockfile/TFM resolution groups: 13.0.1 in src/Api/packages.lock.json (net8.0); 12.0.3 in src/Worker/packages.lock.json (net8.0) [delta from-version] — evidenced disagreement, …`

- Pure function of evidence already in the plan (finding resolutions + delta.oldVersion); exact numeric-core equality (the same comparison apply uses — prerelease oldVersions never match).
- A repo with NO resolution on the from-version gets no marker (e.g. both projects already drifted past it — the neutral list stays neutral; no new sentence invented).

### 2.2 T10a once per repo

The unsupported-groups trailer attaches ONLY to the subject-first finding of each repo (the array is subject-sorted, so "first" is deterministic); subsequent findings of the same repo carry no trailer.

### 2.3 Rule-(c) coverage gaps

`BuildGaps`'s `{repo} coverage` emission extends: it fires for direct-fact (rule-b) repos as today, and additionally for repos affected via lockfile evidence (rule c) with coverage status `gaps`. The rule-c variant uses its own detail text (new gap template):

- **G2** `{repo} coverage (lockfile-evidenced)`: `scan gaps on {gap} — {repo} is affected via lockfile-evidenced transitive exposure; the gaps qualify how much additional work is unknown, they do not erase the evidenced exposure`

(The rule-b text is unchanged; subject naming keeps the two distinguishable in `gaps[]`.)

## 3. Golden impact (fidelity-logged)

- **F9**: ONLY the Newtonsoft.Json finding changes (its 12.0.3 resolution gains `[delta from-version]`; delta is 12.0.3→13.0.3) — the Contoso.Core finding (1.0.0/0.9.0) is untouched. The trailer drops from the second finding; a `svc coverage (lockfile-evidenced)` gap appears.
- **F11**: R11 is rule-c with coverage `gaps` (`resolved version not evidenced: Bogus.Null`) ⇒ an `R11 coverage (lockfile-evidenced)` gap appears (delta oldVersion 1.0.0 matches the LibA resolution ⇒ that finding also gains the marker).
- **F12**: billing + shipping each gain a `… (lockfile-evidenced)` gap (their scans are FailedOrPartial); no findings changes (delta targets Newtonsoft.Json, whose cross-repo difference is never a finding).
- **F13**: the Contoso.Core finding's Api resolution gains `[delta from-version]` (delta oldVersion 1.0.0) — plan, report, AND the push dry-run golden carry that text.
- **F14/F15**: no change (rule-b or complete coverage; findings none).
- Expected GOLDEN-CHANGES entries: F9, F11, F12, F13 — hand-updated exemplars first per the fidelity rule; regeneration forbidden without a recorded reason.
- apply.v1/push.v1 findings rendering (PR bodies) inherits the same T10 text via the shared plan — the push dry-run golden for F13 re-captures with the reason recorded.

## 4. Acceptance criteria

1. F9/F11/F12/F13 goldens updated per §3 with GOLDEN-CHANGES entries reviewed in-round — F11's marker AND its G2 gap are individually asserted.
2. New selftest assertions: from-version marker present/absent correctly (F9 Newtonsoft.Json marked, Contoso.Core unmarked; F11 marked; a drifted-past case unmarked); trailer exactly once per repo (count occurrences); rule-c gap present for billing/shipping AND R11 (F11), absent for repos without gaps.
3. Validator: T10 matcher accepts the marked form (` [delta from-version]` inside a resolution) and still requires exactly-one template per finding; a repo with unsupported lockfile groups and ≥1 finding carries the trailer on **exactly one** finding (the subject-first) — zero trailers for a qualifying repo is a validator failure, not merely >1.
4. Zero behavior change to classification, waves, apply edits, or push mechanics — diff proof: plan.json deltas confined to `findings` and `gaps` arrays only (machine-checked per fixture in the selftest golden compare).

## 5. Out of scope

- Cross-repo comparison findings (SPEC-008 §8 stays); convergence recommendations; T10a redesign beyond once-per-repo; the report renderer's section layout (Findings stays after Gaps).

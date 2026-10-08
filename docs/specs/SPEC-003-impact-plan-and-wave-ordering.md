# SPEC-003 — Impact plan and wave ordering

**Status:** revision 3 — amendment: classification-`unknown` repos are visible in `repos[]` (2026-10-08 real-estate finding; previously silently dropped, producing a false `0 unknown` summary). Revision 2: round-1 consolidated fixes (quorum batch: Qodo 7 High + Codex 11 P1/2 P2; golden changes GC1–GC7 in `docs/reviews/pr3-round1/GOLDEN-CHANGES.md`)
**Author:** ZCode (coordinator)
**Date:** 2026-10-02
**Depends on:** SPEC-002 (fused graph) · SPEC-000 §2.4 (plan semantics) · fixture corpus (merged — normative, byte-exact)

## 1. Purpose

Define how a delta plus the fused graph becomes the `plan.v1` document: three-state classification, wave construction and ordering, prerequisites, conditions, typed stops — deterministically, so output is byte-reproducible against the canonical goldens.

## 1a. Input constraint (multi-change deltas)

V0 plans are **single-change**: a `package-delta.v1` with more than one entry in `changes[]` is rejected at ingest with a clear error naming the file and the count. Multi-change planning (combined subgraphs, merged waves) is a future spec. Acceptance: a 2-change delta input yields a typed rejection, never a partial plan.

## 2. Classification rules (three-state, per repo)

**Precedence clause:** an observed edge (direct reference, lockfile-evidenced path, producer claim) always establishes `affected` first. The `unknown` conditions below change **actionability and wave status only** — they never downgrade an affected classification (F3a R11 and F3b R7 are `affected` with conditional/provisional waves, not `unknown`).

**affected** when any holds (reasons are emitted in this rule order):
- (a) producer repo of the delta target;
- (b) repo with an observed direct reference to the delta target (PackageReference, packages.config, or CPM fact — F6 R8/RB, F7 R9);
- (c) repo with a lockfile-evidenced transitive path to the target (F4a R3; F5 R2);
- (d) **unit ripple:** repo consuming a package produced by an *affected* repo — the producer's unit republishes, so the consumer must rebuild/bump (F1 R3, F4b R3). Ripple is an *obligation* independent of content exposure; it chains transitively through producer/consumer edges.

**not-affected** ONLY with positive evidence: complete scan coverage AND the repo's own lockfile closure contains no path (direct or transitive) to any affected package (F1 RU). Absence of a matching reference alone is NEVER sufficient.

**unknown** (never overriding rule a–d) when the repo has NO observed path to the target and:
- its scan coverage has gaps (residual exposure unresolved) — note: a repo WITH an observed edge is `affected`; the gap becomes an uncertainty note (F2 RX, F6 RB);
- it is otherwise unreachable-but-unproven (e.g., references a package whose closure cannot be resolved and whose producer is not affected).

## 3. Wave construction, ordering, partitioning

1. **Repo dependency DAG:** consumer of a package produced by repo X ⇒ depends on X's release unit. Ripple chains (rule 2d) define the same edges.
2. **Cycle ⇒ typed stop:** `CYCLE_DETECTED`, `cyclePath` (repo→package→…→start, beginning at the delta-target producer, minItems 2), `waves: []`. No auto-bootstrap ever (F-cyc; resolution is a human decision, future spec). Rev 3 corollary: the stopped plan's `repos[]` still enumerates affected repos AND classification-unknown repos — a stop is not a license to hide unclassified state.
3. **Depth:** each scheduled unit's depth = 1 + max(depth of its dependency units) among scheduled units (roots = 1).
4. **Status:**
   - `ready` — the wave's prerequisites are *availability statements* (producer publishes, rebuilds, external-feed availability). Restore/feed checks are stated, not verified (F1, F4a/b, F5, F6, F7, F8 wave 2 — including F5's external-feed prerequisite: availability ≠ gating).
   - `conditional` — gated on something the plan cannot sequence: external team release (T6), publicationStatus `unpublished` for a needed package (T7), or unknown producer (T8).
   - `provisional` — downstream of a contradiction; `blockedOn: <C-id>`; no claim outranks another (F3b).
5. **Partitioning (deterministic layering):** waves are ordered by (status rank: ready=0, conditional=1, provisional=2; then depth). All units at the same (rank, depth) share one wave, sorted by repo name within it. (F8: R2 ready-depth-2 = wave 2; R10 conditional-depth-2 = wave 3. F6: R8+RB ready-depth-2 = one wave 2.)
6. **Unscheduled repos:** only repos whose **ownership is unknown** (RM in F2), **classification-`unknown` repos** (rev 3: gaps or unresolved exposure with no observed edge — visible in `repos[]` with their reason, never scheduled, never silently dropped), and `not-affected` repos appear in `repos[]` but never in waves. `actionType: "unknown"` repos ARE scheduled — into conditional/provisional waves (F3a R11, F3b all).
7. **Parallelism:** the (rank, depth) layering above is V0's only concurrency statement; anything deeper is future work.

## 4. Determinism contract (byte-exact goldens)

1. **Templates — the complete, exhaustive set.** Every prerequisite and condition string in every plan comes from this table by parameter substitution; nothing else is ever emitted.

| id | kind | template |
|---|---|---|
| T1 | prereq | `{producer} publishes {pkg} >= {version} (stated, not verified live)` |
| T4 | prereq | `{producer} publishes {pkg} rebuilt against {dep} {depVersion} (stated, not verified live)` |
| T5 | prereq | `{pkg} {version} available from the public feed ({pkg} declared external via producer-evidence.v0; stated, not verified live)` |
| T6 | condition | `external {team} releases {pkg} >= {version} (request/await — outside our change authority; escalation path is the coordination step, not a PR)` |
| T6a | prereq | `{pkg} >= {version} available and restorable from the consumer environment (stated, not verified live)` |
| T7 | condition | `awaiting publication of {pkg}: producer-evidence.v0 declares {pkg} publicationStatus = unpublished — {repo}'s wave can never be 'ready' while the sidecar says unpublished` |
| T7a | prereq | `{producer} publishes {pkg} (cite: producer-evidence.v0 {pkg} publicationStatus = unpublished; stated, not verified live)` |
| T8 | condition | `producer of {pkg} is unknown — no producer wave is derivable; {repo}'s wave proceeds only after the producer is identified and {pkg} {version} is available` |
| T8a | prereq | `{pkg} {version} exists on the configured feed and authenticated restore succeeds (stated, not verified live)` |
| T9 | prereq | `{cid} resolved: authoritative producer of {pkg} determined by human decision; then {pkg} {version} published (stated, not verified live)` |

   Selection: a wave's `prerequisites` = the availability statements for its gates, in fixed template order [T1, T4, T5, T6a, T7a, T8a, T9]. `condition` = the single gating statement (T6/T7/T8), or `blockedOn` for provisional. Wave 1 carries T5 only when the delta target is external (F5). T2/T3 (restore checks) do not exist — availability statements carry "stated, not verified live" (GC2). A conditional wave chained on an earlier external gate reuses T6 as its condition (F2 wave 2). **Enforcement:** `tools/validate-fixtures.mjs` maps every prerequisite/condition string in every golden to exactly one template (acceptance 9) — a string matching zero or multiple templates is a failure.

2. **Array ordering — every array (machine-enforced: `tools/canonicalize-goldens.mjs` sorts waves by index, releaseUnits by repo, prerequisites by template rank, contradictions by id, plus GC9/GC11 rules; the validator enforces `repos[]` wave-schedule order against the plan's own waves, since that order is semantic):**
   - `repos[]`: wave-schedule order — (status rank of first containing wave, wave index, repo name) — then unscheduled affected (`actionType: unknown`, ownership-known) by name, then ownership-unknown by name, then classification-unknown by name (rev 3), then `not-affected` by name.
   - `reasons[]`: classification-rule order (a, b, c, d), then qualifier notes (edge/gap/ownership) in that fixed order.
   - `evidenceKinds[]`: canonical order [package-evidence.v0, producer-evidence.v0, lockfile-rows.v0, ownership.v0, scan-coverage], deduplicated (GC9 — matches the corpus).
   - `prerequisites[]`: template-id order (§4.1 list). `waves[]`: by index. `releaseUnits` within a wave: repo name.
   - Unit `packages[]` and `publicationStatus` keys: **natural sort** (numeric-suffix aware, case-insensitive) — deterministic AND permutation-stable (GC11: declaration order would violate the permutation acceptance case).
   - `notes[]`: emission order. `contradictions[]`: by id. `claims[]`: repo name. `downstreamProvisional[]`: repo name. `gaps[]`: subject, lexicographic.
   - **Permutation acceptance:** shuffling the declaration order of any input file's records must produce identical output bytes (SPEC-005 harness case).

3. **Serialization:** canonical formatter (`tools/canonicalize-goldens.mjs` is normative): 2-space indent, fully expanded objects/arrays, key order per schema listing, LF endings, trailing newline, minimal escaping. The golden differ is byte-exact; only recorded-and-reviewed diffs are legal (fidelity rule).

## 5. Acceptance criteria (fixture-mapped)

1. All 12 fixtures: output byte-equal to `golden/plan.json` on the network-disabled runner; input-permutation case produces identical bytes.
2. A 2-change delta input is rejected at ingest with a typed error.
3. F-cyc: stop + empty waves; cyclePath exactly `R1, P2, R5, P11, R1`.
4. F3b: producer units share provisional wave 1 (`blockedOn: C1`); R7 in provisional wave 2; all three repos `actionType: unknown` yet scheduled.
5. F2: wave 1 conditional (T6); RM (ownership unknown) unscheduled; RX scheduled in wave 2 on its observed edge.
6. F8: wave 2 `ready` (T1); wave 3 conditional (T7) with T7a prerequisite citing the sidecar.
7. F5: wave 1 `ready` with T5 verbatim (external-feed availability does not gate).
8. F1 vs F4b: identical ripple treatment (R3 affected, scheduled); F4a vs F4b differ exactly in R3's path-evidence (reasons/evidenceKinds/gaps), not in classification.
9. Every prerequisite and condition string matches exactly one template in §4.1 (validator-enforced mapping).

## 6. Out of scope

Multi-change deltas (beyond typed rejection); parallel waves beyond layering; live publication/restore verification; cycle resolution; scanner ingestion; markdown report layout (SPEC-004 renders from `plan.json` under these same rules).

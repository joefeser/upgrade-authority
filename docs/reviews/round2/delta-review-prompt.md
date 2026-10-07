You are claude-local, the bounded fallback reviewer for the upgrade-authority spec review loop (round 2 of at most 3). Round 1 found 1 blocker (B1), 6 majors (M1-M6), 6 minors (m1-m6), and lane issues Q4/Q8/Q10 — all triaged as accepted in the TRIAGE doc below. The spec author applied fixes; both specs are now revision 2 (full text below).

Per the review doctrine (docs/specs/REVIEW-DOCTRINE.md): round 2 is a DELTA review — verify each round-1 finding is resolved by the revision-2 text. Do not re-litigate settled scope. New issues count only if blockers.

OUTPUT FORMAT (mandatory):
1. Verdict per spec: SPEC-000 = SHIP | FIX THEN SHIP | REWORK; SPEC-001 = same.
2. Per-finding resolution table: finding id -> RESOLVED | PARTIALLY RESOLVED | NOT RESOLVED (one line each, cite the spec section that resolves it).
3. Any OPEN blockers/majors (severity + why + minimal fix). If none, say "zero open blockers/majors".
Be adversarial about whether the fixes are real or cosmetic.

=== TRIAGE (round 1 dispositions) ===
# Round 1 triage — brief-001 / PR #1 (2026-10-02)

**Verdicts:** Kiro: SPEC-000 **FIX THEN SHIP**, SPEC-001 **FIX THEN SHIP** (1 blocker). Codex: 2× P1 (map to the blocker + M1). Qodo: 3 High + 6 Medium (all map below). Sourcery: rate-limited, no findings.
**Raw logs:** [raw-review-bodies.md](raw-review-bodies.md) · [raw-inline-comments.md](raw-inline-comments.md) — verbatim, unedited.
**Cross-reviewer convergence on the two biggest findings** (producer evidence, transitive paths) is itself signal: those were real.

| # | Finding (source) | Severity | Disposition | Fix |
|---|---|---|---|---|
| B1 | Producer-evidence rung 4 has no source in tracemap (Kiro blocker; Codex P1-1; Qodo #1) | **Blocker** | Accepted | SPEC-001 §2: new input `producer-evidence.v0` sidecar (fixture-declared facts, `source: fixture-declared`, treated as declared-source rung). Rungs 1–3 removed from V0 (moved to future rungs). **B1(b)** — add `PackageProduced` facts to tracemap — recommended to Joe as parallel work in his repo (his call; does not block V0). |
| M1 | Transitive paths unreconstructable from declared inputs; lockfile `names` null >256 chars (Kiro M1; Codex P1-2; Qodo #2) | Major | Accepted | SPEC-001 §2: `packages.lock.json` rows (direct/transitive/unknown + 256-char caveat). SPEC-000: F4 split into **F4a** (lockfile present → path reconstructed) / **F4b** (absent → `Unknown` + gap, never "excluded"). |
| M2 | "Excluded" invites silent resolution via no-match (Kiro M2) | Major | Accepted | Three-state inclusion: `affected \| not-affected (positive evidence) \| unknown`. SPEC-000 §2.4 + acceptance 2. |
| M3 | Contradiction recorded but wave still firm; "outrank" = silent resolution (Kiro M3; Qodo #3) | Major | Accepted | SPEC-001 §4/§5: precedence affects confidence display ONLY; downstream of contradicted producer → provisional wave `blocked-on: <contradiction id>`. Corroboration = independent sources (distinct repo+commit+rule). |
| M4 | F2 ownership has no input source; outside-team step looks executable (Kiro M4; Qodo #9) | Major | Accepted | SPEC-001 §2: `ownership.v0` input (repo→team + self-team id; missing → `Unknown`, never "ours"). SPEC-000 F2: R4 step = `external — request/await`, never executable. |
| M5 | Fixtures are prose; fidelity rule has nothing to hold (Kiro M5) | Major | Accepted | SPEC-000 §4: every fixture = input files + **golden `plan.json`** (SPEC-005 piece pulled forward); F1 edges fully named. |
| M6 | Missing demonstrated shapes (Kiro M6) | Major | Accepted | SPEC-000 §4: **F5** third-party-via-internal (most common real alert), **F6** packages.config, **F7** CPM + VersionOverride, **F8** partial publication → conditional next wave, **cycle** detect-and-refuse (full handling stays SPEC-003). |
| m1 | ReleaseUnit false prerequisite (Kiro m1; Qodo #7) | Minor | Accepted | `basis: assumed-same-repo \| evidenced` in JSON (not prose) + **F1b** independent-versioning fixture. |
| m2 | PackageIdentity conflates id and version (Kiro m2; Qodo #6) | Minor | Accepted | Split `PackageId` / `PackageVersion?` — unknown version still forms an edge. |
| m3 | Repo identity normalization undefined (Kiro m3) | Minor | Accepted | SPEC-001 §3: normalization rules (case, `.git` suffix, mirrors). |
| m4 | "On Joe's work machine" not agent-repeatable (Kiro m4) | Minor | Accepted | SPEC-000 §5: network-disabled Windows CI run (e.g. `windows-latest`, network blocked) + Joe hands-on as final sign-off. |
| m5 | Pinned schema (Kiro m5) | Minor | Accepted + verified | Coordinator verified against local tracemap checkout: `package-delta.v1` = `{version, sourceRepo, sourceCommitSha, changes[{id, packageName, ecosystem, changeType, oldVersion, newVersion}]}` per `samples/package-deltas/package-delta.example.json`. Recorded in SPEC-001 §2. |
| m6 | Doctrine "zero NEW" can close with open majors (Kiro m6; Qodo #5 High) | Minor (doctrine) | Accepted | REVIEW-DOCTRINE §2: "zero **open** blockers/majors"; delta rounds must verify carried blockers/majors resolved. |
| Q4 | Lane: required-quorum vs silence-=-proceed conflict (Qodo #4 High) | Lane config | Accepted | Lane: `minimumReturned: 1`, `preferAllReturned: true`; doctrine's 1-working-day silence rule governs owner-ferried reviewers; description updated. |
| Q10 | Lane: fallback can run past round 3 (Qodo #10) | Lane config | Accepted | Lane: fallback explicitly bounded WITHIN the 3-round budget (substitute reviewer for remaining cycles); after ceiling → typed human-decision stop, `postTerminalComment` retained. |
| Q8 | Waves omit publication-availability statements (Qodo #8) | Medium | Accepted | Folded into M6/F8: report states publication/restore prerequisites explicitly, claims no live verification. |

**Not accepted / deferred:** none rejected. B1(b) (tracemap `PackageProduced` slice) is recommended-but-deferred to Joe — it is work in his other repo.

**Round 2 scope (delta review):** verify B1, M1–M6, m1–m6, Q4, Q10 fixes; no new full re-review per doctrine §1.

=== SPEC-000 revision 2 ===
# SPEC-000 — V0 definition and scope

**Status:** round 1 fixes applied (TRIAGE 2026-10-02: B1, M1–M6, m1–m6, Q8) — awaiting round 2 delta review
**Author:** ZCode (coordinator)
**Date:** 2026-10-02 · **Revision:** 2
**Decides:** what V0 is and is not. Everything here traces to logged planning decisions (docs/planning, esp. Sessions 08, 11, 12) and round-1 review (docs/reviews/round1/TRIAGE.md).

## 1. V0 goal

Prove the core wedge end-to-end, on sanitized examples, within days: **given evidence for a repo estate, produce a trustworthy impact plan** — who produces what, who is affected, in what order to update, with uncertainty visible instead of invented.

V0 is a validation slice, not a product. Success = Joe touches real output this week and we learn what's wrong early.

## 2. In scope (V0)

1. **Ingest** four local inputs (schemas pinned in SPEC-001 §2; no network, no corporate systems): (a) tracemap package-reference evidence (`PackageReferenced`/packages.config facts), (b) a `package-delta.v1` file describing the proposed change, (c) a **`producer-evidence.v0` sidecar** declaring producer facts for the sanitized estate (source: `fixture-declared`), and (d) an **`ownership.v0` sidecar** (repo → team, plus a self-team id).
2. **Producer map:** repo → package(s) from the producer sidecar (V0's only producer rung), preserving confidence, contradictions, and gaps. No naming heuristics; YAML-sniffing explicitly out.
3. **Consumer graph:** repo/package edges with declared constraint, resolved version and direct/transitive path **when a `packages.lock.json` is present in evidence**; otherwise transitive exposure is `Unknown` with a gap (never silently "excluded").
4. **Impact plan:** for a package delta, classify each repo in the estate as **`affected` | `not-affected` (only with positive evidence: complete scan coverage for that repo) | `unknown`** — three-state, never binary — and produce an ordered update sequence treating a multi-package repo as one unit by default, with the unit's `basis` field carrying `assumed-same-repo | evidenced`. Outside-team repos appear as `external — request/await` coordination steps, never executable steps. Waves state publication/restore prerequisites explicitly and claim **no live verification** of them.
5. **Outputs:** human-readable Markdown report + machine-readable JSON plan (`plan.json`) — the JSON is the Phase-2 hand-off artifact and the golden-fixture comparison target.
6. **CLI, local-first, Windows.** Stack shape in SPEC-004.

## 3. Out of scope (V0) — visible on roadmap, not built

PR generation; merges/CI integration; live Artifactory/JFrog access (mock-JFrog simulator is SPEC-006+, per D020); scanner alert ingestion; compatibility probes/container evidence; npm/pip; multi-team/org features; accountability records (governance track, D008); MSBuild evaluation and build-info ingestion (future producer rungs, SPEC-001 §4a); cycle *resolution* (V0 detects and refuses — see F-cyc).

## 4. Sanitized corpus (acceptance fixtures)

**Every fixture is concrete: input files + a golden expected `plan.json`, committed before implementation** (fidelity rule §7.1 — the SPEC-005 harness piece is pulled forward into fixture authoring; a fixture without a golden file is not a fixture). Substituting or weakening any golden expectation requires a recorded reason and review.

- **F1 — eight-package producer (edges fully named):** repo `R1` produces `P1..P8` (IDs unrelated to repo name). Integration repo `R2` consumes `P1`, `P3` and publishes package `I1`. Service repo `R3` consumes `I1` + external package `E1`. Golden: `R1` = one release unit (`basis: assumed-same-repo`), wave order R1 → R2 → R3 with publication prerequisites stated between waves.
- **F1b — independent versioning inside one repo:** `R1` additionally contains a second, independently-versioned package set (`P9`, `P10`, different version scheme, evidenced in the sidecar). Golden: still one unit, but the assumption is flagged per-unit and the JSON shows `basis: assumed-same-repo` with the contradiction-adjacent note that versioning evidence differs within the unit.
- **F2 — outside-team producer:** `E1` produced by repo `R4` owned by another team per `ownership.v0`. Golden: `R4` appears as `external — request/await`; no executable step is emitted for `R4`; ownership of any repo missing from `ownership.v0` is `Unknown`, never "ours".
- **F3a — ambiguous evidence:** a package with no producer evidence. Golden: explicit gap, zero invented producers, plan lists it under `unknown`.
- **F3b — conflicting evidence:** two repos' sidecar facts both claim production of `P5`. Golden: both claims retained, contradiction recorded; every repo downstream of `P5` appears only in a **provisional wave** marked `blocked-on: <contradiction id>`.
- **F4a — transitive path with lockfile:** service `R3` has `I1` (direct) and reaches `P2` transitively via `I1`; `R2`'s checked-in `packages.lock.json` (in evidence) carries the `direct`/`transitive` rows. Golden: the full path `R3 → I1 → P2` reconstructed and shown.
- **F4b — transitive exposure without lockfile:** same shape, no lockfile in evidence. Golden: transitive exposure = `Unknown` + gap; `R3` classified `unknown`, **not** `not-affected`. (Lockfile `names` field may be null when the joined string exceeds 256 chars — treated as `unknown` child detail, never as absence of dependency.)
- **F5 — third-party package via internal package:** Dependabot-style delta on public package `N1` pulled transitively through internal `P1`. Golden: wave 1 bumps `R1` (producer of `P1`), consumers follow; the report attributes the origin to the public advisory surface without claiming scanner ingestion.
- **F6 — packages.config legacy repo:** one repo consumes `P1` via packages.config facts. Golden: ingested, edge present, `not-affected` only with complete coverage evidence.
- **F7 — central package management:** repo uses `Directory.Packages.props` + one `VersionOverride`. Golden: the fan-out (one pin edit affects multiple projects) and the override are both visible in the plan's constraint-source data.
- **F8 — partial publication:** sidecar/ownership evidence shows only `P1..P4` of `R1`'s eight packages published (simulated). Golden: consumer wave depending on `P6` is emitted as `conditional (awaiting publication)`, never `ready`.
- **F-cyc — dependency cycle:** `R5` consumes `P2` while `R1` consumes `R5`'s output. Golden: V0 **detects and refuses** (typed stop with the cycle path printed); no order is emitted; resolution is SPEC-003 work.

More cases from Joe's demonstrated set are added the same way (inputs + golden), each an explicit acceptance check.

## 5. Acceptance criteria (V0 done when)

1. F1–F-cyc load and produce plans matching their golden `plan.json` (or a recorded, reviewed diff) on a **network-disabled Windows CI run** (e.g. `windows-latest` with network blocked) — Joe's hands-on run on the work machine remains the final sign-off.
2. Every plan is three-state per repo with inclusion/exclusion reasons; F2 orders correctly despite non-ownership; F3a emits zero invented producers; F3b yields provisional waves only.
3. F1 shows one unit for R1's packages with `basis` in the JSON (not prose).
4. `plan.json` validates against its schema; the Markdown report is readable without training.
5. Uncertainty is a first-class output field, never dropped.
6. Every `ProducerClaim` cites an `Evidence` whose kind appears in a pinned input schema; no claim derived from a project, assembly, or repo name.

## 6. Validation milestone

Runnable slice in Joe's hands by **Friday 2026-10-09**, targeting sooner. If reality disagrees with this spec, we fix the spec first (that is the point of the loop).

## 7. Traceability

- Scope basis: D003 (impact plan first), D004 (sanitized), D005 (modern+legacy: `PackageReferenced` + packages.config + CPM shapes all appear in fixtures), D017 (local, work machine), N3 (consume tracemap), Session 08 (evidence ladder, vocabulary), Session 12 §3a (differentiation: no producer conventions required).
- Round-1 review: docs/reviews/round1/TRIAGE.md — B1, M1–M6, m1–m6, Q8 all applied in this revision.

=== SPEC-001 revision 2 ===
# SPEC-001 — Evidence ingestion and graph model

**Status:** round 1 fixes applied (TRIAGE 2026-10-02: B1, M1–M4, m2–m5) — awaiting round 2 delta review
**Author:** ZCode (coordinator)
**Date:** 2026-10-02 · **Revision:** 2
**Depends on:** SPEC-000 (v0 definition)

## 1. Purpose

Define what evidence V0 consumes, how facts become graph entities, and how uncertainty is represented — so the impact plan is only ever as confident as its evidence.

## 2. Inputs (four, all local; schemas pinned)

1. **Tracemap indexed package evidence (consumer-side):** `PackageReferenced` facts and packages.config facts as emitted by tracemap scans (sources, commit SHAs, rule IDs, tiers, gaps). Tracemap emits **no producer-side facts today** (verified round 1: Kiro against tracemap `main`, coordinator against the local checkout) — consumer facts only.
2. **Tracemap `package-delta.v1`** — schema pinned (verified against `samples/package-deltas/package-delta.example.json` in the tracemap checkout):
   `{ version: "package-delta.v1", sourceRepo, sourceCommitSha, changes: [{ id, packageName, ecosystem, changeType, oldVersion, newVersion }] }`
3. **`producer-evidence.v0` sidecar (V0's ONLY producer rung).** Sanitized-estate declaration: `{ source: "fixture-declared", repo, packageId, producedVersion?, evidenceNote? }`. Facts from this input are honest about what they are: `fixture-declared`, confidence capped, and never presented as observed production. **Parallel recommendation (Joe's call, his repo):** add `PackageProduced` facts to tracemap from project properties/nuspec — fits tracemap's static-evidence charter and becomes the durable rung; does not block V0.
4. **`ownership.v0` sidecar:** `{ repo, team, selfTeamId }`. A repo absent from this input has ownership `Unknown` — never "ours".
5. **Optional per-repo `packages.lock.json` rows** (when present in evidence): carry `direct` / `transitive` / `unknown` dependency classification and child dependency names. Caveat pinned from tracemap source: the `names` field is **null when the joined string exceeds 256 chars** — such rows are `unknown` child detail, never evidence of absence.

Ingest rejects unknown schema versions with a clear error naming the pinned version.

## 3. Graph model (vocabulary per Session 08)

Entities (model, not database design):

- `Repository` — identity normalized before graph insertion: lowercase host where present, strip trailing `.git`, trailing slashes; mirror mappings declared in config. Un-normalized duplicates are a defect, not a merge.
- `PackageId` — package identity **without** version (producer claims are about ids).
- `PackageVersion` — an observed/declared version of a `PackageId`; nullable everywhere (a producer claim or consumer edge may carry an unknown version — unknown versions still form edges).
- `Evidence` — one observed fact: kind, source location, commit SHA, rule ID, observed-at. Immutable; never overwritten.
- `ProducerClaim` — evidence that repository R produces PackageId P (confidence derived from evidence kind + corroboration).
- `ConsumerEdge` — repo R references PackageId P: declared constraint, resolved version (if evidenced), TFM, direct/transitive path (only with lockfile evidence), constraint source location (e.g., `Directory.Packages.props`, `VersionOverride`, project file, packages.config).
- `ReleaseUnit` — set of packages that move together, carrying `basis: assumed-same-repo | evidenced`. V0 default: all packages produced by one repo = one unit, flagged `assumed-same-repo`.
- `Finding` — external condition (v0: only the package delta; scanners later).
- `ImpactPlan` — ordered waves + per-repo three-state classification (`affected | not-affected(positive evidence) | unknown`) + rationale + uncertainty list.

## 4. Evidence ladder (V0 reality)

V0 has exactly **one producer rung**: the `producer-evidence.v0` sidecar (`fixture-declared`). The ladder below records where future rungs slot in — they are **out of scope for V0** and listed here only so nobody stubs interfaces for them:

*(future, not V0)* 1. build/publish receipt (build-info, CI artifacts) → 2. produced package manifest (nuspec-in-index) → 3. evaluated MSBuild facts (MSBuild 17.8+ evaluation) → **4. declared source evidence (the only V0 rung; V0's instantiation is the fixture-declared sidecar; tracemap `PackageProduced` would also land here)**. Pipeline-YAML heuristics remain **excluded entirely**.

**Precedence affects confidence display only — never which claim survives.** Higher-rung evidence raises confidence; it never deletes or overrides a contradictory lower-rung claim. A failed/missing observation yields `Unknown`, never an inferred clean result.

**Corroboration count** = number of *independent* evidence sources (distinct repo + commit + rule). Multiple facts from the same file/source count once.

## 5. Uncertainty representation

Every graph node/edge carries: `evidence_kinds[]`, `confidence` (rung + corroboration), `contradictions[]` (pairs of disagreeing evidence), `gaps[]` (what was not observed). Plans surface all three verbatim. Downstream consumers of a **contradicted** producer appear only in **provisional waves** marked `blocked-on: <contradiction id>` — the contradiction, not an implicit preference, is what holds them back. F3a/F3b fixtures assert this.

## 6. Open consideration — HACP publication (Joe, 2026-10-02)

Joe suggested the evidence contract "may also want to be published in hacp." HACP (hacp.io) is a manual approved-loop evidence chain (Session 09). Open questions: does our evidence/plan schema benefit from HACP's evidence-approval representation vs. a plain signed record? Does not block V0; evaluate on the governance track (D008) before any external publication.

## 7. Acceptance criteria

1. F1–F-cyc fixtures (SPEC-000 §4) ingest to a graph; every entity traceable to at least one `Evidence`.
2. F3a: no producer invented; output contains an explicit ambiguity record.
3. F3b: both claims retained, contradiction listed, downstream in provisional waves only.
4. F4a/F4b: lockfile-evidenced path reconstructed; lockfile-less transitive exposure = `Unknown` + gap (never "excluded"); null-`names` rows treated as unknown detail.
5. Pinned schema versions (inputs 1–5) recorded; ingest rejects unknown versions with a clear error.
6. Round-trip: graph serializes; `plan.json` validates against schema.
7. Every `ProducerClaim` cites evidence of a kind in a pinned schema; nothing derived from project/assembly/repo names.
8. Repo identity: two spellings of the same repo (case, `.git`) collapse to one `Repository`.

## 8. Out of scope

Live feeds; MSBuild evaluation execution; build-info ingestion; tracemap `PackageProduced` emission (recommended separately); scanner findings; HACP integration; cycle resolution (detection + typed refusal is V0; resolution is SPEC-003).

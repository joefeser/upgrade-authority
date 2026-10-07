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

PR generation; merges/CI integration; live private-feed/JFrog access (mock-feed simulator is SPEC-006+, per D020); scanner alert ingestion; compatibility probes/container evidence; npm/pip; multi-team/org features; accountability records (governance track, D008); MSBuild evaluation and build-info ingestion (future producer rungs, SPEC-001 §4); cycle *resolution* (V0 detects and refuses — see F-cyc).

## 4. Sanitized corpus (acceptance fixtures)

**Every fixture is concrete: input files + a golden expected `plan.json`, committed before implementation** (fidelity rule, `docs/planning/12-coordinator-synthesis.md` §7.1 — the SPEC-005 harness piece is pulled forward into fixture authoring; a fixture without a golden file is not a fixture). Substituting or weakening any golden expectation requires a recorded reason and review.

- **F1 — eight-package producer (edges fully named):** repo `R1` produces `P1..P8` (IDs unrelated to repo name). Integration repo `R2` consumes `P1`, `P3` and publishes package `I1`. Service repo `R3` consumes `I1` + external package `E1`. Golden: `R1` = one release unit (`basis: assumed-same-repo`), wave order R1 → R2 → R3 with publication prerequisites stated between waves.
- **F1b — independent versioning inside one repo:** `R1` additionally contains a second, independently-versioned package set (`P9`, `P10`, different version scheme, evidenced in the sidecar). Golden: still one unit, but the assumption is flagged per-unit and the JSON shows `basis: assumed-same-repo` with the contradiction-adjacent note that versioning evidence differs within the unit.
- **F2 — outside-team producer:** `E1` produced by repo `R4` owned by another team per `ownership.v0`. Golden: `R4` appears as `external — request/await`; no executable step is emitted for `R4`; ownership of any repo missing from `ownership.v0` is `Unknown`, never "ours".
- **F3a — ambiguous evidence:** a package with no producer evidence. Golden: explicit gap, zero invented producers, plan lists it under `unknown`.
- **F3b — conflicting evidence:** two repos' sidecar facts both claim production of `P5`. Golden: both claims retained, contradiction recorded; every repo downstream of `P5` appears only in a **provisional wave** marked `blocked-on: <contradiction id>`.
- **F4a — transitive path with lockfile:** service `R3` has `I1` (direct) and reaches `P2` transitively via `I1`; **`R3`'s checked-in `packages.lock.json`** (in evidence) carries the `direct`/`transitive` rows. Golden: the full path `R3 → I1 → P2` reconstructed and shown.
- **F4b — transitive exposure without lockfile:** same shape, no lockfile in evidence. Golden: transitive exposure = `Unknown` + gap; `R3` classified `unknown`, **not** `not-affected`. (Lockfile `names` field may be null when the joined string exceeds 256 chars — treated as `unknown` child detail, never as absence of dependency.)
- **F5 — third-party package via internal package:** public package `N1` pulled transitively through internal `P1`. Golden: wave 1 bumps `R1` (producer of `P1`), consumers follow; the plan notes N1 has no private producer (gap; public-package attribution derived from producer absence) without claiming scanner ingestion or asserting an advisory origin no input evidences.
- **F6 — packages.config legacy repo:** one repo consumes `P1` via packages.config facts. Golden: ingested, edge present, `not-affected` only with complete coverage evidence.
- **F7 — central package management:** repo uses `Directory.Packages.props` + one `VersionOverride`. Golden: the fan-out (one pin edit affects multiple projects) and the override are both visible in the plan's constraint-source data.
- **F8 — partial publication:** the `producer-evidence.v0` sidecar carries an explicit per-package `publicationStatus` fact: `P1..P4 = published`, `P5..P8 = unpublished` (fixture-declared). Golden: the consumer wave depending on `P6` is emitted as `conditional (awaiting publication)` **citing that sidecar fact**, never `ready`; packages without a publication fact are `unknown`, never assumed published.
- **F-cyc — dependency cycle:** `R5` consumes `P2` while `R1` consumes `R5`'s output. Golden: V0 **detects and refuses** (typed stop with the cycle path printed); no order is emitted; resolution is SPEC-003 work.

More cases from Joe's demonstrated set are added the same way (inputs + golden), each an explicit acceptance check.

## 4a. Known issues (logged minors — fixed inline, never buying a review round)

- *(none currently)*

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

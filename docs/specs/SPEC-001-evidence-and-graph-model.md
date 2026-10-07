# SPEC-001 — Evidence ingestion and graph model

**Status:** round 1 fixes applied (TRIAGE 2026-10-02: B1, M1–M4, m2–m5) — awaiting round 2 delta review
**Author:** ZCode (coordinator)
**Date:** 2026-10-02 · **Revision:** 2
**Depends on:** SPEC-000 (v0 definition)

## 1. Purpose

Define what evidence V0 consumes, how facts become graph entities, and how uncertainty is represented — so the impact plan is only ever as confident as its evidence.

## 2. Inputs (four, all local; schemas pinned)

1. **Tracemap indexed package evidence (consumer-side):** `PackageReferenced` facts and packages.config facts as emitted by tracemap scans (sources, commit SHAs, rule IDs, tiers, gaps). Tracemap emits **no producer-side facts today** (verified round 1: Kiro against tracemap `main`, coordinator against the local checkout) — consumer facts only.
2. **Tracemap `package-delta.v1`** — schema pinned and verified against tracemap `main @ 32ab4c3` (Kiro, round 1; coordinator re-verified against the local checkout's `samples/package-deltas/package-delta.example.json`):
   `{ version: "package-delta.v1", sourceRepo, sourceCommitSha, changes: [{ id, packageName, ecosystem, changeType, oldVersion, newVersion }] }`
3. **`producer-evidence.v0` sidecar (V0's ONLY producer rung).** Sanitized-estate declaration: `{ source: "fixture-declared", repo, packageId, producedVersion?, publicationStatus?: published | unpublished, evidenceNote? }`. `publicationStatus` is itself fixture-declared in V0 (a future rung replaces it with private-feed build-info/publish receipts); a package with **no** publication fact is `unknown` — never assumed published. Facts from this input are honest about what they are: `fixture-declared`, confidence capped, and never presented as observed production. **Parallel recommendation (Joe's call, his repo):** add `PackageProduced` facts to tracemap from project properties/nuspec — fits tracemap's static-evidence charter and becomes the durable rung; does not block V0.
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

**Two fusion rules added from fixture review (2026-10-02):** (1) an *observed* consumer edge is positive evidence of affected-ness — scan-coverage gaps qualify how much additional work is unknown, they never downgrade an observed edge to `unknown` or de-schedule an observed consumer. (2) A producer repo with complete scan coverage and no reference to the delta target does **not** thereby evidence its packages' independence from the target — package-manifest edges are a future rung; consumer-side classification of its packages follows lockfile/manifest evidence only.

**Corroboration count** = number of *independent* evidence sources (distinct repo + commit + rule). Multiple facts from the same file/source count once. Confidence in `plan.v1` is emitted as `{ rung, corroboration }` per repo classification.

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

## 9. Known issues (logged minors — fixed inline, never buying a review round)

- **Tracemap consumer-fact export schema not yet pinned to a real artifact (2026-10-02, fixture corpus authoring).** Fixtures use the sanitized `package-evidence.v0` shape (`docs/schemas/input-schemas.md`). Before the runner consumes real tracemap output, pin the real export schema and add a converter (or honesty markers), same discipline as `package-delta.v1`. Related known limitation: this machine's `claude` CLI 401s headless (lane fallback degraded) and `kiro agent` is broken (missing kiro-tunnel) — tooling notes in Session 12.

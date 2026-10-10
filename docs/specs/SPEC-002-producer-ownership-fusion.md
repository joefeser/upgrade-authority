# SPEC-002 — Producer map and ownership fusion

**Status:** draft — round 1 review (brief-003)
**Author:** ZCode (coordinator)
**Date:** 2026-10-02
**Depends on:**
> **Amended by SPEC-021 (2026-10-10):** package identity for producer claims, external declarations, and contradiction detection is OrdinalIgnoreCase (NuGet ids); ONE sidecar claiming the same (repo, packageId) in two spellings is a typed load error. Repo identity unchanged. SPEC-001 (inputs, graph model, evidence rules) · fixture corpus (merged, PR #2 — normative)

## 1. Purpose

Define exactly how the pinned V0 inputs become graph entities — producer claims, ownership classification, release units, publication status, contradictions — before any ordering happens (SPEC-003). The merged fixture corpus is normative: if this spec and a golden disagree, this spec is wrong.

## 2. Fusion rules

1. **Repo identity:** normalize before insertion (lowercase host where present, strip trailing `.git` and slashes, apply declared mirror mappings). Two spellings of one repo are one `Repository`; un-normalized duplicates are a defect.
2. **ProducerClaim construction — only from `producer-evidence.v0` `producers[]`** (the single V0 rung, `fixture-declared`). One claim per `(repo, packageId)` pair. Repeated identical entries within one sidecar are the SAME source: corroboration stays 1. Confidence is emitted as `{ rung: "fixture-declared", corroboration: n }` with n = count of *independent* sources (V0: 1 unless another input kind corroborates).
3. **Contradiction detection:** ≥2 distinct repos claiming the same `packageId` ⇒ contradiction `C-n` (deterministic id: `C1`, `C2`, … in sorted `(packageId, repo)` encounter order). Both claims retained forever; no precedence (SPEC-001 §4); downstream consequences are SPEC-003's provisional waves.
4. **External packages and gap scoping:** `externalPackages[]` declares a package external — attribution is evidence-backed, expected producer is none. Producer absence for a package NOT in `externalPackages` means unknown producer — never "public", never a guess (F3a vs F5 rule). **Unknown-producer gaps are recorded ONLY when they gate the plan:** the delta target's producer (F3a) or a producer required for wave construction. Unrelated referenced packages with unknown producers (F1's X9 via RU, E1 via R3) produce NO plan-level gap — their producers gate nothing (GC3).
5. **Ownership:** `ownership.v0` ⇒ `self` (team = selfTeamId) / `external` (any other team) / `unknown` (repo absent from the file). Never "ours" by default. `external` producers render as `external-request-await` coordination, never executable steps (schema-enforced pairing).
6. **Publication status:** per package from `producers[].publicationStatus`; absent ⇒ `unknown` (never assumed published). Attached to the producing repo's release unit.
7. **ReleaseUnit:** group ALL packages produced by one repo into ONE unit, `basis: "assumed-same-repo"` in V0 always (no shared-release evidence exists in any V0 input). When versioning evidence differs within the unit (e.g., P9/P10 on an independent line), the unit carries a note AND the corpus records a gap (F1b). Unit `packages` list order = producer-sidecar declaration order.
8. **Consumer edges:** one edge per `package-evidence.v0` fact (`direct`, with constraint/format/constraintSource/tfm/projects/path/sha). `lockfile-rows.v0` adds `transitive` edges (`via` parent) for that repo — only for that repo, never lent to others (F4a: R3's lockfile evidences R3's path only). `names: null` rows ⇒ unknown child detail, never absence. No lockfile + reference to a package whose producer is affected ⇒ transitive exposure `unknown` (a gap, never `not-affected`).
9. **Observed-edge rule (algorithmic):** an observed consumer edge is positive evidence of affected-ness. Coverage gaps add an uncertainty note about *additional* unknown work; they never downgrade classification, de-schedule a repo, or erase an edge (F2 RX, F6 RB).
10. **Zero-refs rule (algorithmic):** a producer repo with complete coverage and no reference to the delta target does NOT evidence its packages' independence from that target — package-manifest edges are a future rung (F4b gap).

## 3. What fusion outputs

The graph handed to SPEC-003: repositories (normalized), producer claims (+contradictions), external-package declarations, ownership map, release units (+publication status), consumer edges (direct/transitive/unknown-exposure), scan-coverage map, and the accumulated `gaps[]`.

## 4. Acceptance criteria (fixture-mapped)

1. F3a: zero producers invented; one unknown-producer gap. F3b: contradiction C1 with both claims; no precedence applied anywhere.
2. F2: R4 external ⇒ `external-request-await`; RM (absent from ownership) ⇒ `unknown`, never self.
3. F1b: single unit P1..P10, `assumed-same-repo`, versioning-difference note + gap.
4. F8: publication map attached per package exactly as declared (P1..P4 published, P5..P8 unpublished).
5. F5: N1 attributed external ONLY via `externalPackages` declaration.
6. Every entity traceable to at least one input record; nothing derived from names.

## 5. Out of scope

Real tracemap fact ingestion (converter per SPEC-001 §9); `PackageProduced` emission; corroboration beyond V0's single-source reality; anything wave/order related (SPEC-003).

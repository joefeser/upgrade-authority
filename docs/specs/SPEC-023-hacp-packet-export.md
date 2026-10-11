# SPEC-023 — The ua HACP extension profile + wave→task-packet export (the seam slice)

**Status:** reviewed — round 2 SHIP verdict (round-1 FIX THEN SHIP folded; records in `docs/reviews/spec-023/`)
**Author:** ZCode (coordinator); the design-talk outcome (2026-10-10, `docs/planning/17-design-talk-brief.md`): ua = engine with published contracts, leaning WITS-as-system-of-record. The first slice — identical under postures 2 and 3.
**Date:** 2026-10-10 (revised same day)
**Depends on:** SPEC-003 (plan.v1 waves/gates) · SPEC-009 (BuildManifest — the expectedEvidence derivation) · SPEC-022 (samplerepos — the demo estate) · **HACP `schemas/v0.3-candidate/task-packet.schema.json` + `common-defs.schema.json`** (the pinned target — closed schema, JSON Schema 2020-12, `additionalProperties: false`) · the who-decides lessons (closed schemas ⇒ separately versioned extension profile; every decision mints exactly one claimable receipt)

## 1. Purpose

WITS (or any HACP-conformant coordinator) cannot drive ua waves until a wave can ARRIVE as a contract record. This slice defines the **ua HACP extension profile** (`ua-hacp-profile-v0.1` — ua-owned, separately versioned, never edits to the base contracts) and ships ONE direction of ONE record: **a ua plan wave exported as a HACP v0.3-candidate task packet**, read-only, offline, byte-deterministic — demoed over the samplerepos estate. An EMITTER only.

**Target pinned: HACP v0.3-candidate** (round-1 finding 5: v0.1 pins profile consts — no `profileRefs` — and requires a `created_at` clock byte; v0.2 carries no task-packet schema at all). `profileRefs: ["ua-hacp-profile-v0.1"]` is the sanctioned extension pointer, and the record carries **no keys outside the closed base schema** — ua specifics live in VALUES, in the profile doc, and in the pointer.

## 2. The mapping (wave → task packet) — every row traces to plan.v1 (via the deterministic in-memory re-derivation `ua export-packet <fixture-dir> --wave <N>` performs) **or to a profile-doc constant**; the profile doc is the adjudication record

| task-packet field | ua source |
|---|---|
| `recordKind` | const `"hacp.v0_3_candidate.task_packet"` |
| `schemaVersion` | const `"hacp-0.3-candidate"` |
| `packetId` | `ua-<package>-<old>-to-<new>-wave-<N>-<64hex>` — the full delta identity + the FULL tuple digest (delimiter-only ids are ambiguous, and short suffixes are brute-forceable — implementation-review amendments) |
| `digest` | the contract object: `algorithm: sha256`, `canonicalization: json-rfc8785-jcs`, `digestDomain: io.hacp.task-packet.v0.3-candidate`, `value` = sha256 hex of the **JCS (RFC 8785) serialization of `{"domain":<domain>,"record":<record-minus-digest>}`** per the hacp conformance package. ua implements an in-house JCS serializer (scoped: ua records are strings/ints/arrays/objects — no floats, no number-formatting hazards); the EMITTED bytes stay in ua's house display form (2-space, LF) — only the digest computation uses JCS |
| `sourceContext.humanRequest` | profile constant: `"operator-invoked export; the committed delta sidecar is the request record; ua V0 has no separate human-request artifact"` |
| `sourceContext.sourceRefs` | the wave's repos + the fixture's scan provenance (repo names) |
| `requestedScope` | the wave's repos + the packages their units carry (wave-scoped; the plan-level external-request-await coordination reality rides the gate text — profile doc paragraph) |
| `loopPolicy` | **profile constants** — `mode: single_pass`, `ceiling: 1`, `counter: 0`, `incrementTrigger: <packetId>`, `claimAttemptLimit: 1`, `automaticRetryAllowed: false`, `boundaryStatement: "a ua wave is single-pass by construction: one branch, one commit; an existing branch refuses — never force-updated"` |
| `forbiddenEffects` | house invariants as text (profile-enumerated): never merges; never force-updates; never downgrades; edits only verified sites |
| `expectedEvidence` | per release unit: edit-site facts (paths + evidenced package spellings + old/new versions) via the same `BuildManifest` derivation apply uses; unit `notes`/`publicationStatus` ride verbatim where present |
| `stopConditions` | the wave's gates verbatim — `condition` (T-templates), `blockedOn` resolved to the contradiction's **subject + claims text** (never a bare id), prerequisites; the plan-stop reason when the plan stopped; **fallback constant for ready waves** (minItems is 1): `"gates clear; executor preserves idempotency-by-refusal — an existing branch refuses"` |
| `profileRefs` | `["ua-hacp-profile-v0.1"]` |
| `boundaryStatement` | honest-boundary prose: evidence is stated-not-verified-live; spellings are the estate's own; unknowns listed |

## 3. The extension profile document

`docs/contracts/ua-hacp-profile-v0.1.md` (versioned in THIS repo): the mapping table, the constant definitions and their rationales (§2's profile constants), the dispositions for wave facts without record homes (unit `basis` is the V0 unit model — semantics in the profile, not a field; `wave.status` is reconstructible from stopConditions content — the inverse mapping documented), the versioning rule (base-schema bumps force a profile bump; profile bumps never fork the base), and the consumer contract (a coordinator claiming this packet gets exactly ua's wave semantics — idempotency-by-refusal included).

## 4. Acceptance criteria

1. Over the **samplerepos Diesel-delta wave** (it carries T5/T6/T6a gates), `ua export-packet` emits a record that **validates against the pinned v0.3-candidate schema**: a pinned copy of the schema (+ common-defs) is COMMITTED under `docs/contracts/hacp/` with Apache-2.0 attribution (the license explicitly permits copying schemas into implementation suites); validation is two-tier — ua's own strict structural checks in the selftest (closed key set, consts, shapes, digest recomputation — no external validator dependency; we control the emitted shape), plus OPTIONAL full ajv validation via the hacp repo's node tooling documented for developer machines.
2. Byte-deterministic: two runs identical; permutation-invariant; no clock bytes (selftest). The JCS digest recomputes identically (selftest).
3. Every emitted value traces to plan.v1 or a profile-doc constant — the profile doc is the adjudication record (selftest pins representative rows).
4. Existing selftest/validator counts unchanged except explicit new cases; goldens untouched.
5. The harness grows an export+validate step over the samplerepos fixture (PowerShell 5.1-safe); public-clean green.

## 5. Out of scope (typed, honest, logged)

- The reverse direction (agent-report → ua continuation), consumption receipts, coordinator behavior — later slices.
- Post-execution agent-report emission (push integration) — its own slice.
- Upstreaming anything to the HACP repo — the profile is ua-owned; upstreaming is Joe's call after WITS dogfoods.
- Float/number JCS edge cases — ua's records carry none; the serializer's scope is stated and tested at its boundary.

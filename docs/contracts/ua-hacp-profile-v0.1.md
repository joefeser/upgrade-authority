# ua-hacp-profile-v0.1 — the upgrade-authority extension profile for HACP task packets

**Target:** HACP `schemas/v0.3-candidate/task-packet.schema.json` (pinned copy: `docs/contracts/hacp/`,
from joefeser/hacp @ `038fad8890218466534fc67fdbff0340edb8985f`). **Emitter:** `ua export-packet
<fixture-dir> --wave <N>` (SPEC-023). The record is CLOSED — no keys outside the base schema; ua
semantics live in this document, in the record's VALUES, and in `profileRefs:
["ua-hacp-profile-v0.1"]`.

## The mapping (every value traces to plan.v1 or to a constant defined here)

| Field | Source |
|---|---|
| `recordKind` / `schemaVersion` | schema consts (`hacp.v0_3_candidate.task_packet` / `hacp-0.3-candidate`) |
| `packetId` | `ua-<package>-<old>-to-<new>-wave-<N>-<8hex>` — the readable delta identity PLUS a disambiguating 8-hex sha256 of the canonical `(package, old, new, wave)` tuple (delimiters alone are ambiguous under adversarial version strings); distinct upgrades of one package can never share a claim |
| `digest` | sha256 of the JCS (RFC 8785) serialization of `{"domain":"io.hacp.task-packet.v0.3-candidate","record":<record-minus-digest>}` — implemented in ua, **cross-verified byte-identical against the reference `canonicalize` v4 library**; the emitted display bytes stay in ua's house form (2-space, LF) — only the digest computation uses JCS |
| `sourceContext.humanRequest` | **constant**: "operator-invoked export; the committed delta sidecar is the request record; ua V0 has no separate human-request artifact" |
| `sourceContext.sourceRefs` | the wave's release-unit repos + `scan:<repo>` for every repo with INGESTED scan evidence (facts or lock rows) — ownership-only names are never cited as scans |
| `requestedScope` | wave-scoped: unit repos + the packages their units carry. NOTE: the plan-level `external-request-await` coordination reality (an externally-owned producer) reaches the packet through the wave's gate text (T6), not through scope — scope is what the EXECUTOR may touch |
| `loopPolicy` | **constants** — `single_pass` / ceiling 1 / counter 0 / `incrementTrigger = packetId` / claimAttemptLimit 1 / automaticRetryAllowed false / boundaryStatement: "a ua wave is single-pass by construction: one branch, one commit; an existing branch refuses - never force-updated". ua's idempotency-by-refusal IS the loop boundary |
| `forbiddenEffects` | **constants** (the house invariants): never merges; never force-updates; never downgrades; edits only verified sites |
| `expectedEvidence` | per unit: edit sites (path, evidenced package spelling, old→new, edit kind) via the SAME BuildManifest derivation `ua apply` uses — never a second truth; unit notes and publicationStatus ride verbatim; republish-required / external-request-await actions are named |
| `stopConditions` | the wave's gates VERBATIM (condition templates, prerequisites); `blockedOn` resolves to the contradiction's subject + claims text (never a bare id); the plan-stop reason when stopped; **ready-wave fallback constant**: "gates clear; executor preserves idempotency-by-refusal - an existing branch refuses" |
| `boundaryStatement` | **constant**: "evidence is stated, not verified live; package spellings are the estate's own; unknowns are listed, never defaulted" |

## Dispositions (wave facts without record homes)

- **`unit.basis`** (`assumed-same-repo`): the V0 unit MODEL — semantics documented here, carried by
  the profile, not a record field. A future basis value is a profile-semantics change.
- **`wave.status`**: PARTIALLY reconstructible from `stopConditions` — the fallback constant ⇒
  `ready`; a `condition` ⇒ `conditional`; an `unresolved` contradiction ⇒ `provisional`. A wave
  carrying ONLY prerequisites is ambiguous between `ready` and `conditional` (ua emits ready waves
  with non-blocking prerequisites) — a coordinator treating prerequisite-only packets as blocking
  is conservative and execution-correct; treating them as ready matches ua's scheduling. The
  ambiguity is deliberate honesty: the closed record cannot carry the enum, and guessing wrong in
  either direction is safe (the executor re-derives truth from evidence anyway).
- **`unit.notes` / `publicationStatus`**: ride `expectedEvidence` verbatim, prefixed by repo.

## Implementation notes (caps — fail loud at emission)

stringSet entries cap at 1024 chars; `boundaryStatement` at 4096. `export-packet` REFUSES (typed
error naming the field) rather than emitting an invalid record — gate text and contradiction
composites grow with estate content, and the refusal is the honest failure.

## Versioning

Base-schema bumps (a new HACP candidate/ release) force a `ua-hacp-profile` version bump that
re-pins fresh schema copies; profile bumps never fork or patch the base. The profile is ua-owned;
upstreaming any part is the owner's call after consumers dogfood it.

## Consumer contract

A coordinator claiming a packet under this profile gets exactly ua's wave semantics: single-pass
execution, one branch and one commit per wave, idempotency-by-refusal (an existing branch is never
force-updated), gates as stated-not-verified-live preconditions, and edit evidence whose spellings
are the estate's own. The reverse direction (agent-reports back into ua) is a future slice with its
own profile work.

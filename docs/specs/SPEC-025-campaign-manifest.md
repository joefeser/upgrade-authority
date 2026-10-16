# SPEC-025 — The campaign manifest: digest-bound provenance for exported packets

**Status:** reviewed — round 2 SHIP verdict (records in `docs/reviews/spec-025/`)
**Author:** ZCode (coordinator); responds to the WITS coordinator's integration assessment (2026-10-12, reviewed ua @ 5a9c34e + WITS @ e8797358; verdict: **strong product fit, conditional execution fit** — "immutable intake plus a useful decision view is justified; unattended wave execution waits for the return contract"). Their ua-side pre-slice ask, verbatim: *"publish a small digest-bound companion manifest identifying estate, campaign, plan/input snapshot and profile provenance."*
**Date:** 2026-10-12
**Depends on:** SPEC-023 (export-packet, the JCS envelope, the profile) · SPEC-020 (estate identity) · the WITS verdict record (`docs/reviews/spec-025/WITS-VERDICT.md`)

## 1. Purpose

WITS intake (or any coordinator) can verify a packet against its schema and digest — but it cannot tell WHICH estate/campaign/input-snapshot produced it, so it cannot distinguish a duplicate resubmission from a **replan** (same packetId — the id hashes only the upgrade tuple + wave ordinal — with changed scope or evidence). The manifest is the ua-owned companion record that binds every exported packet to its full provenance: estate, campaign, the exact input-file snapshot the plan was derived from, and the profile/schema provenance. It changes nothing in the closed HACP packet; it rides beside it.

The outcome mapping is agreed with WITS and recorded here as the integration contract: **campaign → WITS Case; wave → WorkItem; exported packet → immutable Artifact (bytes + digest + profile preserved); gates → visible blockers (human decisions only where discretionary); push/campaign outputs → execution artifacts (partial/refused/zero-edit outcomes preserved). "Proposal accepted" is NOT "execution authorized"** — WITS Product Decisions preserve `executionAuthorized: false`, and ua treats no returned receipt as permission to proceed until a consumption contract exists (a future spec; SPEC-023 §5's deliberate deferral stands).

## 2. The command

```
ua export-campaign <fixture-dir> --out <dir>
```

Writes, atomically (the apply/push temp-dir-swap-and-replace precedent — NOT emit-deltas' plain writes), into `<dir>`:
- `wave-<N>.json` per wave — exactly the bytes `ua export-packet --wave <N>` emits (same writer, same digest);
- `campaign-manifest.v1.json` — the manifest below.

Re-running replaces `<dir>` wholesale; crash leftovers ride the house `.name.tmp-*`/`.name.old-*` sibling convention (never content). `--out <dir>` is REQUIRED (the push/ingest required-dir precedent). Bare flag refuses (exit 1). Fixture load errors exit 3.

## 3. The manifest (`campaign-manifest.v1`, canonical house JSON; JCS-envelope digest like the packet's, over the ua-owned domain `io.ua.campaign-manifest.v1`)

```json
{
  "schemaVersion": "campaign-manifest.v1",
  "campaignId": "ua-<pkg>-<old>-to-<new>-<64hex sha of (pkg, old, new, sorted estate repos)>",
  "digest": { "algorithm": "sha256", "canonicalization": "json-rfc8785-jcs",
              "digestDomain": "io.ua.campaign-manifest.v1", "value": "<64hex>" },
  "generator": "<ua assembly informational version>",
  "campaign": { "package": "<pkg>", "oldVersion": "<old>", "newVersion": "<new>" },
  "estate": { "repos": ["<ordinal-sorted repo identities>"],
              "inputSha256": { "<input file name>": "<sha256 of its bytes>" },
              "planSha256": "<sha256 of Canonical.Write(plan) bytes>" },
  "profile": { "ref": "ua-hacp-profile-v0.1",
               "hacpSchemaSha256": { "task-packet.schema.json": "<sha>", "common-defs.schema.json": "<sha>" } },
  "packets": { "wave-1": { "packetId": "<id>", "digest": "<the packet's digest.value>" } }
}
```

- **`campaignId`** — the campaign analogue of packetId's design: readable tuple prefix + the FULL sha256 of (package, old, new, sorted estate repos). WITS keys Cases on campaigns; duplicate-Case detection composes this id instead of hand-compositing fields. Distinct estates proposing the same tuple never collide.
- **`generator`** — the ua assembly's informational version: same inputs + changed planner = identical `inputSha256`, different packet digests — THIS field self-describes that revision class.
- `estate.repos` — the planner's normalized repo identities, ordinal-sorted.
- `estate.inputSha256` — sha256 over the BYTES of exactly **the loader's known input names, the present ones** (producer-evidence.v0.json, ownership.v0.json, package-evidence.v0.json, delta.json, the one present lockfile-rows.vN.json, scope.v0.json?, build-freshness.v0.json?), keys sorted. **Any OTHER file in `input/` is a typed refusal** — unrecognized bytes are never blessed into the snapshot, and stray files can never fabricate a revision signal (round-1 finding 2). **A replan that changes any input changes this map — the same packetId over a changed snapshot is a visible new revision.** This is the record WITS diffs.
- `estate.planSha256` — sha256 of `Canonical.Write(plan)` bytes: the PLAN half of "plan/input snapshot" (the deterministic canonical form the goldens already pin). Input-identical + plan-changed revisions are nameable.
- `profile.hacpSchemaSha256` — digests of ua's pinned schema copies, **embedded as build-time resources** (published binaries carry no docs/ — round-1 finding 4); the selftest asserts embedded == committed, so drift between the repo's pins and what ships is detected.
- `packets` — **body-keyed per wave** (`"wave-<N>"`), one `{packetId, digest}` row each (round-1 finding 1: the shape rides the existing string/int/bool/object/array-of-strings serializer scope — no new node kind). Intake verifies each wave file against its row + the manifest's envelope digest.
- No clock bytes anywhere. Zero-wave plans: zero wave files + empty `packets: {}`; the count invariant (wave files == plan.Waves.Count == packets rows) is asserted.

## 4. Acceptance criteria

1. Over F-case and the samplerepos harness fixture: `export-campaign` writes N wave files + the manifest; each wave file is byte-identical to `export-packet --wave <N>` output; wave files == plan.Waves.Count == packets{} rows; the manifest's envelope digest verifies through the independent round-trip path (`Packet.VerifyDigest` generalized over the domain); the embedded schema digests == the committed copies (selftest).
2. **The replan scenario** (the exact mutation pinned — round-1 finding 6: a new-repo fact without ownership never schedules, so the naive example changes nothing): copy F-case, **flip caseB's ownership team in `ownership.v0.json`** — the repo stays scheduled, its external-note evidence changes, so the SAME packetId returns with a changed packet digest, a changed `inputSha256["ownership.v0.json"]`, a changed `planSha256`, and an unchanged `campaignId` tuple prefix when repos are unchanged. The manifest-to-manifest delta names exactly the changed entries (selftest — the WITS revision-review case).
3. Determinism + permutation invariance; no clock bytes (selftest).
4. **`docs/contracts/ua-wits-integration.md`** (its OWN doc — round-1 finding 5: the versioned profile doc is cited by value in every packet and must stay semantically stable): the outcome-mapping agreement (§1's table), the proposal-accepted ≠ execution-authorized rule, and the manifest's intake contract. The WITS verdict is recorded at `docs/reviews/spec-025/WITS-VERDICT.md`.
5. The harness's plan stage exports the campaign (packets + manifest) for both runs; the README documents the intake flow. Existing counts grow only by explicit new cases; goldens untouched.
6. PowerShell 5.1-safe snippets; public-clean green.

## 5. Out of scope (typed, honest, logged — the WITS verdict's "later" list)

- **Typed gates** (stable gate ids, kinds, evidence-backed satisfaction rules) — needed before execution automation, not before intake; a companion record in a future slice.
- **The return/consumption contract** (decision receipts ua verifies and what authorizes execution) — its own future spec; until then ua ignores receipts as permission.
- **Multi-estate disambiguation inside the packetId** — the manifest's estate block carries it; the id stays compact.
- **HACP upstream feedback** (the human-decision schema's finding/stop-response binding vs a generic pre-execution authorization shape) — recorded in the verdict doc for Joe's channel; not a ua change.

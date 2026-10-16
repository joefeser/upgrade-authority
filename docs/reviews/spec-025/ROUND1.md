# SPEC-025 round 1 — triage (2026-10-12)

**Quorum record:** one adversarial stand-in with repo access. Verdict: **FIX THEN SHIP** — "the replan identity mechanism is exactly what Packet.cs guarantees; findings 1-6 are specification decisions, not architecture moves." All 10 folded into v2.

| # | Finding | Severity | Disposition → v2 |
|---|---|---|---|
| 1 | `packets[]` array-of-objects is unrepresentable in the JCS/writer/verifier paths (arrays-of-strings only); the "generalized VerifyDigest" claim is false as written — silent false-fail | Major | Accepted — **reshape `packets` as body-keyed rows** (`"wave-1": {packetId, digest}`, object-of-objects) — the reuse claim becomes true with only the domain parameter; the serializer's scope stays as-shipped |
| 2 | `inputSha256` over a glob is NOT the loader's surface — stray files create false revisions; recursion unstated | Major | Accepted — enumerate **exactly the loader's known names (present ones)**, sorted; any OTHER file in input/ is a typed refusal (fail-loud on unrecognized bytes — house style) |
| 3 | The manifest pins inputs and packets but not the PLAN or its producer — same inputs + changed planner = undescribable revision | Major | Accepted — **`planSha256`** (sha256 of `Canonical.Write(plan)` bytes) + **`generator`** (the ua assembly's informational version) |
| 4 | `hacpSchemaSha256` has no runtime byte source (published binaries carry no docs/) | Major | Accepted — **embed the pinned copies as build-time resources**; the selftest asserts embedded == committed (the drift-detection guarantee survives publishing) |
| 5 | Acceptance 4 mutated the versioned profile doc in place (same v0.1 string, different bytes) | Major | Accepted — the outcome mapping + manifest contract live in **`docs/contracts/ua-wits-integration.md`** (their own doc); the profile doc stays semantically stable |
| 6 | The replan example fails as written (a new-repo fact without ownership never schedules; no packet digest changes) | Major | Accepted — the pinned mutation: **flip an existing scheduled repo's ownership team** in F-case (external note changes expectedEvidence) OR add a fact for an already-scheduled repo; spec names the exact one |
| 7 | `campaign.deltaSha256` duplicates `inputSha256["delta.json"]` — a second truth | Minor | Accepted — dropped from `campaign`; the tuple fields stay (identity), the bytes live once in `inputSha256` |
| 8 | repos "sorted vs ordinal" wobble; emit-deltas cited as an atomic precedent (it isn't) | Minor | Accepted — "ordinal-sorted" everywhere; the swap precedent named precisely (apply/push), re-run replaces `<dir>` wholesale, crash-leftover convention named (`.tmp-*/.old-*` siblings — intake never sees them as content) |
| 9 | Zero-wave plan edge + the count invariant unstated | Minor | Accepted — stated: zero waves ⇒ zero wave files + empty packets{}; wave files == plan.Waves.Count == packets{} rows (asserted) |
| 10 | Should the manifest mint a campaignId? | Question → accepted | **Yes** — `campaignId` = readable tuple prefix + full sha256 of (tuple, estate repos) — the campaign analogue of packetId's design; WITS keys Cases on campaigns |

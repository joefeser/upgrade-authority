# ua ↔ WITS integration contract (SPEC-025; the WITS coordinator's verdict adopted)

**Verdict received 2026-10-12** (records in `docs/reviews/spec-025/WITS-VERDICT.md`): strong product
fit, conditional execution fit — immutable intake plus a decision view is justified; unattended wave
execution waits for an explicit return contract.

## The outcome mapping (their table, adopted)

| ua concept | WITS representation | Qualification |
|---|---|---|
| Upgrade campaign (estate + package delta) | **Case** within a Project | keeps the multi-repo story together |
| Wave | **WorkItem** within the Case | wave status is a projection of ua evidence + execution results |
| Exported task packet | original, immutable **Artifact** linked as evidence | bytes + digest + profile + provenance preserved |
| Prerequisite / gate | visible blocker / evidence requirement | a human decision ONLY where there is a discretionary choice |
| Review-discovered defect | **Finding/ReviewFinding** | ordinary prerequisites are not findings |
| Human choice | canonical **ProductDecisionRequest** | subject and evidence bind the EXACT imported revision |
| `push.v1` + campaign output | execution artifacts | partial/refused/zero-edit outcomes preserved |
| Transfer between agents | **Handoff** | carries context; NOT an execution claim |

## The rules both sides hold

- **"Proposal accepted" is NOT "execution authorized."** WITS Product Decisions preserve
  `executionAuthorized: false`; ua treats NO returned receipt as permission to proceed until a
  consumption contract exists (a future spec — SPEC-023 §5's deliberate deferral stands).
- **The system-of-record split (their correction, accepted):** WITS owns coordination history and
  authority; ua owns its computed plan facts. WITS must not become a second upgrade planner.
- **Duplicate vs revision (the manifest's whole job):** identical resubmission = duplicate (same
  campaignId, same manifest digest, same packet digests); same packetId + changed digest or changed
  `inputSha256`/`planSha256` = a visible new revision requiring delta review; renumbered waves
  inherit nothing. Approved evidence never refreshes silently underneath a decision.
- **Executor refusals are truth:** an existing branch proves collision-or-prior-effect, never
  completion or a durable claim; zero-edit waves create no branch; multi-repo waves can be partial.
  The dashboard projects these states; it never replaces them.
- **`needs_investigation`** is never silently translated into rejection or clarification.

## The intake flow (what WITS consumes)

`ua export-campaign <fixture> --out <dir>` writes `wave-<N>.json` (each exactly the
`ua export-packet --wave <N>` bytes) + `campaign-manifest.v1.json` — the digest-bound provenance
record: campaignId (Case key), estate repos, per-input sha256s (the snapshot WITS diffs on replan),
planSha256, generator, the profile ref, and the pinned HACP schema digests (embedded as build-time
resources; ua's selftest asserts embedded == committed, so shipping drift is detected). Verify each
wave file against its manifest row, then the manifest's envelope digest
(`io.ua.campaign-manifest.v1`, JCS — the same canonicalization as the packets).

## Open items (their "later" list, typed)

- Typed gates (stable ids, kinds, evidence-backed satisfaction rules) — required before execution
  automation; a ua companion record.
- The return/consumption contract (decision receipts ua verifies; what authorizes execution).
- HACP upstream feedback: the human-decision schema binds to review findings/stop responses, not a
  generic pre-execution authorization — Joe's channel.

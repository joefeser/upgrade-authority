# PR #3 round 1 triage — SPEC-002 + SPEC-003 (2026-10-02)

**Quorum batch (per Joe's rule — one consolidated pass after ALL quorum reviews):** Qodo 7 High · Codex 11 P1 + 2 P2 · Sourcery rate-limited · Kiro silent (additive). Raw: [quorum-inline-raw.md](quorum-inline-raw.md) · golden changes under the fidelity process: [GOLDEN-CHANGES.md](GOLDEN-CHANGES.md).

**Convergence of the two quorum reviewers on the core theme:** SPEC-003's determinism contract did not reproduce the merged goldens byte-exactly (templates incomplete/over-specific; serializer under-specified; ripple rule contradicted F4b; gap rule over-broad). All findings accepted; none rejected. Resolution = canonicalization of the corpus + complete contract, with golden changes GC1–GC7 recorded and reviewed.

| Finding(s) | Disposition |
|---|---|
| Qodo High-1 + Codex P1 (ripple/F4b REWORK) | **Accepted — the biggest one.** Ripple *obligation* (rebuild when a consumed unit republishes) split from transitive *content exposure* (needs lockfile; else gap). F4b R3 → affected via ripple, scheduled (GC1); F4a/F4b now differ only in path-evidence vs gap. SPEC-003 §2 rewritten with the precedence clause. |
| Qodo High-2/3/4 + Codex P1 (templates) | **Accepted.** Complete exhaustive template set T1, T4–T9 (T2/T3 removed — no evidence-backed discriminator; GC2). Selection rules fixed-order; validator mapping check added (acceptance 9). |
| Qodo High-5 + Codex P1 (serialization) | **Accepted.** Canonical formatter `tools/canonicalize-goldens.mjs` (normative); all 12 goldens rewritten (GC7); validator enforces canonical bytes. |
| Qodo High-6 + Codex P1 (ready vs external) | **Accepted.** `ready` = availability-statement prerequisites (including external-feed T5); `conditional` = gating conditions (external team action, unpublished, unknown producer). SPEC-003 §3.4 rewritten. |
| Qodo High-7 + Codex P1 (gap scoping) | **Accepted.** Unknown-producer gaps only when they gate the plan (delta target's producer; wave-required producers). E1/X9 gaps removed from F1/F1b (GC3). SPEC-002 rule 4 rewritten. |
| Codex P1 (unknown overriding observed edges) | **Accepted.** Precedence clause: observed edges always classify `affected`; unknown conditions affect actionability/wave status only (F3a/F3b goldens now derivable). |
| Codex P1 (unscheduled-repos too broad) | **Accepted.** Only ownership-unknown repos unscheduled; actionType-unknown repos schedule into conditional/provisional waves. §3.6. |
| Codex P1 (multi-change deltas undefined) | **Accepted.** V0 = single-change; typed ingest rejection for `changes[] > 1` (§1a, acceptance 2). |
| Codex P1 (F8 disclaimer) | **Accepted.** T7a carries the disclaimer (GC5); acceptance invariant now holds corpus-wide. |
| Codex P2 (array ordering incomplete) | **Accepted.** §4.2 orders every array + permutation acceptance case (SPEC-005). |
| Codex P2 (wave partitioning) | **Accepted.** Deterministic layering: (status rank, depth), same-(rank,depth) units share a wave sorted by name (§3.5) — checked against all 12 goldens. |

**State:** all 12 goldens canonical + structurally valid (validator includes canonical-bytes check). Round 2 (delta review only) requested from the quorum.

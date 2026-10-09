# SPEC-017 round 2 — delta verification + verdict (2026-08-08 → 2026-10-08)

**Reviewer:** verification stand-in (delta brief; no external verifier CLIs available — same quorum
record as round 1, doctrine §5 no-verifier clause).

**Result:** every round-1 blocker and major **RESOLVED** as claimed; all spot-checked minors resolved.
No new blockers or majors introduced by the patches. Two minors + two nits rode back:

| # | Riding finding | Severity | Disposition |
|---|---|---|---|
| R2-1 | Tracemap-hash reuse test undefined on a no-`--tracemap` all-reuse run | Minor | Fixed inline: hash compared only when `--tracemap` given; such runs succeed (§4.2) |
| R2-2 | Wrapper's alternate-checkout dedupe had no AC case | Minor | Fixed inline: two-clones case added to §7.1 |
| R2-3 | `outOfScope` empty-representation unpinned | Nit | Fixed inline: omitted when empty (§5) |
| R2-4 | Dot-prefix discovery skip stated only in an AC, not prose | Nit | Fixed inline: stated in §3 prose |

**Convergence rule (doctrine §2): zero open blockers and zero open majors — review ends.**

## VERDICT: SHIP

Round 1 = full fight (2 reviewer stand-ins, 2 blockers + 12 majors/major-overlaps, batched, one patch
pass); round 2 = delta verification only. Quorum caveat stands: Codex/Baz/Qodo/Sourcery did not review
(this environment has none of their CLIs); Joe may re-run the external quorum on the PR — the brief is
`docs/reviews/spec-017/ROUND1.md` + the spec. Proceed to implementation.

## Addendum — external reviewers on the implementation PR (public PR #2, 2026-10-08)

The public branch was opened as PR #2 (joefeser/upgrade-authority), where the external reviewers ARE
installed — closing the quorum gap retroactively on the code:

| Reviewer | Outcome |
|---|---|
| **Baz** (doctrine-rev preferred verifier) | check **pass** (8m); summary-only comment, **zero findings**, zero inline comments — nothing to triage, no dismissal receipts needed |
| **Sourcery** | rate-limited (7-day diff budget exhausted) — posted a descriptive Reviewer's Guide (accurate sequence/flow diagrams), no findings |
| **Qodo** | billing-blocked (out of credits) — no review, matches the doctrine's cost note |
| CI (build + 117-case selftest + validator + public-clean gate) | **green** |

Doctrine §5 status for this round: Baz pass + coordinator disposition; Codex absent (no CLI in the
authoring environment; the PR is the standing surface for a Codex pass if Joe ferries one).


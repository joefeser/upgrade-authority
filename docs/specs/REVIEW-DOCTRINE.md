# Spec review doctrine — "two rounds to converge, three to escalate"

**Adopted:** 2026-10-02 (coordinator, per Joe: "we need a doctrine on how many rounds in the spec review process is enough. Sometimes it gets painful to be 6 rounds in")
**Applies to:** every spec review on this project (Codex, Kiro, any future reviewer). Governed by the coordinator; Joe overrides anything.
**Machine enforcement:** this doctrine is encoded as an Agent Control Kit (ACK) lane at `.agent-control/lanes/spec-review-loop.yaml` — doctor-validated against ACK 0.5.5 on 2026-10-02 (`laneConfigError: null`, version + capabilities satisfied). The lane maps doctrine rounds to ACK freshness cycles (initial fight + `maxFreshReviewFixCycles: 2` = 3 rounds max, then typed human-decision stops), admits owner-ferried Codex/Kiro verdicts via external review receipts, and bounds the `claude-local` CLI fallback (2 attempts, $4 each, owner-authorized receipt only). When the lane and this document ever disagree, this document wins and the lane gets fixed.

## 1. Round structure

- **Round 1 — the fight.** Full adversarial review of the whole spec. This is where reviewers are expected to bring their strongest objections. Briefs say so explicitly.
- **Round 2 — delta review.** Reviews *only* the fixes and the round-1 majors. Re-litigating settled parts of the spec is out of order.
- **Round 3 — exception only.** Happens only if round 2 surfaced *new* blockers or majors. Delta review again.
- **After round 3: no more rounds. Ever.** Remaining blockers go to Joe for a tie-break or a rework decision; everything else ships with dissent logged. Six rounds is a process failure, not thoroughness.

## 2. Convergence rule (stop early)

A round that produces **zero open blockers and zero open majors ends review immediately** — even round 1. ("Open" = raised this round or in any earlier round and not yet verified fixed; a delta round must confirm each carried blocker/major is resolved before convergence counts.) Minors and preferences never buy another round: they get logged in the spec's KNOWN-ISSUES section and fixed inline. A clean spec ships in one round; that is the intended fast path.

## 3. Late-issue rule

Objections raised after round 1 carry full weight **only if they are blockers**. Late majors and minors are logged and queued for the next spec revision instead of triggering another round. Reviewers who sandbag their best material for round 4 forfeit it. Rationale: round 1 is the fight; later rounds exist to verify fixes, not to discover a new front.

## 4. Verdicts (every round ends with one)

- **SHIP** — merge and build. Minors logged.
- **FIX THEN SHIP** — apply fixes; one delta round to verify; no full re-review.
- **REWORK** — the spec's premise is wrong. Not another review round: it leaves review, goes back to the queue/Joe with the objection attached, and comes back as a revised spec starting fresh at round 1.

## 5. Disagreements and silence

- **Quorum (per Joe, 2026-10-02):** **Codex and Qodo are the review quorum** (the two auto-reviewers). **Wait for ALL quorum reviews to return before patching** — no serial per-reviewer patching; one consolidated fix pass per round. Kiro and Sourcery are additive: their findings join the same batch; a late/absent Kiro never blocks (blockers-only rule below), and an approval from either is welcome but not required for convergence. **Qodo reviews once per PR (first pass only, per Joe 2026-10-02):** its findings join round 1's batch; from round 2 on, verification is Codex's re-review (+ any additive reviewer).
- **Reviewer disagreement** (e.g., Codex says SHIP, Kiro says REWORK): the coordinator decides and logs the rationale. Reviewers advise; the coordinator decides; Joe overrides.
- **Silence:** a reviewer who hasn't responded within **1 working day** of the brief being ferried is recorded as no-objection and the process proceeds. Silence is never a veto.

## 6. What goes to Joe (and nothing else)

Only two things interrupt Joe about a spec review: (a) blockers still alive after round 3, (b) a coordinator-waived major he should know about (logged). Everything else is coordinator discretion.

## 7. The arbiter is the acceptance check, not rhetoric

Most review disputes reduce to: *does an acceptance check already cover this?* If yes → ship, the check will catch it. If no → adding the check is the fix; that's a FIX THEN SHIP, not a debate. Reviewers should frame objections as missing acceptance checks whenever possible (this is the fidelity rule doing its job).

## 8. Spec-size signal

If cumulative review effort exceeds roughly **2× the spec's drafting effort**, the spec is too big — split it and continue. Reviews that hurt are usually specs that are really three specs.

## One-line version (goes in every brief)

> Round 1 = full fight; round 2 = fixes only; round 3 = exception, then Joe decides. Zero OPEN blockers/majors = review over. Late issues only count as blockers. Silence after 1 working day = proceed. Disagree → coordinator decides, Joe overrides.

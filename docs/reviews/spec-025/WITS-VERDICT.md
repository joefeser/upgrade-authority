# The WITS coordinator's integration assessment (2026-10-12, recorded verbatim-received)

**Reviewer:** the WITS coordinator (source review of ua @ 5a9c34e + WITS @ e8797358; two independent
assessments agreed on the main gaps). **Verdict: strong product fit, conditional execution fit** —
"UA belongs in WITS... I would integrate it, starting with packet intake, review, and decision
return; automatic execution needs a small but explicit contract first." Neither assessment found a
reason to reject.

The full text lives in the session record; the load-bearing points for THIS spec:

1. **The pre-slice ua ask**: "publish a small digest-bound companion manifest identifying estate,
   campaign, plan/input snapshot and profile provenance." → SPEC-025.
2. **The outcome mapping** (their table, adopted): campaign → Case; wave → WorkItem; exported
   packet → immutable Artifact linked as evidence; gates → visible blockers (human decision only
   when discretionary); review-discovered defects → Findings; human choice → ProductDecisionRequest
   bound to the exact imported revision; push/campaign output → execution artifacts preserving
   partial/refused/zero-edit outcomes; agent transfer → Handoff (not an execution claim).
3. **The system-of-record correction (accepted)**: WITS owns coordination history and authority;
   ua keeps owning its computed plan facts. WITS must not become a second upgrade planner.
4. **Their three frictions**: (a) replan identity — same packetId, different digest is a visible
   new revision; identical resubmission is a duplicate; renumbered waves inherit nothing. (b) gates
   are text — fine for display, typed gates before automation. (c) approval ≠ execution admission
   ≠ branch-collision — three different states; an existing branch proves collision-or-prior-effect,
   never completion or a durable claim; keep the executor's refusals.
5. **The return record**: HACP's human-decision binds to review findings/stop responses — not a
   generic pre-execution authorization; useful HACP contract feedback (Joe's channel). An executable
   return needs packet + decision-request digests, authority refs, permitted operation/scope,
   validity limits, one-use consumption evidence — and ua must publish a consumption contract
   before treating receipts as permission.
6. **Their first slice** (fictional estate): import twice without duplicating; changed packet shows
   the delta; one human decision; a non-executing receipt ua verifies; campaign report attached with
   partial/refused outcomes preserved. SPEC-025's manifest is the ua-side prerequisite.

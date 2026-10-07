# PR #2 round 1 triage — fixture corpus (2026-10-02)

**Reviewer:** Qodo (13 inline findings: 6 High, 7 Medium). Codex triggered; Kiro pending (quota). Raw: [qodo-inline-raw.json](qodo-inline-raw.json) · [review-bodies.md](review-bodies.md).
**Verdict interpretation:** FIX THEN SHIP for the corpus. All 13 findings accepted — none rejected. Every finding was the right class: *goldens asserting what their inputs cannot derive* (the exact failure mode the corpus exists to catch — the review process is working).

| # | Sev | Finding | Disposition |
|---|---|---|---|
| 1 | High | F2/F6: observed-edge consumers (RX/RB) downgraded to `unknown` and de-scheduled | **Accepted — semantic correction.** An observed reference is positive evidence of affected-ness; coverage gaps qualify additional unknown work, never erase the edge. RX/RB now `affected` + executable + in waves; gaps retained in `uncertainty`. Rule written into SPEC-001 §4 (fusion rule 1). |
| 2 | High | F5: `delta.origin` asserted from no input | **Accepted.** Origin removed from golden; public-package attribution now derives from producer absence (gap wording). SPEC-000 §4 F5 amended. |
| 3 | High | F8 wave 2 conditioned on a P1 new version no input declares | **Accepted.** Wave 2 condition/prereq now P3-only (the declared delta). |
| 4 | High | F1: RU `not-affected` without positive evidence (X9 transitive exposure unknown) | **Accepted.** Added RU's `lockfile-rows.v0.json` (X9 direct, no P1 row); RU's reason cites lockfile closure as positive evidence; corroboration 2. |
| 5 | High | F4a/F4b: identical inputs read oppositely (R2 fully-scanned-zero-refs vs I1→P2 edge) | **Accepted.** Added R2→P2 direct fact to both fixtures' inputs; R2 affected via its own reference in both; F4a additionally corroborated by R3's lockfile; F4b's R3 stays `unknown`. Rule written into SPEC-001 §4 (fusion rule 2: producer zero-refs ≠ independence). |
| 6 | High | F1b golden inconsistent with F1 on shared inputs (missing E1 gap, prereqs, evidence kinds) | **Accepted.** F1b golden realigned with F1's; only P9/P10 deltas differ. |
| 7 | Med | F1: R3's direct P1 fact let implementations skip the R2→R3 chain | **Accepted.** Direct R3→P1 fact removed; R3 now affected by **unit ripple** (consumes I1 from affected R2) — the chain is the only derivation path. |
| 8 | Med | schema lacks confidence representation (SPEC-001 §5) | **Accepted.** `confidence: {rung, corroboration}` added to `repoClassification` (required); all 12 goldens carry it. |
| 9 | Med | schema permits external + executable pairing | **Accepted.** ownership/actionType/evidenceKinds now required; if/then: external ⇒ `external-request-await`; mirrored in validator. |
| 10 | Med | schema: provisional/conditional waves can omit blockers/conditions; repo fields optional | **Accepted.** if/then rules added (provisional⇒blockedOn, conditional⇒condition); repo fields required; `additionalProperties: false`. |
| 11 | Med | schema: stop with non-empty waves; CYCLE without cyclePath | **Accepted.** stop⇒waves maxItems 0; CYCLE_DETECTED⇒cyclePath minItems 2. |
| 12 | Med | validator misses wave index/unit fields; success overstates | **Accepted.** Validator extended (index ≥1, unit repo/packages, confidence shape, pairing rules); success line relabeled "STRUCTURAL CHECKS — full JSON-Schema validation lands with SPEC-005 harness". |
| 13 | Med | F8 wave 2 `conditional` while identical situations elsewhere are `ready` | **Accepted.** Wave 2 now `ready` with ordinary stated prerequisite (consistency with F1/F4a/F6/F7); wave 3 (P6, evidenced-unpublished) remains the conditional case. |

**Round 2 request (delta only):** verify the 13 dispositions above in the updated corpus; goldens all pass the hardened structural validator (12/12).

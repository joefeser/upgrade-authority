# SPEC-018 golden changes — F-drift addition

Reviewed per the fidelity rule: every golden adjudicated against the spec BEFORE capture.

| Fixture | Change | Spec rule |
|---|---|---|
| F-drift | **NEW fixture** (real `ua ingest` over billing+shipping+svc+cleanrepo + `input/feed-versions.v1.json`): goldens = `drift.v1` (+ `golden/deltas/Contoso.Payments.1.0.0.delta.json`) **and** plan/report — a full corpus fixture, so the existing golden + permutation loops cover it automatically | §8 acceptance 7 (the verified status table); delta candidate per §7 |
| All 24 existing fixtures | **byte-identical** — machine-checked by the 124-case selftest run and `validate-fixtures.mjs` (25 goldens) | Drift adds a command + validator branch; no planner/report/ingest output changes |

The zero-churn claim is machine-enforced: any existing fixture gaining or losing a byte fails CI.

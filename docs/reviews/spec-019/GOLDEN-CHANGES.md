# SPEC-019 golden changes — F-deps + F-deps-mixed additions

| Fixture | Change | Spec rule |
|---|---|---|
| F-deps | **NEW** — REAL tracemap `--index-deps-json` output (scratch App repo, committed at `testdata-ingest/tracemap-rich/deps-scans/appdeps/` — outside `scans/` so discovery-enumerated estates stay unchanged) + `build-freshness.v0.json` (fresh) | §4 (deps.json rows, provenance, consumer-path exclusion), §5.3 (Q1 qualifier) |
| F-deps-mixed | **NEW** — F-deps + one hand-added lockfile row (12.0.3 vs deps.json 13.0.3) | §5.3 (mixed closures name both kinds), SPEC-007 (T10 on divergence; distinct resolution groups by verbatim tfm) |
| All 25 existing fixtures | **byte-identical** — machine-checked (132-case selftest + validator 27) | lockfile-only closures keep the pre-019 reason text verbatim (19-5) |

Zero churn is machine-enforced by CI.

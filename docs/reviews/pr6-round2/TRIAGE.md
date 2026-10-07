# PR #6 round 2 — Kiro delta review disposition (2026-10-03)

**Verdict:** FIX THEN SHIP. **All findings accepted:**

| Finding | Fix |
|---|---|
| **R2-B1: atomic output destroys existing on cross-volume move** | Temp dir is now a SIBLING of outDir (same volume, same parent). Safe swap: move old aside, move new in, delete old, rollback on failure. |
| **B2: lockfile dedup still by packageId only** | Now detects version conflicts within a repo: if same packageId resolves to different versions, typed error (exit 4). First-wins silently is gone. |
| **B3 planner side: via=null still picks an arbitrary parent** | Fallback removed. When `via` is null: "parent not evidenced — dependencyNames absent or exceeded 256 chars". No fabricated citations. |
| **R2-M1: coverage enums wrong ("Success"/"Full" vs real values)** | Corrected to tracemap's actual values: `Succeeded` / `Level1SemanticAnalysis`. `not-affected` is now reachable on a clean scan. |
| **R2-M2: bad sidecar exits 0, missing file, Contains check** | Sidecars parsed as JSON; schemaVersion compared exactly; non-zero exit (5) on mismatch. Filename in warning fixed. |
| **B4: ingest gaps only on stderr** | Ingest gaps appended to the affected repo's `scanCoverage.gaps`, reaching the plan. |
| **via on direct rows** | Restricted to transitive rows only. |
| **commitSha "unknown"** | Passed through as null, not "unknown". |
| **F5 golden updated** | "names=null treated as unknown child detail" removed (was the B3 fallback's misleading message). Fidelity-recorded. |
| **ingest-real moved** | Moved from `fixtures/` to `testdata-ingest/` (it's tracemap raw output, not a golden fixture; the canonicalizer and validator were trying to process it). |

**Not yet done (deferred to Kiro's "path to SHIP" list):**
- Kiro's original round-1 scans with the richer estate (VersionOverride, transitive Contoso.Core, multi-TFM version conflicts) not committed as fixtures — these need Kiro's scan data
- Ingest selftest case (automated acceptance) — next item
- `--scans-root <dir>` convenience for estate scale — noted as minor

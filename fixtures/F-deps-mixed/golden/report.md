# Impact plan: Serilog 2.0.0 → 3.0.0

## Summary
- Delta: Serilog updated 2.0.0 → 3.0.0 (nuget)
- Repositories: 0 affected · 1 not-affected · 0 unknown
- Version disagreements: 1 — see Uncertainty
- Waves: 0 (0 ready, 0 conditional, 0 provisional)

## Waves
None.

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| repo | not-affected | self | none | package-evidence.v0, lockfile-rows.v2, scan-coverage | declared ×2 | positive evidence: complete scan coverage AND repo's own lockfile closure (Newtonsoft.Json direct) and build-resolved closure (Newtonsoft.Json direct) — transitive exposure ruled out, closure evidenced by build output (deps.json), freshness: fresh |

## Uncertainty

### Gaps
- **Serilog producer**: Serilog is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)

### Findings
- **repo version disagreement: Newtonsoft.Json**: repo resolves Newtonsoft.Json to 2 versions across its lockfile/TFM resolution groups: 13.0.3 in src/App/bin/Release/net10.0/App.deps.json (.NETCoreApp,Version=v10.0); 12.0.3 in src/App/packages.lock.json (net8.0) — evidenced disagreement, not an error; V0 schedules repo once at unit level and picks no winner (convergence is deliberately out of scope, §10)

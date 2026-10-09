# Impact plan: Serilog 2.0.0 → 3.0.0

## Summary
- Delta: Serilog updated 2.0.0 → 3.0.0 (nuget)
- Repositories: 0 affected · 1 not-affected · 0 unknown
- Waves: 0 (0 ready, 0 conditional, 0 provisional)

## Waves
None.

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| repo | not-affected | self | none | package-evidence.v0, lockfile-rows.v2, scan-coverage | declared ×2 | positive evidence: complete scan coverage AND repo's own build-resolved closure (Newtonsoft.Json direct, no Serilog row) — transitive exposure ruled out, closure evidenced by build output (deps.json), freshness: fresh |

## Uncertainty

### Gaps
- **Serilog producer**: Serilog is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)

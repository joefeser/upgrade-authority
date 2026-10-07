# Impact plan: Serilog 3.1.1 → 4.0.0

## Summary
- Delta: Serilog updated 3.1.1 → 4.0.0 (nuget)
- Repositories: 1 affected · 1 not-affected · 0 unknown
- Waves: 1 (1 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites:
  - Serilog 4.0.0 available from the public feed (Serilog declared external via producer-evidence.v0; stated, not verified live)
- **R-consumer** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R-consumer | affected | self | executable | package-evidence.v0 | declared ×1 | direct reference to Serilog (R-consumer/src/App.csproj); Serilog attribution: DECLARED external via producer-evidence.v0 externalPackages (evidence-backed, not inferred from producer absence — absence alone means unknown, per F3a) |
| R-neg | not-affected | self | none | package-evidence.v0, lockfile-rows.v1, scan-coverage | declared ×2 | positive evidence: complete scan coverage AND R-neg's own lockfile closure (Logging.Lib direct, Newtonsoft.Json transitive, Polly direct, no Serilog row) — transitive exposure ruled out by lockfile evidence, not by absence-of-match |

## Uncertainty

### Gaps
- **Serilog producer**: Serilog is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)

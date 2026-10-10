# Impact plan: Contoso.Sample 0.1.0 → 0.2.0

## Summary
- Delta: Contoso.Sample updated 0.1.0 → 0.2.0 (nuget)
- Repositories: 1 affected · 0 not-affected · 2 unknown
- Waves: 1 (1 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites: none
- **pure-ci** (publishes Contoso.Sample) · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| pure-ci | affected | self | executable | producer-evidence.v0 | fixture-declared ×1 | produces Contoso.Sample (delta target) |
| both | unknown | self | unknown | package-evidence.v0 | declared ×1 | classification unknown: coverage gaps or unresolved exposure — never not-affected without positive evidence |
| vers | unknown | self | unknown | package-evidence.v0 | declared ×1 | classification unknown: coverage gaps or unresolved exposure — never not-affected without positive evidence |

## Uncertainty

### Scan notes
- **vers**: analysisLevel: Level1SemanticAnalysisReduced; scan buildStatus: FailedOrPartial

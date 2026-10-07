# Impact plan: Newtonsoft.Json 12.0.3 → 13.0.3

## Summary
- Delta: Newtonsoft.Json updated 12.0.3 → 13.0.3 (nuget)
- Repositories: 2 affected · 0 not-affected · 0 unknown
- Waves: 1 (1 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites:
  - Newtonsoft.Json 13.0.3 available from the public feed (Newtonsoft.Json declared external via producer-evidence.v0; stated, not verified live)
- **billing** · assumed-same-repo
- **shipping** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| billing | affected | self | executable | package-evidence.v0, lockfile-rows.v2 | declared ×1 | transitive path billing -> Contoso.Payments -> Newtonsoft.Json evidenced by billing's lockfile rows (Newtonsoft.Json transitive via Contoso.Payments) |
| shipping | affected | self | executable | package-evidence.v0, lockfile-rows.v2 | declared ×1 | transitive path shipping -> Contoso.Payments -> Newtonsoft.Json evidenced by shipping's lockfile rows (Newtonsoft.Json transitive via Contoso.Payments) |

## Uncertainty

### Gaps
- **Newtonsoft.Json producer**: Newtonsoft.Json is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)

### Scan notes
- **billing**: Compiler diagnostic category: CompilerDiagnostic. Native output was redacted.; analysisLevel: Level1SemanticAnalysisReduced; scan buildStatus: FailedOrPartial
- **shipping**: Compiler diagnostic category: CompilerDiagnostic. Native output was redacted.; analysisLevel: Level1SemanticAnalysisReduced; scan buildStatus: FailedOrPartial

# Impact plan: Newtonsoft.Json 12.0.3 → 13.0.3

## Summary
- Delta: Newtonsoft.Json updated 12.0.3 → 13.0.3 (nuget)
- Repositories: 1 affected · 0 not-affected · 0 unknown
- Version disagreements: 2 — see Uncertainty
- Waves: 1 (1 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites:
  - Newtonsoft.Json 13.0.3 available from the public feed (Newtonsoft.Json declared external via producer-evidence.v0; stated, not verified live)
- **svc** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| svc | affected | self | executable | package-evidence.v0, lockfile-rows.v1 | declared ×1 | transitive path svc -> Contoso.Core -> Newtonsoft.Json evidenced by svc's lockfile rows (Newtonsoft.Json transitive via Contoso.Core) |

## Uncertainty

### Gaps
- **Newtonsoft.Json producer**: Newtonsoft.Json is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)
- **svc coverage (lockfile-evidenced)**: scan gaps on NuGet lockfile analysis reported `packages-lock-group-unsupported`. — svc is affected via lockfile-evidenced transitive exposure; the gaps qualify how much additional work is unknown, they do not erase the evidenced exposure

### Findings
- **svc version disagreement: Contoso.Core**: svc resolves Contoso.Core to 2 versions across its lockfile/TFM resolution groups: 1.0.0 in src/Api/packages.lock.json (net8.0); 0.9.0 in src/Worker/packages.lock.json (net8.0) — evidenced disagreement, not an error; V0 schedules svc once at unit level and picks no winner (convergence is deliberately out of scope, §10); svc also has lockfile groups the scanner could not parse and contributed no rows — those resolutions stay unevidenced (coverage gap)
- **svc version disagreement: Newtonsoft.Json**: svc resolves Newtonsoft.Json to 2 versions across its lockfile/TFM resolution groups: 13.0.1 in src/Api/packages.lock.json (net8.0); 12.0.3 in src/Worker/packages.lock.json (net8.0) [delta from-version] — evidenced disagreement, not an error; V0 schedules svc once at unit level and picks no winner (convergence is deliberately out of scope, §10)

### Scan notes
- **svc**: Compiler diagnostic category: CompilerDiagnostic. Native output was redacted.; Workspace diagnostic category: SdkResolutionFailed. Native output was redacted.; Workspace diagnostic category: UncategorizedWorkspaceFailure. Native output was redacted.; analysisLevel: Level1SemanticAnalysisReduced; scan buildStatus: FailedOrPartial

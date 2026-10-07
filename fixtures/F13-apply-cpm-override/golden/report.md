# Impact plan: Contoso.Core 1.0.0 → 1.1.0

## Summary
- Delta: Contoso.Core updated 1.0.0 → 1.1.0 (nuget)
- Repositories: 1 affected · 0 not-affected · 0 unknown
- Version disagreements: 2 — see Uncertainty
- Waves: 1 (1 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites:
  - Contoso.Core 1.1.0 available from the public feed (Contoso.Core declared external via producer-evidence.v0; stated, not verified live)
- **svc** · assumed-same-repo
  - note: scheduled on the observed edge; unscanned area may surface additional work — tracked as a gap, not a blocker

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| svc | affected | self | executable | package-evidence.v0, scan-coverage | declared ×1 | OBSERVED direct reference to Contoso.Core (Directory.Packages.props) — an observed reference is positive evidence of affected-ness; coverage gaps qualify HOW MUCH work is uncertain, they do not erase the observed edge; central package management: single pin edit in Directory.Packages.props fans out to Api.csproj + Worker.csproj; Worker.csproj carries an explicit VersionOverride pinning Contoso.Core = 0.9.0 (src/Worker/Worker.csproj) — the override is visible and must be updated or consciously kept; Contoso.Core attribution: DECLARED external via producer-evidence.v0 externalPackages (evidence-backed, not inferred from producer absence — absence alone means unknown, per F3a) |

## Uncertainty

### Gaps
- **Contoso.Core producer**: Contoso.Core is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)
- **svc coverage**: scan gaps on NuGet lockfile analysis reported `packages-lock-group-unsupported`. — svc still affected (observed edge) and scheduled; additional edges/work in the unscanned area remain unknown

### Findings
- **svc version disagreement: Contoso.Core**: svc resolves Contoso.Core to 2 versions across its lockfile/TFM resolution groups: 1.0.0 in src/Api/packages.lock.json (net8.0) [delta from-version]; 0.9.0 in src/Worker/packages.lock.json (net8.0) — evidenced disagreement, not an error; V0 schedules svc once at unit level and picks no winner (convergence is deliberately out of scope, §10); svc also has lockfile groups the scanner could not parse and contributed no rows — those resolutions stay unevidenced (coverage gap)
- **svc version disagreement: Newtonsoft.Json**: svc resolves Newtonsoft.Json to 2 versions across its lockfile/TFM resolution groups: 13.0.1 in src/Api/packages.lock.json (net8.0); 12.0.3 in src/Worker/packages.lock.json (net8.0) — evidenced disagreement, not an error; V0 schedules svc once at unit level and picks no winner (convergence is deliberately out of scope, §10)

### Scan notes
- **svc**: Compiler diagnostic category: CompilerDiagnostic. Native output was redacted.; Workspace diagnostic category: SdkResolutionFailed. Native output was redacted.; Workspace diagnostic category: UncategorizedWorkspaceFailure. Native output was redacted.; analysisLevel: Level1SemanticAnalysisReduced; scan buildStatus: FailedOrPartial

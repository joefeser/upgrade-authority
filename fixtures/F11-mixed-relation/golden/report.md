# Impact plan: Bogus.Target 1.0.0 → 2.0.0

## Summary
- Delta: Bogus.Target updated 1.0.0 → 2.0.0 (nuget)
- Repositories: 1 affected · 0 not-affected · 0 unknown
- Version disagreements: 1 — see Uncertainty
- Waves: 1 (1 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites:
  - Bogus.Target 2.0.0 available from the public feed (Bogus.Target declared external via producer-evidence.v0; stated, not verified live)
- **R11** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R11 | affected | self | executable | package-evidence.v0, lockfile-rows.v1 | declared ×1 | transitive path R11 -> Bogus.Parent -> Bogus.Target evidenced by R11's lockfile rows (Bogus.Target transitive via Bogus.Parent) |

## Uncertainty

### Gaps
- **Bogus.Target producer**: Bogus.Target is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)
- **R11 coverage (lockfile-evidenced)**: scan gaps on resolved version not evidenced: Bogus.Null — R11 is affected via lockfile-evidenced transitive exposure; the gaps qualify how much additional work is unknown, they do not erase the evidenced exposure

### Findings
- **R11 version disagreement: Bogus.Target**: R11 resolves Bogus.Target to 2 versions across its lockfile/TFM resolution groups: 1.0.0 in src/LibA/packages.lock.json (net8.0) [delta from-version]; 2.0.0 in src/LibB/packages.lock.json (net8.0) — evidenced disagreement, not an error; V0 schedules R11 once at unit level and picks no winner (convergence is deliberately out of scope, §10)

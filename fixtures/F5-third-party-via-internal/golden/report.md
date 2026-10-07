# Impact plan: N1 3.0.0 → 3.0.1

## Summary
- Delta: N1 updated 3.0.0 → 3.0.1 (nuget)
- Repositories: 2 affected · 0 not-affected · 0 unknown
- Waves: 2 (2 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites:
  - N1 3.0.1 available from the public feed (N1 declared external via producer-evidence.v0; stated, not verified live)
- **R1** (publishes P1) · assumed-same-repo

### Wave 2 — ready
- Prerequisites:
  - R1 publishes P1 rebuilt against N1 3.0.1 (stated, not verified live)
- **R2** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | executable | package-evidence.v0, producer-evidence.v0 | declared ×1 | direct reference to N1 (R1/src/Lib.csproj); produces P1 — bump + republish flows the update; N1 attribution: DECLARED external via producer-evidence.v0 externalPackages (evidence-backed, not inferred from producer absence — absence alone means unknown, per F3a) |
| R2 | affected | self | executable | package-evidence.v0, lockfile-rows.v0 | declared ×1 | transitive path R2 -> P1 -> N1 evidenced by R2's lockfile rows (N1 transitive via P1) |

## Uncertainty

### Gaps
- **N1 producer**: N1 is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)

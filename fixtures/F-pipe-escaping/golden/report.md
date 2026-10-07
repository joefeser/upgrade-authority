# Impact plan: P1 1.0.0 → 1.2.0

## Summary
- Delta: P1 updated 1.0.0 → 1.2.0 (nuget)
- Repositories: 2 affected · 0 not-affected · 0 unknown
- Waves: 2 (2 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites: none
- **R1** (publishes P1) · assumed-same-repo

### Wave 2 — ready
- Prerequisites:
  - R1 publishes P1 >= 1.2.0 (stated, not verified live)
- **R2** · assumed-same-repo
  - note: packages.config consumer — edit path is packages.config; unscanned pipe case area may surface additional work (tracked as a gap)

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | executable | producer-evidence.v0 | fixture-declared ×1 | produces P1 (delta target) |
| R2 | affected | self | executable | package-evidence.v0, scan-coverage | declared ×1 | OBSERVED packages.config reference to P1 (R2/src/Legacy\|Dir/packages.config) — observed edge is positive evidence of affected-ness; coverage gaps (R2/src/pipe case) qualify additional unknown work, they do not erase the observed edge |

## Uncertainty

### Gaps
- **R2 coverage**: R2/src/pipe case not scanned — R2 still affected and scheduled on its observed edge; additional edges/work in the unscanned area remain unknown

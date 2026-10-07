# Impact plan: P3 1.0.0 → 1.5.0

## Summary
- Delta: P3 updated 1.0.0 → 1.5.0 (nuget)
- Repositories: 3 affected · 0 not-affected · 0 unknown
- Waves: 3 (2 ready, 1 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites: none
- **R1** (publishes P1, P2, P3, P4, P5, P6, P7, P8) · assumed-same-repo
  - note: current publication state cited from producer-evidence.v0 (fixture-declared)
  - publication: P1=published
  - publication: P2=published
  - publication: P3=published
  - publication: P4=published
  - publication: P5=unpublished
  - publication: P6=unpublished
  - publication: P7=unpublished
  - publication: P8=unpublished

### Wave 2 — ready
- Prerequisites:
  - R1 publishes P3 >= 1.5.0 (stated, not verified live)
- **R2** · assumed-same-repo

### Wave 3 — conditional
- Prerequisites:
  - R1 publishes P6 (cite: producer-evidence.v0 P6 publicationStatus = unpublished; stated, not verified live)
- Condition: awaiting publication of P6: producer-evidence.v0 declares P6 publicationStatus = unpublished — R10's wave can never be 'ready' while the sidecar says unpublished
- **R10** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | executable | producer-evidence.v0 | fixture-declared ×1 | produces P3 (delta target); unit = P1..P8, basis assumed-same-repo |
| R2 | affected | self | executable | package-evidence.v0 | declared ×1 | direct references to P1 and P3; the delta targets P3 |
| R10 | affected | self | executable | package-evidence.v0, producer-evidence.v0 | declared ×1 | direct reference to P6 (same release unit as the delta target — unit-level republish affects P6 consumers) |

## Uncertainty
None.

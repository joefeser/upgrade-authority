# Impact plan: P2 1.0.0 → 1.3.0

## Summary
- Delta: P2 updated 1.0.0 → 1.3.0 (nuget)
- Repositories: 3 affected · 0 not-affected · 0 unknown
- Waves: 3 (3 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites: none
- **R1** (publishes P2) · assumed-same-repo

### Wave 2 — ready
- Prerequisites:
  - R1 publishes P2 >= 1.3.0 (stated, not verified live)
- **R2** (publishes I1) · assumed-same-repo

### Wave 3 — ready
- Prerequisites:
  - R2 publishes I1 rebuilt against P2 1.3.0 (stated, not verified live)
- **R3** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | executable | producer-evidence.v0 | fixture-declared ×1 | produces P2 (delta target) |
| R2 | affected | self | executable | package-evidence.v0, producer-evidence.v0, lockfile-rows.v0 | declared ×2 | direct reference to P2 on the producer side (R2/src/Lib.csproj); produces I1 — R3's lockfile corroborates that I1's closure carries P2 (transitive via I1) |
| R3 | affected | self | executable | package-evidence.v0, lockfile-rows.v0 | declared ×1 | transitive path R3 -> I1 -> P2 reconstructed from R3's own lockfile (I1 direct; P2 transitive via I1) |

## Uncertainty
None.

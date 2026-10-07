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
- **R9** · assumed-same-repo
  - note: edit sites: Directory.Packages.props (central, fans out to ProjA/ProjB) AND ProjC VersionOverride (separate visible pin — not hidden by the central version)

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | executable | producer-evidence.v0 | fixture-declared ×1 | produces P1 (delta target) |
| R9 | affected | self | executable | package-evidence.v0 | declared ×1 | central package management: single pin edit in R9/Directory.Packages.props fans out to ProjA + ProjB; ProjC carries an explicit VersionOverride pinning P1 = 1.0.0 (R9/src/ProjC/ProjC.csproj) — the override is visible and must be updated or consciously kept |

## Uncertainty
None.

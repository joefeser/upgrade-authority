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
  - note: R3 scheduled in wave 3 on the ripple obligation; its P2 content-exposure stays a gap

### Wave 3 — ready
- Prerequisites:
  - R2 publishes I1 rebuilt against P2 1.3.0 (stated, not verified live)
- **R3** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | executable | producer-evidence.v0 | fixture-declared ×1 | produces P2 (delta target) |
| R2 | affected | self | executable | package-evidence.v0 | declared ×1 | direct reference to P2 (R2/src/Lib.csproj) |
| R3 | affected | self | executable | package-evidence.v0, producer-evidence.v0 | declared ×1 | consumes I1 produced by R2; R2 is affected — unit ripple: R3 must rebuild/bump to consume the republished I1; transitive CONTENT exposure to P2 via I1 remains UNKNOWN (no lockfile) — recorded as a gap, never as not-affected |

## Uncertainty

### Gaps
- **I1 -> P2 dependency edge**: R2's own reference to P2 is evidenced, but I1's manifest dependency on P2 is a future rung (nuspec-in-index); only R3's lockfile (absent here) could evidence the consumer-side path
- **R3 content exposure**: no lockfile-rows for R3; whether P2 itself flows into R3 via I1 is unresolved — ripple obligation stands (affected), content exposure stays unknown (gap), never not-affected

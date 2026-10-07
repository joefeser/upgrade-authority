# Impact plan: P2 1.0.0 → 1.6.0

## Summary
- Delta: P2 updated 1.0.0 → 1.6.0 (nuget)
- Repositories: 3 affected · 0 not-affected · 0 unknown
- Waves: 3 (1 ready, 2 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites: none
- **R1** (publishes P1, P2, P3, P4) · assumed-same-repo
  - note: current publication state cited from producer-evidence.v0 (fixture-declared)
  - publication: P1=published
  - publication: P2=unpublished
  - publication: P3=unpublished
  - publication: P4=unpublished

### Wave 2 — conditional
- Prerequisites:
  - R1 publishes P2 (cite: producer-evidence.v0 P2 publicationStatus = unpublished; stated, not verified live)
- Condition: awaiting publication of P2: producer-evidence.v0 declares P2 publicationStatus = unpublished — R2's wave can never be 'ready' while the sidecar says unpublished
- **R2** (publishes I1) · assumed-same-repo
  - note: R3 scheduled in wave 3 on the ripple obligation; its P2 content-exposure stays a gap

### Wave 3 — conditional
- Prerequisites:
  - R2 publishes I1 rebuilt against P2 1.6.0 (stated, not verified live)
- Condition: awaiting publication of P2: producer-evidence.v0 declares P2 publicationStatus = unpublished — R3's wave can never be 'ready' while the sidecar says unpublished
- **R3** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | executable | producer-evidence.v0 | fixture-declared ×1 | produces P2 (delta target); unit = P1..P4, basis assumed-same-repo |
| R2 | affected | self | executable | package-evidence.v0 | declared ×1 | direct reference to P2 (R2/src/Integration.csproj) |
| R3 | affected | self | executable | package-evidence.v0, producer-evidence.v0 | declared ×1 | consumes I1 produced by R2; R2 is affected — unit ripple: R3 must rebuild/bump to consume the republished I1; transitive CONTENT exposure to P2 via I1 remains UNKNOWN (no lockfile) — recorded as a gap, never as not-affected |

## Uncertainty

### Gaps
- **I1 -> P2 dependency edge**: R2's own reference to P2 is evidenced, but I1's manifest dependency on P2 is a future rung (nuspec-in-index); only R3's lockfile (absent here) could evidence the consumer-side path
- **R3 content exposure**: no lockfile-rows for R3; whether P2 itself flows into R3 via I1 is unresolved — ripple obligation stands (affected), content exposure stays unknown (gap), never not-affected

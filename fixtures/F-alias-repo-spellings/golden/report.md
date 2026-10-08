# Impact plan: P1 1.0.0 → 1.2.0

## Summary
- Delta: P1 updated 1.0.0 → 1.2.0 (nuget)
- Repositories: 3 affected · 1 not-affected · 1 unknown
- Waves: 3 (3 ready, 0 conditional, 0 provisional)

## Waves

### Wave 1 — ready
- Prerequisites: none
- **R1** (publishes P1, P2, P3, P4, P5, P6, P7, P8) · assumed-same-repo
  - note: shared compilation observed; shared-release NOT evidenced (no publication/versioning evidence per package)

### Wave 2 — ready
- Prerequisites:
  - R1 publishes P1 >= 1.2.0 (stated, not verified live)
- **R2** (publishes I1) · assumed-same-repo
  - note: R3 scheduled in wave 3 on the ripple obligation; its P1 content-exposure stays a gap

### Wave 3 — ready
- Prerequisites:
  - R2 publishes I1 rebuilt against P1 1.2.0 (stated, not verified live)
- **R3** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | executable | producer-evidence.v0 | fixture-declared ×1 | produces P1 (delta target) via producer-evidence.v0; release unit R1 = P1..P8 |
| R2 | affected | self | executable | package-evidence.v0 | declared ×1 | direct references to P1 and P3; the delta targets P1 |
| R3 | affected | self | executable | package-evidence.v0, producer-evidence.v0 | declared ×1 | consumes I1 produced by R2; R2 is affected — unit ripple: R3 must rebuild/bump to consume the republished I1; transitive CONTENT exposure to P1 via I1 remains UNKNOWN (no lockfile) — recorded as a gap, never as not-affected |
| r1 | unknown | unknown | unknown | package-evidence.v0 | declared ×1 | classification unknown: coverage gaps or unresolved exposure — never not-affected without positive evidence |
| RU | not-affected | self | none | package-evidence.v0, lockfile-rows.v0, scan-coverage | declared ×2 | positive evidence: complete scan coverage AND RU's own lockfile closure (X9 direct, no P1 row) — transitive exposure ruled out by lockfile evidence, not by absence-of-match |

## Uncertainty

### Gaps
- **I1 -> P1 dependency edge**: R2's own reference to P1 is evidenced, but I1's manifest dependency on P1 is a future rung (nuspec-in-index); only R3's lockfile (absent here) could evidence the consumer-side path
- **R3 content exposure**: no lockfile-rows for R3; whether P1 itself flows into R3 via I1 is unresolved — ripple obligation stands (affected), content exposure stays unknown (gap), never not-affected

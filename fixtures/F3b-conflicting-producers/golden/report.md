# Impact plan: P5 1.0.0 → 2.0.0

## Summary
- Delta: P5 updated 1.0.0 → 2.0.0 (nuget)
- Repositories: 3 affected · 0 not-affected · 0 unknown
- Waves: 2 (0 ready, 0 conditional, 2 provisional)

## Waves

### Wave 1 — provisional · blocked on C1
- Prerequisites: none
- **R1** (publishes P5) · assumed-same-repo
  - note: candidate producer — provisional pending C1 resolution
- **R6** (publishes P5) · assumed-same-repo
  - note: candidate producer — provisional pending C1 resolution

### Wave 2 — provisional · blocked on C1
- Prerequisites:
  - C1 resolved: authoritative producer of P5 determined by human decision; then P5 2.0.0 published (stated, not verified live)
- **R7** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R1 | affected | self | unknown | producer-evidence.v0 | fixture-declared ×1 | producer claim for P5 (claim A of contradiction C1) — BOTH claims retained; neither outranks the other for scheduling |
| R6 | affected | self | unknown | producer-evidence.v0 | fixture-declared ×1 | producer claim for P5 (claim B of contradiction C1) — BOTH claims retained |
| R7 | affected | self | unknown | package-evidence.v0 | declared ×1 | direct reference to P5 (R7/src/Downstream.csproj); downstream of a CONTRADICTED producer — appears only in a provisional wave blocked on C1; no firm prerequisite on either claim |

## Uncertainty

### Contradictions
- C1 (P5 producer): claims by R1 (P5), R6 (P5); downstream provisional: R7

# Impact plan: Bogus.Pin 1.0.0 → 2.0.0

## Summary
- Delta: Bogus.Pin updated 1.0.0 → 2.0.0 (nuget)
- Repositories: 3 affected · 0 not-affected · 0 unknown
- Waves: 2 (0 ready, 0 conditional, 2 provisional)

## Waves

### Wave 1 — provisional · blocked on C1
- Prerequisites: none
- **teamX** (publishes Bogus.Pin) · assumed-same-repo
  - note: candidate producer — provisional pending C1 resolution
- **teamY** (publishes Bogus.Pin) · assumed-same-repo
  - note: candidate producer — provisional pending C1 resolution

### Wave 2 — provisional · blocked on C1
- Prerequisites:
  - C1 resolved: authoritative producer of Bogus.Pin determined by human decision; then Bogus.Pin 2.0.0 published (stated, not verified live)
- **R1** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| teamX | affected | self | unknown | producer-evidence.v0 | fixture-declared ×1 | producer claim for Bogus.Pin (claim A of contradiction C1) — BOTH claims retained; neither outranks the other for scheduling |
| teamY | affected | self | unknown | producer-evidence.v0 | fixture-declared ×1 | producer claim for Bogus.Pin (claim B of contradiction C1) — BOTH claims retained |
| R1 | affected | self | unknown | package-evidence.v0 | declared ×1 | direct reference to Bogus.Pin (src/App.csproj); downstream of a CONTRADICTED producer — appears only in a provisional wave blocked on C1; no firm prerequisite on either claim |

## Uncertainty

### Contradictions
- C1 (Bogus.Pin producer): claims by teamX (Bogus.Pin), teamY (Bogus.Pin); downstream provisional: R1

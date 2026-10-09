# Impact plan: E1 1.0.0 → 1.5.0

## Summary
- Delta: E1 updated 1.0.0 → 1.5.0 (nuget)
- Repositories: 4 affected · 0 not-affected · 0 unknown
- Scope: 2 repos out of scope by config: archived, spikes
- Waves: 2 (0 ready, 2 conditional, 0 provisional)

## Waves

### Wave 1 — conditional
- Prerequisites:
  - E1 >= 1.5.0 available and restorable from the consumer environment (stated, not verified live)
- Condition: external team-b releases E1 >= 1.5.0 (request/await — outside our change authority; escalation path is the coordination step, not a PR)
- **R4** (publishes E1) · assumed-same-repo
  - note: external — request/await

### Wave 2 — conditional
- Prerequisites:
  - E1 >= 1.5.0 available and restorable from the consumer environment (stated, not verified live)
- Condition: external team-b releases E1 >= 1.5.0 (request/await — outside our change authority; escalation path is the coordination step, not a PR)
- **R3** · assumed-same-repo
- **RX** · assumed-same-repo
  - note: scheduled on the observed edge; unscanned area may surface additional work — tracked as a gap, not a blocker

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| R4 | affected | external | external-request-await | producer-evidence.v0, ownership.v0 | fixture-declared ×1 | produces E1 (delta target) via producer-evidence.v0; ownership.v0 declares R4 = team-b (external to team-a): rendered as coordination, NOT executable work for our team |
| R3 | affected | self | executable | package-evidence.v0 | declared ×1 | direct reference to E1 (R3/src/Api.csproj) |
| RX | affected | self | executable | package-evidence.v0, scan-coverage | declared ×1 | OBSERVED direct reference to E1 (RX/src/OtherTeamApi.csproj) — an observed reference is positive evidence of affected-ness; coverage gaps qualify HOW MUCH work is uncertain, they do not erase the observed edge |
| RM | affected | unknown | unknown | package-evidence.v0 | declared ×1 | OBSERVED direct reference to E1 (RM/src/Orphan.csproj); repo ABSENT from ownership.v0 — ownership unknown, never assumed ours; coordination target unidentified, so no executable action is scheduled |

## Uncertainty

### Gaps
- **RM ownership**: RM absent from ownership.v0 — ownership unknown (never 'ours'); RM is NOT scheduled into a wave until ownership is established
- **RX coverage**: scan gaps on RX/src/legacy-dir — RX still affected (observed edge) and scheduled; additional edges/work in the unscanned area remain unknown

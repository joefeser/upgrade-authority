# Impact plan: Bogus.Pin 1.0.0 → 2.0.0

## Summary
- Delta: Bogus.Pin updated 1.0.0 → 2.0.0 (nuget)
- Repositories: 2 affected · 0 not-affected · 0 unknown
- Waves: 2 (0 ready, 2 conditional, 0 provisional)

## Waves

### Wave 1 — conditional
- Prerequisites:
  - Bogus.Pin >= 2.0.0 available and restorable from the consumer environment (stated, not verified live)
- Condition: external team-b releases Bogus.Pin >= 2.0.0 (request/await — outside our change authority; escalation path is the coordination step, not a PR)
- **vendor** (publishes Bogus.Pin) · assumed-same-repo
  - note: external — request/await

### Wave 2 — conditional
- Prerequisites:
  - Bogus.Pin >= 2.0.0 available and restorable from the consumer environment (stated, not verified live)
- Condition: external team-b releases Bogus.Pin >= 2.0.0 (request/await — outside our change authority; escalation path is the coordination step, not a PR)
- **R1** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| vendor | affected | external | external-request-await | producer-evidence.v0, ownership.v0 | fixture-declared ×1 | produces Bogus.Pin (delta target) via producer-evidence.v0; ownership.v0 declares vendor = team-b (external to team-a): rendered as coordination, NOT executable work for our team |
| R1 | affected | self | executable | package-evidence.v0 | declared ×1 | direct reference to Bogus.Pin (src/App.csproj) |

## Uncertainty
None.

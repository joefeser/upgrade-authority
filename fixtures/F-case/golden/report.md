# Impact plan: NEWTONSOFT.JSON 12.0.3 → 13.0.3

## Summary
- Delta: NEWTONSOFT.JSON updated 12.0.3 → 13.0.3 (nuget)
- Repositories: 2 affected · 0 not-affected · 0 unknown
- Waves: 1 (0 ready, 1 conditional, 0 provisional)

## Waves

### Wave 1 — conditional
- Prerequisites:
  - NEWTONSOFT.JSON 13.0.3 exists on the configured feed and authenticated restore succeeds (stated, not verified live)
- Condition: producer of NEWTONSOFT.JSON is unknown — no producer wave is derivable; caseA's wave proceeds only after the producer is identified and NEWTONSOFT.JSON 13.0.3 is available
- **caseA** · assumed-same-repo
- **caseB** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| caseA | affected | self | unknown | package-evidence.v0 | declared ×1 | direct references to Newtonsoft.Json and Serilog; the delta targets NEWTONSOFT.JSON; no producer wave possible: producer of NEWTONSOFT.JSON unknown |
| caseB | affected | self | unknown | package-evidence.v0 | declared ×1 | direct references to Serilog and newtonsoft.json; the delta targets NEWTONSOFT.JSON; no producer wave possible: producer of NEWTONSOFT.JSON unknown |

## Uncertainty

### Gaps
- **NEWTONSOFT.JSON producer**: no pinned input declares who produces NEWTONSOFT.JSON; zero producers invented (no heuristic, no name inference)

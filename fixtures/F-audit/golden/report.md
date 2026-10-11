# Impact plan: Serilog 3.1.1 → 4.0.0

## Summary
- Delta: Serilog updated 3.1.1 → 4.0.0 (nuget)
- Repositories: 1 affected · 1 not-affected · 3 unknown
- Waves: 1 (0 ready, 1 conditional, 0 provisional)

## Waves

### Wave 1 — conditional
- Prerequisites:
  - Serilog 4.0.0 exists on the configured feed and authenticated restore succeeds (stated, not verified live)
- Condition: producer of Serilog is unknown — no producer wave is derivable; appC's wave proceeds only after the producer is identified and Serilog 4.0.0 is available
- **appC** · assumed-same-repo

## Repositories
| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |
|---|---|---|---|---|---|---|
| appC | affected | self | unknown | package-evidence.v0 | declared ×1 | direct reference to Serilog (src/App.csproj) — affected-ness is consumer-side and fully evidenced; no producer wave possible: producer of Serilog unknown |
| appA | unknown | self | unknown | package-evidence.v0 | declared ×1 | classification unknown: coverage gaps or unresolved exposure — never not-affected without positive evidence |
| corelib | unknown | self | unknown | package-evidence.v0 | declared ×1 | classification unknown: coverage gaps or unresolved exposure — never not-affected without positive evidence |
| shared | unknown | unknown | unknown | package-evidence.v0 | declared ×1 | classification unknown: coverage gaps or unresolved exposure — never not-affected without positive evidence |
| appB | not-affected | self | none | package-evidence.v0, lockfile-rows.v2, scan-coverage | declared ×2 | positive evidence: complete scan coverage AND appB's own lockfile closure (Newtonsoft.Json direct, no Serilog row) — transitive exposure ruled out by lockfile evidence, not by absence-of-match |

## Uncertainty

### Gaps
- **Serilog producer**: no pinned input declares who produces Serilog; zero producers invented (no heuristic, no name inference)

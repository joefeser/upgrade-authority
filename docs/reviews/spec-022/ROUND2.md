# SPEC-022 round 2 — delta verification + verdict (2026-10-10)

**Reviewer:** verification stand-in (delta brief; verified every fresh-round-1 finding against v3 AND the code sites that must produce the claimed shapes).

## Verdict: SHIP

All majors verified landed:
- **Lockfile tier**: `RestorePackagesWithLockFile=true` stated; stage 4 asserts `"evidence": "lockfile"` (matches `Drift.cs` L33/200/405).
- **Diesel-delta run**: verified in `Planner.cs` that the GIVEN estate actually fires `externalTarget` (L95/L245), `externalProducer` (L248/L153), **T5** (L387/L946), **T6/T6a** (L349/L381/L388), **`external-request-await`** (L785-790), and Booker affected (L257).
- **Scratch-commit honesty**: exact contents stated, lockfile tracked-in-scratch/gitignored-in-repo resolved, the identical-SHA claim dropped and inverted with the contentHash rationale.
- **ownership-init**: exact command with a gitignored `--out`; the hand-authored cross-team sidecar justified (init's single-team modes, verified at `Program.cs` L86-97).

All minors verified (TZ-pinned dates + `finally` + `-c` identity; anchored ignores; the scan-stage lockfile guard; Diesel/Booker versions stated). Zero new blockers; the anomaly wording re-verified against `Registry.cs`. One non-blocking nuance logged (the "does NOT yield an identical SHA" shorthand under-claims rather than over-claims — harmless).

Zero open blockers/majors — doctrine §2 convergence. Proceeds to implementation.

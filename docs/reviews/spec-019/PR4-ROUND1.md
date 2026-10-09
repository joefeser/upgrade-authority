# SPEC-019 implementation PR #4 — external round 1 (2026-10-09)

**Reviewers:** Codex (4 inline: 2×P1, 2×P2); Baz pending at triage time.

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C1 | The affected closure's rule-(c) entry and `ConsumersOf` read ALL lockfile rows — a stale deps.json row carrying the target scheduled work AND `BuildRepo`'s `LockProvable(...).First(...)` threw on the filtered set | **P1** | **Accepted** | Both entry points gate on `LockProvable` (stale build output never schedules); crash-path pinned (facts-stripped scratch + stale + target-on-the-row ⇒ unknown, unscheduled, no throw) |
| C2 | Worktrees were created in the sequential pass (N satellites regardless of `--parallel`) and several exits skipped removal (cache-hit, missing-tracemap, build-failure, worker-exception) | **P1** | **Accepted** | The satellite is minted INSIDE the bounded worker (the sha needs only `rev-parse origin/<trunk>` — no satellite); removal in a `finally` on every exit path |
| C3 | In-place `--build --index-deps-json` could reuse a pre-build cache match and skip the requested build | P2 | **Accepted** | `--build` never reuses — an explicit freshness-seeking action always executes |
| C4 | Freshness keys stored verbatim before the mirror map loaded — alias-keyed entries missed, fresh evidence demoted | P2 | **Accepted** | Keys normalize through `Norm` once mirrors are loaded (same identity as every input) |

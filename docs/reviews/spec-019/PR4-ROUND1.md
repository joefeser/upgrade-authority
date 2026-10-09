# SPEC-019 implementation PR #4 — external round 1 (2026-10-09)

**Reviewers:** Codex (4 inline: 2×P1, 2×P2); Baz pending at triage time.

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C1 | The affected closure's rule-(c) entry and `ConsumersOf` read ALL lockfile rows — a stale deps.json row carrying the target scheduled work AND `BuildRepo`'s `LockProvable(...).First(...)` threw on the filtered set | **P1** | **Accepted** | Both entry points gate on `LockProvable` (stale build output never schedules); crash-path pinned (facts-stripped scratch + stale + target-on-the-row ⇒ unknown, unscheduled, no throw) |
| C2 | Worktrees were created in the sequential pass (N satellites regardless of `--parallel`) and several exits skipped removal (cache-hit, missing-tracemap, build-failure, worker-exception) | **P1** | **Accepted** | The satellite is minted INSIDE the bounded worker (the sha needs only `rev-parse origin/<trunk>` — no satellite); removal in a `finally` on every exit path |
| C3 | In-place `--build --index-deps-json` could reuse a pre-build cache match and skip the requested build | P2 | **Accepted** | `--build` never reuses — an explicit freshness-seeking action always executes |
| C4 | Freshness keys stored verbatim before the mirror map loaded — alias-keyed entries missed, fresh evidence demoted | P2 | **Accepted** | Keys normalize through `Norm` once mirrors are loaded (same identity as every input) |

## Round 2 (2026-10-09)

Baz confirmed 6 round-1 items in-thread. New findings:

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C5 | The worker's build/scan targeted the `PENDING` planning marker, not the minted satellite (real-runner worktree scans would hit a nonexistent path; the stub hid it) | **P1** | **Accepted** | `satellite` is the one true path (scanTarget, build, fingerprint) |
| C6 | Freshness staging still ran before the mirror loop; alias keys missed | P2 | **Accepted** | Applied AFTER mirrors; stored under BOTH the raw RepoKey (lockfile rows are verbatim-keyed) and `Norm` |
| Baz | `repos:null` / `[null]` freshness shapes crash instead of typed | medium | Accepted | Typed UaException at load |
| Baz | Non-object `scan-estate.json` crashes ingest's freshness assembly | medium | Accepted | `ValueKind == Object` guard |
| Baz | Freshness keys case-variant vs lockfile keys | medium | Accepted (folded into C6's dual-key store) | — |
| Baz | Rescue branch used 7-char sha | medium | Accepted | 8 chars |
| Baz | Rescue appended a synthetic skip + a scanned entry (duplicate repos[]) | medium | Accepted | ONE canonical entry; rescue provenance rides its `reason` |
| Baz | DepsManifests traversed repeatedly and unboundedly | medium | Accepted | Single bounded enumeration per call (cap 2048, warned, conservative) |
| Baz | Pattern-matching foreign dirs under ua/ could be swept | medium | **Accepted (hardening)** | Sweep is OWNERSHIP-CHECKED: only dirs git itself lists as this repo's worktrees |
| Baz | Optional-parameter signature changes break compiled callers | medium | **Dismissed (receipt)** | Self-contained console app; all callers in-tree (same receipt as PR #2 round 1) |

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

## Round 3 (2026-10-09)

Baz confirmed all round-2 fixes in-thread (10 addressed replies). New findings:

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C7 + Baz | A truncated manifest set could still certify fresh / falsely reuse (the discarded suffix might hold the oldest file) | **P1** | **Accepted** | Truncation fails closed: freshness ⇒ `stale`; fingerprint ⇒ `"!truncated"` sentinel (never equals a real hash) |
| C8 | `Take()` bounded matches, not the WALK — a hostile tree still cost unbounded traversal | P2 | **Accepted** | Manual bounded walk (dir budget 50k, `.git`/`node_modules`/`.vs`/`packages` skipped, in-bin flag) |
| C9 | Rescue provenance lost on reuse/failure terminal states | P2 | **Accepted** | Applied once at the durable manifest write — every terminal state carries the branch name |
| Baz | Conflicting duplicate freshness entries silently last-wins | medium | **Accepted** | Identical duplicates collapse; conflicts refuse at load (the file contradicts itself) |

## Round 4 (2026-10-09)

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C10 | Alias+canonical freshness entries with different values became last-wins after normalization | **P1** | **Accepted** | The dual-key store REFUSES conflicting assignments to one resolved repo (UaException) |
| C11 | An unreadable subtree silently skipped without `truncated` — partial set could certify fresh | **P1** | **Accepted** | Discovery failure sets `truncated` (fail closed, like manifest overflow) |
| C12 | `"!truncated"` sentinel matched itself across runs ⇒ obsolete-scan reuse | P2 | **Accepted** | Reuse explicitly disabled when either side carries the sentinel |
| C13 + Baz | `GetFileSystemEntries` materialized wide dirs eagerly; budget checked after | P2 | **Accepted** | Lazy `EnumerateFileSystemEntries` + a total entry budget (500k), truncated on exceed |
| C14 | Rescue provenance lost on the missing-`--tracemap` precondition exit | P2 | **Accepted** | `WriteManifest()` local function is the single durable-write site — called on that exit too |
| Baz | Nested symlinks could feed outside-the-repo deps.json as evidence | medium | **Accepted** | Reparse points are never followed in the walk |

## Round 5 (2026-10-09)

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C15 | Real worktree scans record the satellite leaf as repo identity ⇒ ingest freshness never matches + repo identity churns per rescan | **P1** | **Accepted** | Post-scan normalization (worktree mode only): the satellite leaf in facts/manifest `repo`/`repoName` is OUR orchestration artifact, not repo identity — restored to the canonical name before the swap |
| C16 | Lazy enumeration throws from `MoveNext` (not creation) — outside the catch, aborting the estate | **P1** | **Accepted** | `SafeIterate` wrapper: mid-iteration failure marks discovery truncated (fail closed) |
| C17 | A stale deps row naming the target demoted repos with an independent lockfile closure — contradicts spec 19-5 | P2 | **Accepted** | `touches` scopes stale-row suspicion to repos with NO lockfile rows (checked-in proof stands; pinned by a new acceptance: lockfile-without-target + stale-deps-with-target ⇒ not-affected) |

## Round 6 (2026-10-09)

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C18 | Identity normalization used string replace on spaced JSON — real tracemap facts are COMPACT; satellite name survived | **P1** | **Accepted** | Parse-and-rewrite (per-line JsonNode for facts; whole-doc for the manifest); unparseable lines stand as scanned |
| C19 | SafeIterate leaked its manually owned enumerator; acquisition failures bypassed markTruncated | medium | **Accepted** | try/finally disposal on every exit; acquisition routed through markTruncated |

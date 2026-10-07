# PR #4 round 1 triage — V0 planner (2026-10-02)

**Quorum batch (one consolidated pass per doctrine §5):** Codex 9 (6× P1, 3× P2) · Qodo 20 (7 High, 12 Medium/Low, 1 Informational). Raw: [quorum-inline-raw.md](quorum-inline-raw.md). **All 29 accepted; none rejected.** Convergence: both reviewers independently found the cycle-detector defects (first-edge-only walk, reversed edge lookup, 2-repo-only detail) and the declaration-order dependencies.

| Theme | Findings | Fix |
|---|---|---|
| Repo-identity normalization never coded | Codex P1-1, Qodo 14 | Normalize (lowercase host, strip `.git`/trailing slash) before every repo-keyed map |
| Cycle detector: first-edge-only walk, reversed EdgePkg, 2-repo detail, non-participant crash, path rotation, DepthOf overflow | Codex P1-2, Qodo 1, 5, 6, 12, 13 | Full DFS with white/grey/black coloring over ALL edges, deterministic order; path rotated to target producer; EdgePkg reads consumer's deps; detail built from every hop; CyclePlan handles consumer-only/non-participant repos; DepthOf in-progress guard throws UaException |
| Conditional gates don't propagate downstream | Codex P1-4 | Status computed transitively: worst-of (self, upstream deps); rank-first grouping then never orders ready before its own gate |
| Ripple evidence from declaration order (depsForD) | Codex P1-5, Qodo 2, 8 | Causative (producer, pkg) edges retained per repo during closure; rule-d recorded for every qualifying edge (d-reasons deduped when the same package already produced an a/b/c reason — preserves goldens); gaps use the causative edge |
| Publication status keyed by pkg only (last-write-wins) | Codex P1-3 | Keyed by (repo, pkg); contradiction claimants carry their own statuses |
| Versioning note hard-codes F1b's values; 2-pkg crash | Codex P2, Qodo 7 | Groups derived from actual ProducedVersion values (dominant group = "{range} = {major}.x"; others = "{pkgs} = independent {version} line"); no Range on empty groups |
| Declaration-order outputs (projects, gaps, lock rows, produced lists) | Qodo 10, 11, 16 | All input collections normalized at construction (natural/ordinal sorts); permutation selftest runs on EVERY fixture |
| Natural comparer: int overflow, case sensitivity | Codex P2, Qodo 15 | Length-normalized digit-run comparison (no int.Parse), case-insensitive (matches JS base sensitivity) |
| Canonical writer: CRLF on Windows, \b/\f/0x7F-9F escaping | Codex P2, Qodo 4, 18 | AppendLine → explicit '\n'; .gitattributes LF for fixtures; \b \f escapes; escape only < 0x20 + lone surrogates; selftest asserts no '\r' |
| Unknown-classification repos dropped from repos[] | Codex P1-3(buckets) | Ordering bucket appended (after ownership-unknown, before not-affected) |
| CPM assumes central+override pair; unsorted projects | Qodo 9, 10 | Each shape rendered if present, independently; projects sorted |
| Lockfile path evidence names wrong parent | Qodo 11 | Direct parent found via the transitive row's `via` |
| Ripple note overwrites notes, guesses wave, may name unscheduled repo | Qodo 17 | Units built in a second pass with real wave indices; note appended, not replacing; skipped for unscheduled downstream |
| Windows stdout mangles non-ASCII | Qodo 19 | UTF-8 output encoding |
| Non-UaException crashes | Qodo 20 | JsonException → UaException naming file; catch-all one-liner; FindRepoRoot throws UaException |

**New acceptance fixtures (hand-authored goldens, fidelity rule):** F-cyc2 (3-repo cycle, rotated path, all-hop detail), F-alias (equivalent repo spellings merge), F-gate (conditional propagation through downstream consumer). Central-only CPM noted for round 2 if time-boxed.

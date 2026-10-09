# SPEC-017 round 1 — triage (2026-10-08)

**Quorum record (doctrine §5):** Codex, Baz, Qodo, Sourcery CLIs are not installed in this
environment; no external verifier was available this round. Per the doctrine's no-verifier clause,
the round was run as **coordinator + two independent adversarial reviewer stand-ins** (full round-1
"fight" brief + verification-focused brief, both with repo access). Joe can re-run the external
quorum on the PR; the brief is this file plus the spec.

**Verdicts:** Reviewer A (adversarial): **FIX THEN SHIP** (1 blocker, 7 majors, 9 minors, 2 questions).
Reviewer B (verifier): **FIX THEN SHIP** (1 blocker, 5 majors, 9 minors, 1 question).
Cross-reviewer convergence on the load-bearing findings (F2 contradiction, seam undefined,
manifest ordering, cache-key honesty) is itself signal.

| # | Finding (source) | Severity | Disposition | Fix applied |
|---|---|---|---|---|
| A1 | Explicit-dirs ingest vs alternate snapshots: two clones of one repo collide at ingest exit 4; equivalence AC unachievable on that estate shape | **Blocker** | Accepted | §4.2 alternate-checkout dedupe BEFORE scanning (normalized origin URL — every F2-passing repo has one); §7.1 pins the comparator (manual = explicit name-ordinal ingest of the same fresh set); §7.5 adds the two-clones case |
| A2/B1 | F2 (no origin) specified two contradictory ways under `--allow-stale` | **Blocker** (B: major) | Accepted | §4.1: `--allow-stale` waives F3–F6 only; F1/F2 never bypassable, with the rationale stated; §7.3 adds the no-origin+allow-stale case |
| A3 | Scanner stub seam never defined; AC1/AC2 lean on it | Major | Accepted | §4.2 seam contract: internal overridable delegate `(repoPath, scanOutDir, headSha, excludes) → exit`, argv composition a separate pure helper; stub must echo the real HEAD sha (else reuse can't fire) |
| A4 | Cache key SHA-only: `--exclude` changes and tracemap upgrades serve stale scans silently | Major | Accepted | §4.2 cache metadata `scan-estate-cache.v1` (`exclude`, `tracemapSha256`, `staleScan`); reuse requires all to match; §7.2 covers both |
| A5 | Dirty `--allow-stale` scans poison the cache permanently, no marker | Major | Accepted | `staleScan` flag in metadata; clean re-run at same SHA rescans; §7.2 case |
| A6 | Scope-by-name defeats alternate-snapshot detection (exclude svc ⇒ svc-net48 sneaks in) | Major | Accepted | §3: excluded children still register repo names for alternate detection; §7.5 committed-scans case (ingest + ownership) |
| A7 | Include-mode complement unnameable in report vs "naming them" claim | Major | Accepted (resolution: layered honesty) | §3/§5: report states mode + include count + exclude names; observed out-of-scope names live in scan-estate.v1.json (the surface that sees the universe); §7.5 include-mode case |
| A8/B6 | Producer template packageId unbound when delta.json exists and no flags (the flagship flow) | Major | Accepted | §4.3: source = `--delta-package` else existing delta's `changes[0].packageName`; disagreement/missing ⇒ typed errors; ≠1-change delta refused at bootstrap; §7.6 case |
| B2/B3 | Manifest written after bootstrap contradicts survival claim; empty-set path runs bootstrap over zero dirs (Ownership.Init fails silently) | Major | Accepted | §4.4 reorder: loop → manifest → empty-set check → bootstrap; message names the status composition; no sidecars on that path; §7.7 case |
| B5 | `.tmp`/`.old` swap leftovers are discoverable by `--scans-root` (no dot-skip in SPEC-011 discovery) | Major | Accepted | §4.2 clears leftovers before swap; §3/§4.4: ingest + ownership discovery skip dot-prefixed children; §7.4 planted-leftover case |
| B7/A11 | `include:[]` ambiguity; both-arrays-empty vs no-scope bytes | Minor | Accepted | §3: empty/absent arrays dropped at validation; validates-to-nothing = no scope field/line; `include:[]`+exclude ⇒ typed error; §7.5 |
| B8 | Permutation invariance of scope echo unstated (accident, not criterion) | Minor | Accepted | §5: dedup+ordinal at echo, `includedCount` = config's deduped count; §7.7 names the permutation case |
| A9/B9 | "Swaps atomically" not machine-checkable | Minor | Accepted | §7.4 rewritten as three checkable proxies (no residue, previous cache intact on failure, facts-without-manifest warn) |
| A10 | Broken cross-refs (§7/§8.8) | Minor | Accepted | Fixed |
| B10/A14b | Bare trailing `--out` silently falls back to stdout | Minor | Accepted | §2: every value flag requires a value ⇒ typed error; §7.8 case |
| B11 | Scope-file edges: missing file, stray copy in scan dir, flag-vs-input conflict, exit codes | Minor | Accepted | §3: missing ⇒ exit 1; bad JSON/schemaVersion ⇒ exit 5 (ingest/ownership), exit 3 at load; stray scan-dir copy ignored with warning; flag bytes win; one shared validator; §7.9 |
| B12/A12 | AC1 methodology underdefined (runbook Out-File writes a BOM) | Minor | Accepted | §7.1 pins in-process/manual comparator + child-capture for --out; §6 notes the BOM caveat |
| B13 | `ownership update` refuses `--self` — mismatch check must be scan-estate's own | Minor | Accepted | §4.3 sentence added (read selfTeamId, compare, invoke update without --self); init/update over explicit dirs |
| B14/A14 | PS 5.1: synopsis not paste-safe; `--exclude` name collision | Minor | Accepted | §2 marks grammar vs script, verified one-liners promised in runbook, `--exclude` = tracemap pass-through stated, repeatable parsing stated |
| B15/A13/A17 | Misc: file children, git-missing preflight, fetch-reason trim rule, F6 untracked wording, manifest schemaVersion literal | Minor | Accepted | §4: non-directory children not enumerated; `git --version` preflight; first-stderr-line-capped-200 rule; F6 reason names untracked + escape; literal `scan-estate.v1` |
| A15 | Scope line placement vs Version-disagreements line | Minor | Accepted | §5: directly after Repositories, before disagreements |
| A16/B-none | Canonical.cs missing from lockstep list; scope key order unpinned | Minor | Accepted | §5: fixed key order `["mode","includedCount","outOfScope"]`; Canonical.cs first in the lockstep list |
| A18 | master-trunk estates? | Question | Answered in spec | §4.1 F4 + §8: visibly skipped with reason; preference config waits for a real need |
| A19 | Detached/feature checkout at trunk tip passes F5 | Question | Answered in spec | §4.1 F5: content identity (SHA equality) — stated as intended |

**Not accepted / deferred:** none rejected.

**Round 2 scope (delta review):** verify the blocker/major fixes landed as described; no full re-review.

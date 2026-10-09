# SPEC-018 implementation PR #3 — external round 1 triage (2026-10-09)

**Reviewers:** Codex (5 inline: 1×P1, 4×P2) + Baz (5 inline). Both finding sets landed within minutes
of the push; Baz's check passed while carrying inline findings (the known pattern).

| # | Finding (source) | Severity | Disposition | Fix |
|---|---|---|---|---|
| C1 | Even with the estate spelling emitted, plan/apply compare package ids ORDINALLY against the delta target — a case-variant estate can miss affected facts/edit sites/producer | P1 | **Deferred with receipt** | Pre-existing planner identity policy, not a drift behavior: a HAND-written delta carries the identical hazard (the estate-spelling rule is drift's mitigation — maximal coverage). Teaching planner/apply NuGet case-insensitivity changes SPEC-002/003 matching and is queued as its own slice (spec §9 records it). Receipt: `Planner.cs` target matching was ordinal before SPEC-018 existed; SPEC-007 already treats case-variant ids within one resolution group as a typed conflict, so the hazard needs cross-repo variants |
| C2 | `--emit-deltas` into an existing dir leaves stale candidates beside (or instead of) the new set | P2 | **Accepted** | Non-empty destination ⇒ typed error exit 3 (clear it or pass a fresh dir); every candidate precomputed/validated before the destination is touched |
| C3 + Baz | `packages: null` and `[null]` feed shapes NRE (exit 4) instead of the documented exit 5 | P2 | **Accepted** | Both shapes typed-rejected in `Validate` (5); pinned in selftest |
| C4 | Case-variant SAME-version folder files (`Foo.1.0.0` + `foo.1.0.0.nupkg`) refused as a contradiction | P2 | **Accepted** | Identical `(id, version)` collapses in the folder form too (matching the file form's idempotence); pinned |
| C5 + Baz | Bare/dangling `-o` accepted on drift (only `--out` was in the preflight) | P2 | **Accepted** | `-o` added to the value-flag preflight; bare `-o` pinned |
| Baz | Partial writes before collision discovery; pre-existing destination overwritten | medium | **Accepted** | Folded into C2's precompute + refuse-non-empty rework — a refusal never leaves partial or overwritten output |
| Baz | Control chars in feed fields/filenames forge stderr line structure | medium | **Accepted (hardening)** | `Esc()` renders control chars visibly in every untrusted diagnostic interpolation |
| Baz | input-schemas claims validator drift canonical-byte checks; validator had structure only | medium | **Accepted** | `toDriftCanonicalText` added to `validate-fixtures.mjs` (fixed key order mirroring `Drift.Write`); drift goldens byte-checked |

**Result:** 7 fixed, 1 deferred with receipt. Selftest stays 124 (case extensions); goldens unchanged
(25); the emitted-delta goldens are byte-identical (the emit rework changed ordering of validation,
not bytes). Spec §3/§7/§9 updated to match.


## Round 2 (2026-10-09)

Baz confirmed all round-1 fixes in-thread (9 addressed replies, check pass). New findings:

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C6 | The no-behind early return skipped the non-empty-destination check — an all-current rerun left stale candidates in place | P2 | **Accepted** | Destination check moved BEFORE the early return (spec §7 wording extended); pinned (all-current rerun into a populated dir exits 3) |
| C7 | Esc gaps: conflict diagnostics wrote versions raw; folder-conflict wrote filenames raw; null schemaVersion/source NRE'd inside Esc | P2 | **Accepted** | Esc applied to every untrusted interpolation (versions, filenames); null metadata coalesced; typed exit 5 pinned for null schemaVersion/source and case-variant-two-versions |
| Baz | Validator's ord() fallback preserved unknown drift keys (out-of-shape goldens could pass) | medium | **Accepted** | ord is STRICT (unknown keys dropped from the rendering ⇒ byte-compare fails); comment records why |
| Baz | Unbounded staged memory in EmitDeltas | medium | **Dismissed (receipt)** | The tool is in-memory by design (Engine loads every fact/lockfile row before drift runs); staged candidates are strictly smaller than the already-loaded inventory. An arbitrary cap would be the first memory limit in a codebase that deliberately has none; estate-scale memory is the design-talk tier |


## Round 3–4 + CONVERGENCE (2026-10-09, head 291383a)

| # | Finding | Severity | Disposition | Fix |
|---|---|---|---|---|
| C8 (r3) | Feed file's own basename interpolated raw in validation diagnostics | P2 | Accepted | `displayName` passes through `Esc` at `Validate` entry |
| C9 (r4) | The invalid-JSON branch (runs before Validate) still had the raw basename | P2 | Accepted | That branch escapes its basename too — the last raw untrusted feed value |
| Baz (r4) | Returned report's per-package `Statuses` map empty (only `Write` computed counts) — in-process API inconsistency | medium | Accepted | `Build` populates all four counts from the final deduplicated installations |

**Round 5: Codex delta review of 291383a completed with ZERO new findings; Baz confirmed rounds 3–4
in-thread with passing checks.** Finding decay 10 → 5 → 4 → 2 → **0 open** — every finding fixed or
dismissed with a receipt. Per doctrine §2 the implementation review is converged. PR #3 awaits Joe's
work-machine verification → ONE merge to public main → ONE sync to dev.

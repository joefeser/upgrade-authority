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

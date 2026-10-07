# PR #11 fidelity notes (SPEC-008 implementation)

## Goldens
- F12 golden hand-authored FIRST from the spec rules, then diffed against the
  implementation: byte-identical on first diff (plan + report). F12's input is
  REAL `ua ingest` output from the committed billing+shipping scans with
  sidecars-estate2 — not hand-typed.
- All 19 pre-existing fixtures: byte-identical (v0/v1 load paths untouched;
  zero GOLDEN-CHANGES). F9/F10/F11 input files stay v1 — frozen accepted shapes.

## Behavior changes (all per spec)
- Ingest emits lockfile-rows.v2 (repos[] envelope) whenever lockfile facts
  exist; exit 2 (multi-repo refusal) retired; lockfile-less runs still emit no
  lockfile file.
- The svc e2e selftest now expects `lockfile-rows.v2` provenance (spec §7.7);
  F9's committed input remains v1 (load-compat case, kept deliberately).

## Acceptance evidence
- `ua selftest`: 50 cases PASS — 20 fixtures × (golden + permutation), plus
  multi-change, v0-dup, v1-conflict (ingest+load), case-variant (ingest+load),
  svc e2e, estate2 e2e, mixed-run, v2-duplicate-repo, both-lockfile-files.
- `node tools/validate-fixtures.mjs`: 20/20 (v2-aware provenance check).

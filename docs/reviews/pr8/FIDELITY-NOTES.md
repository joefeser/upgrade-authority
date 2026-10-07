# PR #8 fidelity notes (SPEC-007 implementation)

## Spec amendment (recorded per the acceptance-fidelity rule, Session 12 §7.1)

**SPEC-007 §9.3/§9.4 amendment — null-version case moved from F10 to F11.**
The round-2-approved spec text placed F10's null-`version` row on the same repo
that must classify `not-affected`. Those two assertions cannot coexist: the
null row's `resolved version not evidenced` coverage gap (added per Codex R2-1)
forces coverage status `gaps`, and `not-affected` requires `complete` coverage
(SPEC-003 §2). Surfaced by building the implementation — the engine is itself a
review pass. Resolution: F10 keeps agreement + not-affected (complete coverage);
the null-version row lives in F11 (already `affected` via rule c). Spec text
amended in-tree with this note as the recorded reason.

## Goldens

- F9/F10/F11 goldens hand-authored FIRST from the spec rules (templates + SPEC-003
  ordering), then diffed against the implementation: byte-identical on first diff.
- F9's input is REAL `ua ingest` output from Kiro's committed svc scan
  (`testdata-ingest/tracemap-rich/scans/svc`) — not a hand-typed approximation.
- All 16 pre-existing fixtures: byte-identical plan.json and report.md (no
  GOLDEN-CHANGES entries; the contract change is purely additive).

## Acceptance evidence

- `ua selftest`: 42 cases PASS (19 fixtures × golden + permutation, multi-change
  rejection, v0-duplicate rejection, v1-conflict rejection, ingest→plan→report
  end-to-end on the committed real scan).
- `node tools/validate-fixtures.mjs`: 19/19 (structural + one-template mapping
  incl. findings T10/T10a + evidence-kind provenance + canonical bytes).

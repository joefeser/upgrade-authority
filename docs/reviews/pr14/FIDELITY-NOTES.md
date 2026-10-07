# PR #14 fidelity notes (SPEC-009 implementation)

## Goldens
- F13 plan/report: hand-authored from the rules first; two authoring slips found by the diff and
  corrected TO THE RULES (Bare() keeps the `.csproj` suffix; coverage gaps sort before
  FirstOrDefault). apply.v1 + wave-1.patch goldens captured after byte-checking the rule-derived
  manifest; the patch was additionally verified with real `git apply` against a scratch copy.
- F9/F12 inputs regenerated with the §3a evidence (line/endLine spans, CPM pin facts with
  correlated fan-out Projects, VersionOverride constraint source): **plan/report goldens
  byte-identical** (acceptance 4 proven by the corpus run). F9's lockfile input stays v1
  deliberately (frozen load-compat case per SPEC-008); F12's stays v2.
- Ingest honesty fix en route: constraint-not-evidenced no longer fires for override-carrying
  references (the override IS the evidenced constraint).

## Acceptance evidence
- `ua selftest`: **58 cases PASS** — 21 fixtures × (golden + permutation), 10 typed-refusal/e2e
  cases, 6 apply cases (F13 goldens, already-satisfied N4 + empty patch + no downgrade,
  stale refusal exit 6, ambiguous refusal exit 6, transitive N1, gated waves verbatim).

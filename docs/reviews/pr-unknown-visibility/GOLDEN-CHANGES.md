# GOLDEN-CHANGES — unknown-repo visibility (SPEC-003 rev 3)

**Fixture:** SPEC-003 rev 3 amendment. Classification-`unknown` repos now appear in `repos[]`
(previously silently dropped — found on the real estate 2026-10-08: `ingested: … 130 repos`
produced a plan with 18 repos and a **false `0 unknown` summary**; ~112 repos lacked lockfile
evidence → unknown → invisible).

Every changed line adjudicated before capture:

## F-alias-repo-spellings
- `r1` (alias key with no facts/coverage/edge) **added** to `repos[]` as
  `classification: unknown`, `ownership: unknown`, `actionType: unknown`, evidence
  `package-evidence.v0`, declared ×1. Summary `0 unknown → 1 unknown`; report table gains the row.
  Adjudication: correct — r1 was always unknown-classified; the old golden hid it.

## F9-multi-lockfile-disagreement
- `corelib` **added** as unknown (producer-sidecar-referenced, no scan coverage in this fixture,
  no observed edge). Summary `0 → 2 unknown`.
- `legacy` **added** as unknown (ownership self, no scan, no edge).
  Adjudication: correct — both were sidecar-referenced-but-unscanned repos the old golden silently
  dropped; visible-unknown with the honest reason string is the spec's intent.

No other lines changed. No template changes. Canonicalizer + validator re-run green (23/23);
selftest 99/99 (new case: `unknown-repo-visibility`, svc+legacy ingest, asserts repos[] presence,
never-scheduled, and the summary count).

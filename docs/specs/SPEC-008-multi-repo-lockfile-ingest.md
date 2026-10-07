# SPEC-008 — Multi-repo lockfile ingest

**Status:** draft — round 1 review (brief-011, depends on PR #10 evidence)
**Author:** ZCode (coordinator)
**Date:** 2026-10-03
**Depends on:** SPEC-007 (v1 rows, findings, provenance) · SPEC-005 (ingest) · committed real scans `testdata-ingest/tracemap-rich/scans/{billing,shipping}` (PR #10)

## 1. Purpose

Remove the last ingest limitation: `ua ingest` refuses (typed exit 2) when more than one repo in a run carries lockfile facts. Estate-scale runs need every scanned repo's lockfile closure in one plan. This spec is deliberately narrow — it is the plumbing that lets multiple lockfile repos flow through the model SPEC-007 already defined; every semantic (row identity, findings, provenance) is unchanged.

## 2. Problem (with committed evidence)

`scans/billing` and `scans/shipping` (real tracemap output, PR #10):

| Repo | lockfile rows (net8.0) |
|---|---|
| billing | Contoso.Payments 1.0.0 direct (names: Newtonsoft.Json); Newtonsoft.Json **13.0.3** transitive |
| shipping | Contoso.Payments 2.0.0 direct (names: Newtonsoft.Json); Newtonsoft.Json **12.0.3** transitive |

Today: `ua ingest scans/billing scans/shipping --out …` → `error: lockfile facts found for 2 repos (billing, shipping); V0 supports one`, exit 2. The bridge cannot plan a two-repo estate, let alone 150.

Root cause: the `lockfile-rows.v0`/`v1` envelope carries exactly one `repo` + `rows`, the planner's `Engine` accepts one such file, and `LoadEngine` reads one file.

## 3. Input schema: `lockfile-rows.v2` (v0/v1 frozen)

```json
{
  "schemaVersion": "lockfile-rows.v2",
  "repos": [
    { "repo": "billing", "rows": [ { "packageId": "…", "type": "…", "version": "…", "via": "…", "names": "…", "lockfile": "src/Api/packages.lock.json", "tfm": "net8.0" } ] },
    { "repo": "shipping", "rows": [ … ] }
  ]
}
```

- `repos[]` replaces the single `repo`/`rows` envelope. Row shape and semantics are **exactly v1** (SPEC-007 §3): identity `(repo, lockfile, tfm, packageId)`, same-key conflict rules, null-version rules, `names: null` meaning.
- v0 and v1 files remain accepted at load (F9/F10/F11 and the 13 pre-lockfile-era fixtures stay byte-identical; **zero golden churn**). v1 is no longer written.
- `ua ingest` emits **v2 whenever any lockfile facts exist** (one emitted shape; a single-repo run produces `repos: [one]`). A run with NO lockfile facts emits no lockfile file at all — exactly today's behavior for lockfile-less scans (`legacy`, `cleanrepo`); an empty `repos: []` file is never written (and is malformed at load).
- Load-time typed errors extend naturally: duplicate `repo` entries in `repos[]`; empty `repos`; per-repo row validation identical to v1. Both v1 and v2 files present → typed error (same rule as v0/v1 today).

## 4. Ingest changes

- The multi-repo check (exit 2) is **removed** — it was the typed, honest placeholder for exactly this spec.
- Lockfile rows are grouped per repo exactly as today; each repo's rows are canonical-sorted (packageId, lockfile, tfm, version), `via` derived per `(lockfile, tfm)` group within the repo — all unchanged, just per-repo.
- Output: one `lockfile-rows.v2.json` containing every repo with lockfile facts, `repos[]` sorted by repo name (ordinal); no lockfile file at all when no lockfile facts exist (unchanged).
- `packages-lock-group-unsupported`, null-version coverage gaps, per-repo gap attribution: unchanged (per repo).
- Exit-code table: exit 2 is retired (no remaining condition uses it); exits 0/1/3/4/5 unchanged in meaning.

## 5. Planner semantics

Everything already operates through the per-repo row lookup; multi-repo is plumbing plus these pins:

1. **Rule (c)/(b)/(d) evaluation per repo** — each repo's own rows drive its own classification, unchanged. billing and shipping each classify independently (rule c: transitive Newtonsoft.Json row each).
2. **Findings stay per-repo internal** (SPEC-007 §6 unchanged): a version difference BETWEEN repos is NOT a finding — different repos legitimately resolve different versions (that is the entire waves premise); findings report disagreement inside one repo's lockfiles/TFMs. billing (13.0.3) vs shipping (12.0.3) yields **zero findings**.
3. **Evidence-kind provenance:** `lockfile-rows.v2` for v2 inputs (same canonical slot).
4. **Waves:** unchanged construction — both billing and shipping are consumers of an external target, ownership-known, depth 1, ready → **one wave, two release units** (same (rank, depth) share a wave, units sorted by repo name).
5. **Carrier/closure logic** (`CarrierPkg`, dependency edges, `not-affected` closure) reads per-repo rows — no cross-repo row mixing ever occurs because every lookup is keyed by repo.

## 6. Determinism contract additions

1. `repos[]` in v2 files: repo-name ordinal (ingest writes; canonicalizer enforces).
2. Findings across multiple lockfile repos: same rule as now (subject ordinal, tie detail) across the union.
3. Permutation acceptance extends to v2: reversing `repos[]` and each repo's `rows[]` produces identical output bytes.
4. `evidenceKinds` canonical order gains `lockfile-rows.v2` in the lockfile slot (after v0/v1 forms — only one ever appears per plan).

## 7. Acceptance criteria

1. **Zero golden churn:** all 19 existing fixtures byte-identical (v0/v1 paths untouched).
2. **New fixture `F12-multi-repo-lockfiles`** — input generated by `ua ingest` from the committed `scans/billing + scans/shipping` with the committed `sidecars-estate2/` sidecars (ownership declares billing+shipping = team-a; producer-evidence declares Newtonsoft.Json external; delta 12.0.3 → 13.0.3) — without ownership evidence the asserted wave cannot exist, so the sidecars are part of the pinned acceptance, not incidental: billing and shipping both `affected` via rule (c); **one** ready wave with two release units (billing, shipping — name order), T5 prerequisite; **zero findings** (cross-repo difference is not a finding); both repos cite `lockfile-rows.v2`; coverage gaps per repo (buildStatus FailedOrPartial etc.) land on the right repo only.
3. **Ingest of the committed estate** exits 0 and emits one `lockfile-rows.v2.json` with two repos, rows sorted per repo; exit 2 is gone.
4. **End-to-end, machine-checked forever:** permanent selftest case ingests the committed two-repo estate with `sidecars-estate2/` → plan → report, asserting both repos affected, zero findings, v2 provenance.
4a. **Mixed lockfile/no-lockfile run:** ingesting `billing + shipping + cleanrepo` (or `legacy`) in one run emits v2 with exactly two `repos[]` entries — the lockfile-less repo contributes no lockfile rows and no invented entry.
5. **Typed rejections as selftest cases:** duplicate repo in `repos[]`; v1+v2 files both present; per-repo row conflicts (v1 rules applied per repo).
6. **Permutation:** reversed `repos[]`/`rows[]` → identical bytes (F12 selftest case).
7. **Single-repo runs produce v2 with one entry** — and F9's selftest e2e case is updated to expect v2 provenance (F9's committed input file stays v1; the e2e re-ingests live).

## 8. Out of scope (typed, honest, logged)

- **Cross-repo findings** (e.g. "shipping lags billing on Newtonsoft.Json") — the waves already encode who moves when; adding a comparison finding is a future wording decision with no committed evidence need yet (logged; pairs with Kiro's from-version advice).
- **`--scans-root` globbing** for estate-scale runs — CLI sugar over many scan dirs; next slice after this one.
- **Merging repos by identity** beyond existing mirror rules (SPEC-001 §3) — two scan dirs of the SAME repo (same repoKey) collapse today at the fact level; v2 keeps that behavior (rows merge into one `repos[]` entry, same-key rules apply) and this spec pins it rather than changing it.
- Kiro's findings-UX advice (#1/#4/#5 from PR #8) — queued separately; nothing here blocks them.

## 9. Implementation notes

- Spec PR (this document) rides on PR #10's evidence. Implementation PR: models (v2 envelope), ingest (emit v2, drop exit 2), load validation (v2 + duplicate-repo + both-files rules), engine (list of lockfile repos — the per-repo dictionary already exists), findings across repos, report (no changes expected), validator/canonicalizer/templates (v2 slot), F12 (golden-first from rules, input from real ingest), selftest cases (rejections, permutation, estate e2e). Expected GOLDEN-CHANGES: none.
- The engine's `_lock`-as-single-repo assumptions (e.g. `BuildFindings` reading one repo) become loops over the lockfile-repo set — mechanical, guarded by the corpus.

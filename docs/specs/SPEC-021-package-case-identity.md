# SPEC-021 — Case-insensitive package identity in plan/apply

**Status:** reviewed — round 2 SHIP verdict (round-1 findings folded; N1-N4 minors folded inline; records in `docs/reviews/spec-021/`)
**Author:** ZCode (coordinator); closes the finding deferred with receipt during PR #3 (record: `docs/reviews/spec-018/PR3-ROUND1.md`, C1)
**Date:** 2026-10-10
**Depends on:** SPEC-002 §2 (fusion identity) · SPEC-003 §2 (affected closure + not-affected proof) · SPEC-007 (resolution-group conflicts — the model for typed rejection) · SPEC-009 §5 (apply verification) · SPEC-010 (push identity/idempotency) · SPEC-012 §2 (the one OrdinalIgnoreCase spot today) · SPEC-018 §4 (Drift's conventions — the model)

## 1. Purpose and the hazard

NuGet package ids are **case-insensitive by ecosystem definition** — `Newtonsoft.Json` and `newtonsoft.json` are one package (the rule and its safety argument are scoped to `ecosystem: nuget`, the only ecosystem V0 ingests; future ecosystems inherit or version their own rule). A real estate can legally carry case variants **across** repos: repo A's scan reports `Newtonsoft.Json`, repo B's reports `newtonsoft.json` (SPEC-007 forbids variants only **within one resolution group**, where they are already a typed conflict). But plan/apply compare package ids **ordinally** today, in two hazard classes:

- **Under-scoping:** a delta naming one spelling silently misses the other repo's facts, rows, edit sites, and producer claims — no error fires (`Planner.cs` rule-b/rule-c loops, `ConsumersOf`, `bFact` selection — which picks the edit-site evidence — format probes, `CarrierPkg`/corroboration joins, the `_external`/`_producerRepos`/`_pubStatus` maps; `Apply.cs` fact selection and file verification). A drift-emitted delta mitigates (estate spelling) but hand-written deltas carry the identical hazard.
- **FALSE SAFETY (round-1 blocker R1-1):** the not-affected/touches closure joins an `affectedPkgs` set seeded with the delta + producer spellings against fact/row spellings ordinally — a complete-coverage repo whose lockfile rows spell the variant is granted `not-affected` ("no {Target} row") while a variant-spelled row sits in that very lockfile. This is the one site where a case variant produces a false *safety* assertion, not mere under-scoping.

## 2. The rule

**Every package-id comparison in plan/apply that joins two different evidence sources is `StringComparer.OrdinalIgnoreCase`.** Sites:

- (a) **Planner target matching:** rule-b facts, rule-c provable rows, `bFact`/format probes, `ConsumersOf(pkg)` (producer spelling vs consumer facts/rows), `CarrierPkg` and corroboration joins — **including the `r.Via == p2`/`carrier` predicates** (Via is the lockfile's spelling of the parent; p2/carrier the producer's — cross-source), rule-c gap lookups, the F1/F1b ripple-note join (`u2.Packages.Contains(f.PackageId)`), and CyclePlan's produced/fact filters.
- (b) **The closure set (R1-1):** `affectedPkgs` (and `HasStaleDepsRow`'s set) is case-insensitive — a variant-spelled row or fact can never be invisible to the not-affected proof. A case-variant lockfile repo classifies `unknown`, never `not-affected`.
- (c) **Fusion maps keyed by package id:** `_external`, `_producerRepos`, `_pubStatus`'s package component (repo components stay exact — repo identity is SPEC-001 §3, mirrors only, never case-folded). `_produced` stays keyed by repo; its VALUES join other sources through (a)/(b).
- (d) **Apply:** edit-site fact selection, `ConstraintKind`/no-edit-site classification — OrdinalIgnoreCase against the delta target. **File verification (`VerifySite`/`ElementSpansNaming`, shared by `ua apply --repo` AND `ua push`) matches the checkout against the edit site's OWN evidenced package id** (the fact's spelling — what the file itself says), never the delta's. The `packageId` parameter of `WriteVerifiedPatches` is removed (per-edit spelling replaces it). **The file matcher itself stays Ordinal on the fact's own spelling** — case-folding it would make a line carrying two spellings read as one ambiguous site (R2-4's negative instruction).
- (e) **T10 findings grouping (R1-9):** lockfile rows group case-insensitively by package id — a repo resolving one id to two versions across case-variant lockfiles is ONE resolution disagreement; the group's reported spelling is ordinal-first (deterministic, permutation-stable).

**Edit-site dedup (R2-3):** the dedup key gains the evidenced spelling — two variant-spelling elements sharing `(Path, Line, Attribute, OldVersion)` produce TWO edits, each verifying its own element. Silence (one edit surviving, the other element left at the old version) is the one forbidden outcome.

**Manifest (R1-2):** an edit emits `packageId` **only when it differs from `delta.packageName`** — zero churn on the existing corpus (verified variant-free), replayable exactly where it matters, and the conditional emission is itself golden-pinned by F-case.

**Safety argument (over-matching):** two distinct packages differing only by case cannot exist in NuGet — case-insensitive joins can never merge two different packages. The inverse risk (today's) is real: silent under-scoping and false `not-affected`.

**Typed rejection (R1-6/7):** ONE sidecar claiming the same `(repo, packageId)` in two spellings is a typed load error (the SPEC-007 rule applied to producer claims — a sidecar contradicting itself about one package); this kills the two-spelling `Range()` and the split `VersionGroups` at the source. **Cross-repo** variants stay legal and now meet: ≥2 repos claiming one id ⇒ contradiction C-n (today the second claim is invisible to the target join).

## 3. Reported spellings and consequences (unchanged outputs, changed behavior — each deliberate)

- `delta.packageName`, C-n `Claims`, prerequisites, T-notes and apply's N-notes **keep the delta's spelling** — they are echoes; on a variant estate they may cite a spelling the underlying evidence lacks (stated once, here).
- Per-repo reason text cites each repo's own fact spellings, as today; unit `packages[]` keep the producer sidecar's spellings; `VersionGroups`/`Range` ordering already case-folds (natural sort).
- **Produced + case-variant `externalPackages` (R1-5):** `externalTarget` becomes true ⇒ the T5 prerequisite, the DECLARED-external attribution reason, and the external gap all appear, and **corroboration degrades** (carrier no longer corroborates: rung stays, corroboration count drops, the lockfile evidence kind drops from the repo's evidence kinds). Not a silent unknown-producer path — the observables are named and machine-checked.
- **Apply reconciles SITES, not spellings (R2-7):** a repo with `Contoso.Core`@14.0.0 (satisfied) + `contoso.core`@12.0.3 (stale) gets an edit for the stale site only — never a downgrade; same-repo variant pairs are SPEC-007 conflicts at load (or T10 findings across lockfiles, (e)); apply adds no new finding.
- **Push identity (R2-2a):** branch/commit/PR names keep the delta spelling (echo). **Idempotency-by-refusal extends case-insensitively:** the precondition check compares the constructed `ua/wave-N/...` ref against existing refs ignoring case — a re-run with a different spelling of the same logical upgrade refuses (never a second branch/PR; never a force-update).
- **Ingest (R1-4):** the case-variant producer warning's parenthetical ("planner identity is case-sensitive; merge deliberately") becomes false — the text updates to state **package** identity is case-insensitive in the planner and same-repo variant claims are rejected at load (repo identity stays exact — the same warning also fires for repo-spelling variants, where the old text remains true) (stderr-only; keep-both behavior at ingest unchanged — the loader's typed rejection above is the guard).
- Drift, registry, ownership, ingest joins: already case-insensitive where they join packages — **no behavior change** (their goldens and batteries prove it).

## 4. Amendments to prior specs

- **SPEC-002 §2:** package identity for producer claims, external declarations, and contradiction detection is OrdinalIgnoreCase; same-sidecar case-variant claims for one repo are a typed load error.
- **SPEC-003 §2:** affected-rule matching AND the not-affected closure (`affectedPkgs`) are OrdinalIgnoreCase — a variant-spelled row/fact can never be invisible to either side.
- **SPEC-009 (§3 edit rules + §4 manifest + §5 verification):** file verification keys on the edit site's own evidenced package id; the dedup key includes the evidenced spelling; edits carry `packageId` when (and only when) it differs from the delta's.
- **SPEC-010 (§3.1 preconditions + §5 templates):** precondition refs compare case-insensitively (spelling-variant idempotency); branch/commit/PR text keeps the delta spelling.
- **SPEC-012 §2:** the marker's OrdinalIgnoreCase comparison stops being "the exception" and becomes an instance of this rule; T10 groups case-insensitively.

## 5. Acceptance criteria

1. **`F-case` fixture** (hand-written, house style): `repoA` references `Newtonsoft.Json` 12.0.3 (pin + line evidence), `repoB` references `newtonsoft.json` 12.0.3, delta names `NEWTONSOFT.JSON` 12.0.3→13.0.3 ⇒ BOTH repos `affected` (rule-b), one wave, both edit sites derived; `Serilog` (genuinely different, both repos) untouched. Goldens: `plan.json`, `report.md`, `apply.v1.json` (edits carry their own `packageId` — the conditional emission pinned).
2. **Real-CLI per-repo verification (R2-1):** committed tiny sources under `testdata-ingest/tracemap-rich/sources/{caseA,caseB}/` carry each repo's own spelling; the selftest makes per-repo-scoped fixture COPIES (strip the other repo's facts — the multi-repo refusal's own guidance) and runs `Apply.Run(--repo <committed source>)` per repo ⇒ exit 0, per-repo patch goldens under `F-case/golden/patches/`, and the patched lines keep their original case.
3. **Closure honesty (R1-1):** a complete-coverage repo whose lockfile rows spell the variant of an affected package classifies `unknown`, never `not-affected` (selftest, synthetic input).
4. **Cross-repo variant producer claims ⇒ contradiction C-n**; **produced + case-variant external ⇒ T5 present, attribution reason present, corroboration degraded to 1** (selftest, synthetic inputs — observables named, machine-checked).
5. **Determinism + permutation:** F-case plan/apply bytes permutation-invariant; two runs byte-identical; a repo carrying BOTH spellings picks `bFact` ordinal-first across spellings — pinned (selftest).
6. **Every existing golden byte-identical, verified not assumed** — plan, report, drift, registry, **apply.v1, patches, push dry-run** goldens all byte-compared after implementation; the corpus was swept for case-variant package spellings before implementation — **zero found** (record in `docs/reviews/spec-021/GOLDEN-CHANGES.md`). Same-repo variant producer claims ⇒ typed load error; **push precondition refusal is case-insensitive** — a second push with a differently-spelled delta for the same logical upgrade refuses (N2, selftest).
7. Counts swept from the current totals (selftest + validator + README/FEATURES/runbook); PowerShell 5.1-safe snippets only.

## 6. Out of scope (typed, honest, logged)

- **Repo identity** stays exact-match + declared mirrors (SPEC-001 §3) — case-folding repo ids would silently merge distinct orgs' repos (ownership's repo-side case warning stands).
- **Renaming/normalizing spellings in outputs or inputs** — ua never rewrites evidence; case variants within ONE resolution group remain SPEC-007 typed conflicts.
- **Drift/registry/ownership/ingest join behavior** — already case-insensitive; only the ingest warning TEXT changes (§3).
- **Multi-repo checkout application** — stays SPEC-010 territory; per-repo runs, exactly as today.

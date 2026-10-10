# SPEC-009 — `ua apply` v1: dry-run edit generation

**Status:** draft — round 1 review (brief-013)
**Author:** ZCode (coordinator)
**Date:** 2026-10-03
**Depends on:**
> **Amended by SPEC-021 (2026-10-10):** edit-site fact selection matches the delta target OrdinalIgnoreCase; file verification keys on the edit site's OWN evidenced package id (the file matcher stays Ordinal); the edit dedup key includes the evidenced spelling; edits emit `packageId` when (and only when) it differs from `delta.packageName`. SPEC-003 (waves/prerequisites) · SPEC-007/008 (lockfile rows, findings) · tracemap 0.2.0 (`CentralPackageVersionDeclared`, `versionOverride` — merged tracemap#804, scans refreshed in PR #12)

## 1. Purpose

Turn a plan into the concrete file edits it implies. `ua apply` reads a fixture (plan inputs), rebuilds the plan, and emits a deterministic **apply manifest** (`apply.v1`) naming every evidenced edit site for the delta target — file, line span, old text, new text, evidence kind — plus, when pointed at a repository checkout, **verified unified diffs**. Nothing is written into any repo in v1: no git, no branches, no PRs, no network. Those mechanics are SPEC-010; this spec is the evidence-checked edit layer beneath them.

## 2. Scope and non-goals

**In scope:** edit-site derivation from evidence; the apply.v1 manifest; evidence verification against a checkout (`--repo`); unified diffs per wave; determinism (canonical serialization, byte-exact goldens); typed refusals.

**Out of scope (typed, honest, logged):**
- **Execution:** git operations, branch/PR creation, GitHub API, auth (SPEC-010).
- **Lockfile edits:** `packages.lock.json` is restore-generated; ua never hand-edits it. Lockfile rows inform affected-ness (SPEC-007/008), not edit sites.
- **Transitive bumps:** a repo exposed to the target only transitively has NO direct edit site — its path to the new version is bumping its direct chain or pinning explicitly, a human/Phase-2+ decision. v1 records `no-edit-site` with that reason; it never invents an edit.
- **Producer republish edits:** producer units must republish; their own version-bump site (csproj `Version` property) is not indexed by tracemap in V0. Producer units get a `republish-required` entry with no edit list. (A future tracemap slice could index project `Version` properties.)
- **Multi-change deltas** (unchanged: typed rejection), version arithmetic beyond substitution (no floating ranges: `version: "[1.0.0, 2.0.0)"` constraints are NOT rewritten in v1 — only exact pins), cross-TFM convergence decisions (findings territory).

## 3. Edit-site model (exhaustive)

For the delta target `{pkg}` → `newVersion`, per scheduled unit (wave-ordered), edit sites are derived ONLY from evidence:

| # | Evidence | Edit site | Edit |
|---|---|---|---|
| E1 | `PackageReferenced` fact, `manifestKind=csproj/vbproj`, non-empty safe `version` (exact pin) | the csproj, fact line | attribute/element `Version` value `old → newVersion` |
| E2 | `CentralPackageVersionDeclared` fact (tracemap ≥0.2.0) for `{pkg}` | the props file, fact line span | `PackageVersion` item's version `old → newVersion` |
| E3 | `PackageReferenced` fact with `versionOverride` (non-empty safe) | the csproj, fact line span | `VersionOverride` value `old → newVersion` (override outranks the central pin for THIS project — NuGet semantics; the central pin at E2 is still listed separately with `shared` flag) |
| E4 | `PackageReferenced` fact, `manifestKind=packages.config` | packages.config, fact line | `version` attribute `old → newVersion` |

Rules:
- **Evidence-gated:** a site exists only with a fact carrying an exact, safe version. Redacted (`versionHash`), empty, or ranged constraints ⇒ `no-edit-site` + reason (`constraint-not-evidenced` / `constraint-ranged` / `constraint-redacted`). Ranged constraints are deliberately untouched in v1 — rewriting a range is a semantic decision.
- **Never downgrade (upgrade-only policy):** versions compare by numeric core segments (`major.minor.patch[.rev]`; prerelease/build suffixes are not ordered in V0 — such pins get `no-edit-site` + `constraint-prerelease`). A pin **above** `newVersion` is `already-satisfied` (note N4) — the repo already meets the target; **equal** pins are `already-satisfied` too (no edit, no churn). Only pins **below** `newVersion` produce edits. An `already-ahead` acceptance case is required (a 2.0.0 pin under a 1.0.0→1.1.0 delta yields N4, never a downgrade patch).
- **Occurrence-level identity via a structural selector:** each edit carries `selector: { packageId, attribute, oldVersion }` where `attribute` names the on-disk form (`Version` reference attribute/element · `VersionOverride` · `PackageVersion` item's `Version` · packages.config `version`). The selector is DERIVED — fixtures carry no source text (scan facts hash snippets, never store them), so no stored match string exists; the concrete on-disk text is resolved only during `--repo` verification (§5). Two same-package entries with different versions are distinct sites (different selectors); two **identical** selector matches in one verification window ⇒ typed refusal `ambiguous-site` (exit 6) — never guess which to edit.
- **Per-repo ordering:** edits sorted by (path ordinal, line).
- **Wave attachment:** each edit carries its unit's wave index and the wave's prerequisites verbatim (from the plan) — the manifest answers "what do we edit AND what must be true first."

## 3a. Ingest extension (prerequisite — the evidence must survive ingest)

Today `ua ingest` drops everything apply needs: `Fact` carries no line, `versionOverride` is ignored, and `CentralPackageVersionDeclared` facts are not consumed at all. SPEC-009 extends ingest and the fixture schema:

- **`Fact` gains `line` and `endLine`** (int?, each omitted when null) — the COMPLETE evidence span from the scan fact. Multiline elements (child `<Version>` several lines below the opening tag) must not fall outside apply's verification window; where a span is one line, `endLine` is omitted.
- **Central pins map onto the existing CPM fact shape** (the planner already understands it — F7): `CentralPackageVersionDeclared` → `Fact { format: "cpm", constraintSource: "Directory.Packages.props", declaredConstraint: <pin version>, path: <props path>, line: <pin line>, endLine: <pin span end> }` — **correlated with references**: a pin is emitted only when the same repo has a **non-lockfile** reference fact for the same package (`manifestKind` csproj/vbproj/packages.config — never `packages.lock.json`, whose rows are transitive/direct resolutions, not consumer declarations: a package appearing only transitively in the lockfile must not turn an unused pin into a false consumer). An unused pin (legal MSBuild: props retain versions nothing references) is NOT consumer evidence — the planner's rule-(b)/consumer logic would otherwise fabricate an affected repo — and it needs no edit for this delta. Unused pins are skipped and counted in ingest stderr.
- **`versionOverride` maps onto the reference fact**: `constraintSource: "VersionOverride"`, `declaredConstraint: <override>`, `line` = span start (E3 sites exist only for facts that carry an override).
- **Occurrence-preserving dedup:** ingest's reference-fact dedup identity extends from `(packageId, path)` to `(packageId, path, line, declaredConstraint, constraintSource)` — two conditional `PackageReference` entries for one package in one project (including the same-line case) must both survive to apply.
- Apply derives E2 from `cpm` facts, E3 from `VersionOverride` facts, E1/E4 from exact-pinned reference facts — one evidence vocabulary end to end.
- **Golden safety:** the new fields are omitted when null and the new CPM facts only add evidence for packages the plan already carried — F9/F12 inputs regenerate, their plan/report goldens stay byte-identical (verified in acceptance 4; any diff is a fidelity event, never silent).

## 4. Manifest: `apply.v1`

Written to `<out>/apply.v1.json` (canonical serialization — same formatter rules as plan.v1; new top-level schema `apply.v1`):

```
{ schemaVersion: "apply.v1", delta: {…}, waves: [ { index, status, prerequisites[], condition?, blockedOn?, units: [ { repo, action, edits: [ { kind: E1|E2|E3|E4, path, line, attribute, oldVersion, newVersion, evidenceKind, shared? } ], note? } ] } ] }
```

- `action`: `edit` (≥1 edit) | `no-edit-site` (affected, no evidenced site — carries `note` with the reason) | `republish-required` (producer unit) | `external-request-await` (external ownership — rendered, never executed by us).
- **Gates carry verbatim:** conditional waves include the plan's `condition`, provisional waves their `blockedOn` — the manifest must state the decision that gates the edits, not just a status label. Both wave kinds are covered by acceptance cases.
- `evidenceKind`: `package-evidence.v0` | `central-package-version.v0` (new — cites `CentralPackageVersionDeclared` facts).
- Unscheduled/unknown repos do not appear (the plan already carries them).
- Zero-edit plans are legal output (all `no-edit-site`) — an honest manifest, never padded.

## 5. Verification + diffs (`--repo <path>`)

When `--repo` is given (root of the repo checkout the evidence describes):
1. **Evidence check before any edit:** at each site, the file must exist, and within the verification window (the fact's line span, or its line ±2 to tolerate reformatting) there must be **exactly one** occurrence matching the full selector — the package id AND the named attribute AND `oldVersion` together (never "the old version string somewhere": a neighboring package on the same 1.0.0 must not satisfy a Contoso.Core check). Zero matches ⇒ **typed refusal** (exit 6) with expected vs found; two or more identical matches ⇒ `ambiguous-site` refusal. ua never edits a file whose content contradicts — or fails to uniquely identify — its evidence.
2. Verified sites produce **unified diffs** (`git diff --no-index` semantics, emitted by ua itself — no git dependency): one `<out>/wave-N.patch` per wave, files in canonical edit order. The diff rewrites exactly the matched occurrence.
3. `--repo` with multiple repos in evidence: `--repo` maps ONE repo (the lockfile/in evidence root); multi-repo checkout application is SPEC-010 territory. For estates, run apply per repo fixture. (Pinned: v1 fixtures are single-estate.)

## 6. Determinism contract

1. Canonical serialization (2-space, LF, trailing newline, minimal escaping) — `apply.v1.json` goldens byte-exact.
2. Edits ordered (path, line); waves by index; units per wave by repo name (plan order).
3. Pure function of the fixture + (for diffs) the checkout bytes. Permutation acceptance extends: reversed input arrays ⇒ identical manifest bytes.
4. Note templates (exhaustive — no other note strings):
   - **N1** `no-edit-site` note: `no evidenced direct edit site for {pkg} in {repo} — exposure is transitive or the constraint is not an exact pin; bump the direct chain or pin explicitly (human decision)`
   - **N2** `republish-required`: `unit must republish before downstream edits take effect; its own version-bump site is not indexed in V0`
   - **N3** ranged/redacted/empty constraint: `constraint for {pkg} in {repo} is {kind}; v1 rewrites exact pins only`
   - **N4** `already-satisfied`: `{repo} pins {pkg} at {version}, already at or above {newVersion} — no edit, never a downgrade`

## 7. Acceptance criteria

1. **New fixture `F13-apply-cpm-override`** — input generated by `ua ingest` from the committed (0.2.0) `scans/svc`, with a `Contoso.Core 1.0.0 → 1.1.0` delta sidecar: svc is ONE unit (one repo) whose `edits` are exactly two — E2 (central pin `Contoso.Core 1.0.0 → 1.1.0`, `shared: true`, `Directory.Packages.props` — covers Api's CPM reference, which carries `version: ""` and is therefore not itself a site) and E3 (Worker's `VersionOverride 0.9.0 → 1.1.0`); no `no-edit-site` entries (every reference resolves through the pin or the override); golden manifest hand-authored first.
2. **Diffs vs `--repo sources/svc`:** `ua apply <F13> --repo testdata-ingest/tracemap-rich/sources/svc --out …` emits wave-1.patch editing exactly the two sites (each verified by its selector occurrence); evidence check passes; patch applies cleanly (selftest re-applies and re-scans textually).
2a. **Already-ahead:** a scratch variant pinning Contoso.Core at 2.0.0 under the 1.0.0→1.1.0 delta yields N4 entries and an empty patch — never a downgrade.
2b. **Ambiguity refusal:** a scratch props file with two identical `Include="X" Version="1.0.0"` pins in one span ⇒ typed `ambiguous-site` refusal (exit 6).
2c. **Gated waves:** one acceptance case with a conditional wave (condition carried verbatim) and one with a provisional wave (blockedOn carried) — synthetic fixtures are fine.
3. **Typed refusal:** scratch a checkout whose pin says `9.9.9` ⇒ exit 6 naming the site, expected 1.0.0, found 9.9.9.
4. **Zero golden churn** on the 20 existing fixtures (apply is additive: new command, no planner/report changes).
5. **Transitive honesty:** the svc Newtonsoft.Json delta (existing sidecars) yields all `no-edit-site` + N1 — machine-checked selftest case.
6. **Selftest:** manifest goldens + permutation + refusal + patch-apply case; report.md unchanged (apply output is JSON + patches).
7. **`ua report`/`ua plan` byte-identical** before/after this spec (no shared-code drift).

## 8. Open questions logged (coordinator defaults, Joe overrides)

1. **E3 vs E2 when both exist:** v1 edits BOTH (override for the project, pin for the estate) and marks the pin `shared` — a human may prefer deleting the override; deletion is not a v1 edit kind.
2. **`--repo` for producer repos:** out of scope (republish notes only).
3. **Should diffs include report prose?** No — patches are machine artifacts; prose lives in the PR body (SPEC-010).

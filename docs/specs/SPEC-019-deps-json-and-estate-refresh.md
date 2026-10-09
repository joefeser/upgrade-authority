# SPEC-019 — deps.json evidence + estate refresh + parallel scans

**Status:** implemented — review converged round 2 (SHIP; records in `docs/reviews/spec-019/`); branch `feature/deps-evidence`
**Author:** ZCode (coordinator); deps.json = Joe's insight (2026-10-08); refresh/parallel/worktree shaped with Joe (2026-10-09)
**Date:** 2026-10-09
**Depends on:** tracemap PR #848 (SHIPPED — `--index-deps-json`, fact shape AUDITED against real output) · SPEC-017 (scan-estate, cache, freshness — **amended here**: cache literal v1→v2, run-manifest repos[] gains buildFreshness, parallelism note) · SPEC-007/008 (lockfile rows — the surface deps.json rides; **amended here**: provenance joins row identity) · SPEC-015 (**amended here**: `deps-json-not-found` ⇒ Note) · SPEC-003 (not-affected rule — **amended here**: §5.3)

## 1. Purpose

Close the ~112/130 unknowns: Joe's org will not adopt NuGet lockfiles, but every built repo already carries `bin/**/<Assembly>.deps.json` — the fully resolved transitive closure. tracemap now indexes it (opt-in, stale-qualified by its own help text). This spec makes ua **consume it honestly** (staleness never silently proves safety), makes the estate **refresh first-class** (north-star step 1's verb: *ensure* repos are on latest, with Joe's work-machine script as the reference), and makes first scans **tractable at scale** (bounded parallelism). One coherent change to the scan loop and its cache metadata.

## 2. Command surface (scan-estate additions)

```
ua scan-estate … [--update-checkouts] [--worktree] [--worktree-root <dir>] [--trunk <branch>]
                 [--parallel <N>] [--build] [--index-deps-json]
```

- `--trunk <branch>`: run-wide trunk override (default auto-detect origin/main → origin/dev stays). Per-repo trunk config waits for a real need.
- `--parallel <N>`: bounded scan/fetch/build parallelism, **default 2** (fair and modest per Joe; raise for first scans). N < 1 or > 64 ⇒ typed error. **All eligibility/dedupe/freshness decisions are made sequentially in name-ordinal order BEFORE any fan-out** (the alternate-checkout first-wins rule must never become timing-dependent). Fetch ownership: under `--update-checkouts`/`--worktree` the refresh fetch runs in the sequential pass (its result feeds the trunk-tip keying); in plain mode each repo's fetch is part of its own parallel work unit and feeds ONLY that repo's freshness decision — no cross-repo ordering ever depends on fetch timing. Output determinism is independent of N: results collect and emit in name-ordinal order; console lines are written atomically under a lock; per-repo failure isolation unchanged.
- `--index-deps-json`: pass-through to tracemap (a **scan condition** in the cache key — enabling it later rescans, like `--exclude`).
- `--update-checkouts` (in-place refresh) and `--worktree` (isolated scan) are **mutually exclusive refresh modes**; both ⇒ typed error.

## 3. Refresh modes (Joe's script as the reference)

### 3.1 `--update-checkouts` — in-place (the script's behavior)

Per repo, before freshness checks: fetch; if dirty ⇒ **rescue**: create branch `ua/rescue-<sha8-of-HEAD>` off HEAD and commit ALL stray work including untracked files (the sha suffix keeps names unique and keeps wall-clock bytes out of artifacts; console + manifest entry record the branch name — the operator can find their WIP later; never silent, never dropped; the commit uses the repo's own git identity — none configured ⇒ typed skip, identity is never invented); checkout the trunk branch (creating a local tracking branch if needed); fast-forward to `origin/<trunk>` tip. **Never** rebase or merge: local commits ahead of the tip (non-ff) ⇒ visible skip (`local commits ahead — refresh refused rather than merge`). Anything that cannot be made fresh safely skips visibly, exactly as today.

### 3.2 `--worktree` — isolated scan (the drive-space-safe alternative)

Never touches the operator's checkout — HEAD, branch, and dirty state are **byte-identical after the run** (selftest-pinned). Per repo: `git worktree add <worktreeRoot>/ua/<name>-<hex8> origin/<trunk>`. **`--worktree-root <dir>`** makes all worktrees share one configurable root (default `<out>/.worktrees/`; an operator aliasing `D:\ua-worktrees` is the intended use — less clutter). Everything ua creates lives under the `ua/` NAMESPACE inside that root, whatever it points at — a shared root can never mix our satellites with anyone else's worktrees. Worktrees share each repo's object store (peak extra disk ≈ `--parallel` × working-tree size) and are removed immediately after their scan.

  **Removal + cleanup safety (the "never break the base repo or a neighbor" rules):**
  - `git worktree remove --force` applies ONLY to worktrees under `<worktreeRoot>/ua/` that WE created — build outputs make them untracked-dirty, and `--force` is exactly for that; nothing outside the namespace is ever touched, even pattern-matching.
  - Crash-leftover sweep: only paths recorded in our own run metadata, plus exact-pattern matches under `<worktreeRoot>/ua/`; `git worktree prune` follows (repo-local admin cleanup, always safe).
  - **Base-repo safety, stated**: adding/removing a worktree never modifies the base repo's branches, objects, or working checkout — a worktree is a satellite whose admin entry lives under the base's `.git/worktrees/`; deletion cannot corrupt the base (worst case is a stale admin entry, which `prune` clears). The worktree is scanned instead of the checkout; the cache dir keys on the repo NAME as usual. Dirty checkouts stop being a problem at all — no rescue, no skip.

- Without `--build`: a fresh worktree has **no** `bin/` ⇒ no deps.json facts (tracemap reports `deps-json-not-found`; honest absence, not an error). A `--build` failure ⇒ visible skip (`build failed: <first stderr line, scrubbed+ capped>`) — never a scan-without-note; `buildFreshness` is not recorded for skipped repos.
- With `--build`: compile inside the worktree (restore+build, default configuration) ⇒ deps.json produced **at the scanned commit** — build evidence is provably fresh by construction; `buildFreshness` records `fresh-by-build` and the cache metadata records the exact HEAD sha it was built at (`buildCommitSha` becomes knowable here even though the facts carry `unknown`).

`--build` outside `--worktree` mode compiles the operator's checkout in place (writes to their `bin/` — ignored artifacts, but noted in the manifest); worktree+build is the recommended pairing.

## 4. deps.json ingestion (ua side — audited real shape)

Facts with `manifestKind: "deps.json"` (real output verified 2026-10-09) ingest as **lockfile-class rows** into `lockfile-rows.v2` — and **never enter the consumer-fact path**: ingest skips them exactly like `packages.lock.json` (they carry a `version` property and would otherwise fabricate consumer facts, CPM correlations, drift `declared-pin` rows, and apply edit sites **inside `bin/`** — build output is resolution evidence, never a declaration). Acceptance pins: zero consumer facts with paths under `bin/`.

- `lockfile` = the fact's `manifestPath` (e.g. `src/App/bin/Release/net10.0/App.deps.json`); `tfm` = the fact's `targetFramework` **verbatim** — the full target form `.NETCoreApp,Version=v10.0`, deliberately NOT normalized (SPEC-007 house rule); a package's lockfile row and deps.json row are therefore distinct resolution groups by construction (spec'd, not accidental).
- `type` = `dependencyRelation` (direct/transitive/unknown — tracemap derives it from the emitted target graph, `relationBasis: "emitted-target-graph"`); `version` = `resolvedVersion`. `unknown`-relation rows ride exactly like lockfile `unknown` rows.
- The facts' reserved placeholders (`freshness: "unknown"`, `buildCommitSha: "unknown"`, plus `manifestSha256`) pass through untouched on a `depsProvenance` record attached to the row (v2 rows gain an optional `provenance` object; omitted for plain lockfile rows — zero churn). SPEC-007's identical-fields rule extends to include `provenance` (`SameRow` compares it): rows from one deps.json manifest share provenance by construction, so duplicates collapse and conflicts stay typed.
- **Classifier mapping (SPEC-015 rules)**: `deps-json-not-found` ⇒ **Note** ("build output not found — repo not built or --index-deps-json off"; the expected state for never-built repos, same honesty as lockfile-absence today — not a new blocking gap). Every other `deps-json-*` gap (`invalid`, `read-failed`, byte/file/library limits, `linked-path`, `relation-unproven`, discovery failures) ⇒ **Gap** (evidence exists but was lost — package-relevant).
- Drift (SPEC-018) picks these rows up through the existing lockfile-class surface; deps.json-sourced installations report `evidence: "deps.json"` (distinct from `lockfile` and `declared-pin` — an inventory reader can always tell resolution truth from build output). Dedup precedence for an identical `(repo, package, version)`: **lockfile > deps.json > declared-pin** (checked-in resolution beats build output beats declaration); the validator's evidence vocabulary grows the value — pinned by selftest.

## 5. Staleness (three layers, never silent)

1. **Upstream**: facts carry `freshness`/`buildCommitSha: "unknown"`; ua never overwrites them.
2. **Scan-time detection** (scan-estate has repo access; ua-the-planner never reads repos): per repo, `buildFreshness ∈ fresh | stale | none | fresh-by-build` — the **OLDEST** `bin/**/*.deps.json` mtime (min — conservative: fresh only if even the oldest build output postdates the change) vs the HEAD **committer** date, boundary `>=` (`--worktree` without `--build` ⇒ `none`; with `--build` ⇒ `fresh-by-build`). Known imprecision, stated: tracemap may index a SUBSET of bin manifests (include/exclude globs) — the min-rule covers what exists on disk, and the plan-level qualifier (§5.3) remains the honesty backstop regardless. Recorded in BOTH `scan-estate.v1.json` (per-repo), the cache metadata, and the fixture input (§5.3).
3. **Plan-level (SPEC-003 amendment)** — the signal reaches the planner through a **fixture input**, never a guess: scan-estate writes `<fixture>/input/build-freshness.v0.json` (`{schemaVersion: "build-freshness.v0", repos: [{repo, freshness, basis?}]}`; `basis` names the layer, NEVER timestamp values); ingest copies it when scan-estate produced one; `LoadEngine` reads it like any input. **Absent ⇒ conservative**: a deps.json closure alone cannot prove not-affected without the file (unknown never defaults). With it: a **build-resolved closure participates as positive transitive evidence only when `fresh`/`fresh-by-build`**, carrying the qualifier — house-style byte-exact templates: Q1 `closure evidenced by build output (deps.json), freshness: fresh` / Q2 `…freshness: fresh-by-build (built at the scanned commit)`. **`stale` demotes only repos whose SOLE closure is build-resolved** (classification `unknown`, reason naming both sides: HEAD commit vs build) — a repo with a complete lockfile closure keeps it, byte-identical to today (SPEC-003's lockfile rule stands independently); a repo with BOTH closures names both evidence kinds in its reason. `none` behaves as no transitive evidence at all (today's rule).

## 6. Cache extension (the "what can we skip" key)

The cache metadata bumps to **`scan-estate-cache.v2`** (a v1 file or an unknown version ⇒ rescan — old code never silently reuses scans taken under conditions it can't see) and gains: `depsFingerprint` (ordinal-sorted hash of `(path, size, mtime)` for `bin/**/*.deps.json` — metadata only, no content reads, make-style), `indexDepsJson` (bool), `buildFreshness`, `scanMode ∈ in-place | worktree`, `trunk` recorded **resolved** (`origin/main`, so `--trunk main` and auto-detect share one entry). Reuse requires ALL of those plus the existing key to match. **Per-mode:** in-place reuses against the checkout HEAD as today; **worktree reuses against the fetched trunk-tip sha** (its manifest sha), and its fingerprint is RECORDED at scan time from the scanned tree — never recomputed against the operator's checkout (which the scan never read). Crash-leftover `.wt-*` worktrees are cleaned on the next run exactly like `.tmp`/`.old` swap siblings. Consequence (the hole this closes): **a rebuild with unchanged HEAD changes the fingerprint ⇒ rescan** — today that would silently reuse the old scan, harmless while scans read only source, wrong once they read build output. First scan records everything; every later run skips only what is provably unchanged across content, build, and conditions.

## 7. Acceptance criteria

1. **Real-fact ingestion**: the audit's real tracemap output (scratch App repo scanned with `--index-deps-json`) committed as `testdata-ingest/tracemap-rich/scans/appdeps/` (synthetic-safe names, real-tool bytes) → ingest produces v2 rows with the pinned shape (deps.json lockfile paths, verbatim tfm, provenance object); drift classifies Newtonsoft 13.0.3 from it; `deps-json-not-found` ⇒ Note, `invalid`-class ⇒ Gap (selftest).
2. **Staleness**: fresh closure ⇒ `not-affected` WITH the qualifier; stale (build mtime older than HEAD commit) ⇒ `unknown` with the two-sided reason; none ⇒ unchanged rule (selftest, synthetic mtimes).
3. **Fingerprint cache**: rebuild-with-same-HEAD ⇒ exactly that repo rescans (seam-counted); no-change rerun ⇒ zero scans; toggling `--index-deps-json` ⇒ rescan (selftest).
4. **`--update-checkouts`**: dirty repo ⇒ rescue branch created + committed + trunk reached + manifest names the branch; local-ahead repo ⇒ visible skip, nothing merged (selftest).
5. **`--worktree`**: operator checkout byte-identical after the run (HEAD/branch/status asserted); temp worktrees all gone (`<worktreeRoot>/ua/` empty; base repo's `git worktree list` clean after prune); a foreign worktree planted beside the namespace SURVIVES a run (sweep never leaves `ua/`); `--worktree-root` honored; scan manifest sha = trunk tip; with `--build` ⇒ `fresh-by-build` + deps.json facts present; without ⇒ `deps-json-not-found` note (selftest).
6. **`--parallel`**: N=4 run produces byte-identical manifest/plan/report vs sequential; default N=2; invalid N ⇒ typed error (selftest).
7. **`--trunk`**: override drives freshness + worktree base; auto-detect unchanged without it (selftest).
8. **Fixture pins**: `F-deps` (real appdeps facts + inputs) golden pins the qualifier text and the not-affected/unknown outcomes byte-exactly; a second fixture covers the MIXED repo (lockfile + deps.json closures: lockfile rule stands, both evidence kinds named, and a divergence between them fires T10 as today); existing 124 cases + goldens untouched (GOLDEN-CHANGES).
9. **Totals**: counts swept; validator green; determinism (no wall-clock bytes — mtimes enter only cache decisions, never output text).

## 8. Out of scope (typed, honest, logged)

- **CI-definition producer discovery** — SPEC-020.
- **npm/ecosystems beyond NuGet** — V0 is NuGet-only; the provenance shape names no ecosystem.
- **Submodule worktrees** — worktree mode ignores submodules V0 (a repo with submodules scans its top tree only); real need first.
- **In-place `--build` with refresh-refusal interplay** — in-place build only runs on repos that passed freshness/rescue; refused repos are never compiled.
- **Per-repo trunk configuration, rebase/merge of ahead repos, DB persistence** — design-talk tier / refused on principle.
- **mtime spoofing** — the fingerprint is a heuristic skip-key, not a proof; the staleness qualifier (§5) remains the honesty backstop regardless of what the cache believes.

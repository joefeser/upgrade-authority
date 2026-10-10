# Features — the complete tour

Organized by the pipeline. Every behavior listed here is specified (see `docs/specs/`), implemented, and pinned by a deterministic golden corpus plus a 149-case selftest — including real tracemap scan data for the tricky cases.

## 0. Estate wrapper — `ua scan-estate`

**What it does:** one command from a folder of repo checkouts to a report (absorbs the manual scan loop → sidecar bootstrap → ingest → plan → report sequence).

- **Freshness precondition** — every repo is verified at its origin trunk tip (`origin/main`, else `origin/dev`) after a `git fetch`; behind/dirty/no-trunk/fetch-failed repos are skipped with a visible reason and recorded in `scan-estate.v1.json` (the run manifest). `--allow-stale` waives freshness (except non-git and no-origin repos); skips never fail the run.
- **SHA-cached scans** — the scans folder IS the cache: a repo whose HEAD matches the cached scan's `commitSha` (and the scan conditions: `--exclude` globs, tracemap dll hash, staleness flag) is reused, never re-scanned. Daily runs re-scan only movers; a dirty `--allow-stale` scan never silently speaks for a later clean tree.
- **Alternate-checkout dedupe** — two checkouts of one origin: the name-ordinal first wins, the later is visibly skipped (one scan per repo; combining snapshots stays a deliberate explicit act).
- **Sidecar bootstrap** — `ownership init` (or `update` when the file exists — manual assignments sacred), a minimal producer sidecar listing the delta target, and a single-change delta template. Existing files are operator data and are never overwritten; the producer template derives its package from a pre-placed real `delta.json` when flags are absent.
- **The chain** — ingest (explicit dirs, all sidecars explicit) → `plan.json` → `report.md`, all written into the out dir with canonical bytes.

## 0b. Estate scope — `--scope <file>` (scan-estate / ingest / ownership)

**What it does:** the operator declares repos out of scope (`estate-scope.v0`: `include`/`exclude` lists of repo folder names).

- **Ignored ≠ not-affected** — out-of-scope repos carry no classification and never appear as `not-affected`; the report states them ("N repos out of scope by config: …", or include-mode's count), and `scan-estate.v1.json` names every observed out-of-scope repo. Never silently dropped.
- **Applies at discovery** — scan loop, `ingest --scans-root`, and `ownership` discovery all filter by scope; excluded children still register for alternate-snapshot detection (excluding `svc` cannot let `svc-net48` sneak in). Explicit scan dirs are deliberate acts — warned, never filtered.
- **Config contradictions are typed errors** — a name in both include and exclude, or `include: []` beside an exclude list, refuses rather than guessing. One shared validator: the same malformed file produces the same message at every surface.

## 1. Ingest — `ua ingest`

**What it does:** converts [tracemap](https://github.com/joefeser/tracemap) scan output (`facts.ndjson` + `scan-manifest.json` per repo) into the planner's input format.

- **Estate-scale in one command** — `--scans-root <dir>` discovers every child scan directory (name-ordered, deterministic); a broken child (manifest without facts) warns and is skipped — one bad scan never blocks the rest.
- **Unlimited multi-repo** — lockfile rows from every repo flow into one `lockfile-rows.v2` file; alternate snapshots of the same repo are never silently combined (combining two commits of one repo is a deliberate act: pass both explicitly).
- **Sidecar explicitness at scale** — with multiple scan directories, run-wide metadata (`--producer`, `--ownership`, `--delta`) must be explicit; a stray sidecar file in one repo's directory can never silently speak for the estate.
- **Consumer evidence** — `PackageReference` (csproj/vbproj), `packages.config` (first-class, not a special case), and central package management: `PackageVersion` pins from `Directory.Packages.props` and per-project `VersionOverride`s, with file+line evidence preserved end-to-end.
- **Lockfile evidence** — checked-in `packages.lock.json` rows keyed by (repo, lockfile, TFM, package): direct/transitive relations, resolved versions, per-lockfile parent derivation. Multiple rows per package are legal; cross-lockfile and cross-TFM version disagreements become findings (not errors). Unsupported TFM groups stay honest coverage gaps — never fabricated rows.
- **Producer discovery** — `PackageProduced` facts from scans become producer entries automatically: a project declaring `PackageId`/version is a producer, no hand-written sidecar needed. Sidecar entries win on conflict; `publicationStatus` is never invented from a declaration.
- **Coverage honesty** — package-relevant problems are gaps; compile/build health (compiler diagnostics, workspace/SDK issues, buildStatus, analysisLevel) is recorded as informational notes. One structural classifier, one place; unknown shapes default to blocking (conservative).

## 2. Ownership — `ua ownership init / update`

**What it does:** generates and maintains `ownership.v0` (which team owns each repo) from the scan set.

- **init** — every discovered repo, ordinal order: `--all-self` (one team owns the estate), `--team <t>`, or `--unassigned` (checklist to stderr, zero ownerships — absent means *unknown* to the planner, never an invented team).
- **update** — manual assignments are sacred (kept verbatim); `mirrors[]` and unknown future fields pass through untouched; alias-keyed entries resolve through the file's own mirror mappings exactly as the planner does; repos missing from the scan set are **kept and warned** (a scan gap never erases an assignment).
- **One identity algorithm** — repo identity is computed by the same code ingest uses; the generator and the planner can never disagree about who a repo is.

## 2b. Drift — `ua drift` (outdated discovery)

**What it does:** answers "what's actually old" — the north-star step the hand-written delta used to paper over.

- **Feed truth, never live** — latest versions arrive as a file (`feed-versions.v1`, `asOf` echoed verbatim) or a flat folder feed of `*.nupkg` names (longest-suffix filename grammar; prerelease-suffixed and hyphenated ids parse; one version per id — latest-selection needs prerelease ordering V0 refuses). "Stated, not verified live."
- **Installed inventory** — lockfile rows (resolved truth) + exact declared pins (`declared-pin` evidence), case-insensitive package identity with an estate-spelling rule that never lets a feed-cased delta silently plan to nothing; a repo seen at two versions keeps both rows.
- **Classification parity** — behind / current / ahead via apply's own comparer (drift can never disagree with the upgrade-only policy); prerelease/floating sides are honest `unclassified` rows with D1–D3 reasons; packages missing from the feed are `unknown-feed` — visible, never guessed.
- **Delta candidates** — `--emit-deltas <dir>` writes one single-change `package-delta.v1` per distinct behind version, planner-compatible as-is; picking which to run (deepest-layer-first) stays a human/campaign decision. Canonical `drift.v1` output with `--out` (SPEC-017 convention).

## 2c. Registry — `ua registry` (the producer boundary)

**What it does:** answers "is this package ours or the internet's" from evidence, not a hand-maintained list.

- **Provenance-first producers** — every producer entry carries provenance recorded at fusion time: `project-declared` (tracemap `PackageProduced` facts), `operator-declared` (the sidecar — now the override layer, not the bootstrap; wins on conflict with the weaker source named in the warning), `ci-defined` (reserved — the tracemap CI-workflow slice; its consuming contract is pinned, and the report says the source is "not yet available" until it lands). Entries predating the spec classify via their in-band `tracemap PackageProduced (…)` evidence-note marker.
- **The boundary, visibly** — `internalPackages` (produced, any provenance) / `externalPackages` (declared external and not produced — unconsumed declarations echo verbatim) / `unknownPackages` (consumed, produced nowhere, declared nowhere — the honest residue, never defaulted to external). `anomalies.producedAndDeclaredExternal` surfaces packages that are both — never silently resolved.
- **Identity & spelling** — case-insensitive NuGet identity with drift's spelling precedence (lockfile > deps.json > declared-pin, ordinal-first within each); null-version lockfile rows still count as consumption (the packageId is the evidence).
- **A report, not a gate** — exit 0 for any successfully loaded input; unknowns and anomalies are content. Canonical `registry.v1` output with `--out` (SPEC-017 convention).

## 3. Planning — `ua plan`

**What it does:** builds the impact plan — `plan.v1`, byte-deterministic.

- **Three-state classification** — `affected` / `not-affected` / `unknown`. `not-affected` requires positive evidence (complete coverage AND the repo's own lockfile closure containing no path to any affected package). Absence of a match is never enough.
- **Four affectedness rules** — (a) producer of the target; (b) observed direct reference (PackageReference, packages.config, or CPM); (c) lockfile-evidenced transitive path; (d) unit ripple (consumers of packages produced by affected repos — an obligation independent of content exposure).
- **Wave ordering** — depth-ranked, status-partitioned (ready / conditional / provisional); producers republish before consumers bump. Conditional waves carry their gate (`condition`) verbatim; contradictions (two repos both claiming to produce a package) yield *provisional* waves blocked on a human decision — neither claim outranks the other.
- **Dependency cycles** — typed stop (`CYCLE_DETECTED`) with the full narrated path; no auto-bootstrap ever.
- **Findings** — evidenced version disagreements *inside* one repo, enumerating every (lockfile, TFM) resolution; resolutions matching the delta's from-version are marked `[delta from-version]`. The unsupported-group honesty trailer appears once per repo.
- **Publication gates** — a needed package whose sidecar says `unpublished` makes the wave conditional; "stated, not verified live" is the house style for every availability claim.
- **Determinism** — canonical serialization, fixed array orders, template-controlled strings, input-permutation invariance: the same inputs produce byte-identical plans on any machine.

## 4. Reporting — `ua report`

- Markdown rendering of the plan: summary (including the scope statement when a scope config is in play), waves with prerequisites/conditions, a repositories table (classification, ownership, action, evidence kinds, confidence, reasons), uncertainty (contradictions, gaps, findings), and **Scan notes** (compile/build health — visible, informational, never blocking).
- Escaping by construction — adversarial strings cannot forge tables or headings.
- **`--out <file>` on plan and report** — canonical bytes written directly to the file, byte-identical to redirected stdout (UTF-8, no BOM): the cure for Windows PowerShell 5.1, where plain `>` and even `Out-File -Encoding utf8` mangle or BOM the UTF-8 `→`/`—`. A flag without a value is a typed error, never a silent stdout fallback.

## 5. Apply — `ua apply` (dry-run edit generation)

**What it does:** turns a plan into the exact file edits it implies — evidence-gated, read-only.

- **Four edit-site kinds** — direct `Version` pins, CPM central pins (marked `shared` — one pin fans out to every project), `VersionOverride`s (per project; the override outranks the central pin for that project), and `packages.config` versions.
- **Upgrade-only policy** — numeric-core comparison; a pin already at or above the target is `already-satisfied` (noted, no edit); **never a downgrade**. Prerelease/unparseable pins are honestly skipped, never mangled.
- **Lockfiles are never edited** — they're restore-generated; lockfile rows inform affectedness, not edits.
- **Transitive honesty** — a repo exposed only transitively has no direct edit site; the plan says so (`no-edit-site` with the reason) rather than inventing an edit.
- **Evidence verification before any patch** — with `--repo`, each site must still match the checkout (package id + attribute + old version, in exactly one place in the evidence window; neighboring packages can never satisfy the selector). Stale or ambiguous evidence is a typed refusal. Output is `git apply`-compatible per-wave unified diffs, Windows CRLF-aware.

## 6. Push — `ua push` (execution)

**What it does:** executes verified patches as real git work. **Never merges; never force-updates.**

- **Branch per wave** — `ua/wave-N/{pkg}-{old}-to-{new}`, cut from an explicit `--base` (never guessed).
- **Verification against the tree being committed** — a temporary worktree of the base branch hosts verification + patch + commit; a clean working tree on a different branch can never slip unverified bytes in. Your checkout never switches branches.
- **Gates ride the commit** — every commit body carries `wave-status`, `condition`/`blocked-on` when gated, and the prerequisites verbatim; a cherry-picked gated commit can never look ready.
- **Staged set is the patch's own files** — `ua push` stages exactly the verified edit sites (never `-a`) and commits normally (hooks run). A pre-commit hook can still alter the commit (`git add` of new files, mutation of staged files) — hooks are NOT bypassed or validated today; treat untrusted hooks as you would in any workflow and verify results in CI.
- **Rollback strictly pre-commit** — any failure before the commit lands removes the branch and worktree (repo exactly as found); after a commit succeeds, the commit stands and the report says which later step failed.
- **Idempotency by refusal** — re-running refuses on the existing branch; nothing is ever force-updated.
- **Optional PRs** — `--pr` pushes and opens a GitHub pull request whose body is the plan rendered (delta, wave status, gates, prerequisites, per-edit evidence, findings). Fine-grained PAT with just `Contents: RW` + `Pull requests: RW`. github.com by default; `--github-api` for GitHub Enterprise (HTTPS-only — the token never rides plaintext; credentials never appear in bases or reports; a loopback carve-out exists solely for offline testing).

## 7. Engineering guarantees

- **Zero dependencies** — a .NET 10 console tool with no NuGet packages; runs offline on macOS, Linux, and Windows (verified on an isolated, network-disabled Windows machine).
- **Specified then built** — every feature has a reviewed spec (`SPEC-000`…`SPEC-021`); the fixture corpus (28 goldens) pins behaviors byte-exactly, including real scan data; the 149-case selftest covers goldens, input-permutation invariance, and every typed refusal.
- **Fidelity discipline** — golden changes require recorded, reviewed reasons (`docs/reviews/*/GOLDEN-CHANGES.md`); nothing is silently regenerated.

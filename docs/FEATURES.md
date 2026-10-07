# Features — the complete tour

Organized by the pipeline. Every behavior listed here is specified (see `docs/specs/`), implemented, and pinned by a deterministic golden corpus plus a 94-case selftest — including real tracemap scan data for the tricky cases.

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

- Markdown rendering of the plan: summary, waves with prerequisites/conditions, a repositories table (classification, ownership, action, evidence kinds, confidence, reasons), uncertainty (contradictions, gaps, findings), and **Scan notes** (compile/build health — visible, informational, never blocking).
- Escaping by construction — adversarial strings cannot forge tables or headings.

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
- **Specified then built** — every feature has a reviewed spec (`SPEC-000`…`SPEC-015`); the fixture corpus (23 goldens) pins behaviors byte-exactly, including real scan data; the 94-case selftest covers goldens, input-permutation invariance, and every typed refusal.
- **Fidelity discipline** — golden changes require recorded, reviewed reasons (`docs/reviews/*/GOLDEN-CHANGES.md`); nothing is silently regenerated.

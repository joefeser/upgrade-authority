# SPEC-022 — samplerepos: a regenerable real-estate harness over folder feeds (D020 v1)

**Status:** reviewed — round 2 SHIP verdict (fresh round-1 FIX THEN SHIP folded; records in `docs/reviews/spec-022/`)
**Author:** ZCode (coordinator); Joe's framing (2026-10-04): "own NuGet server + committed `samplerepos/` for deployable proof — should we?" → v1 = NuGet folder feeds, no Docker
**Date:** 2026-10-10 (revised same day)
**Depends on:** SPEC-018 §3 (`--folder-feed` grammar — one version per id) · SPEC-020 §2b + tracemap #851 (`--index-ci-producers`) · SPEC-011 §3 (multi-dir ingest explicitness) · SPEC-014 (ownership sidecars) · `docs/planning/12-coordinator-synthesis.md` (D020) · the WITS-derived fixture house style (public-source-grounded, provably fictional)

## 1. Purpose

Everything the corpus proves today rests on **committed scan bytes** — truthful, but static. The missing tier is a **deployable proof**: a small estate whose packages are REALLY packed, REALLY restored by real consumers, REALLY scanned, and REALLY planned — reproducible from source on any machine with the .NET SDK and tracemap, no network, no Docker. `ua drift --folder-feed`, the ci-defined provenance source, and the declared-external boundary all get exercised against truth this corpus cannot synthesize: a genuine `packages.lock.json` written by NuGet itself, and a genuine nupkg filename on disk.

This is D020 v1 — a **harness** (a script + committed sample sources + committed scan bytes), not a planner behavior change. **Zero new corpus fixtures ride this spec; the selftest and validator are untouched** (counts unchanged, full stop).

**Placement invariant (load-bearing):** `samplerepos/` lives at the repo ROOT and never under `fixtures/` or `testdata-ingest/` — anything under those roots is selftest input by construction, and the isolation of the offline deterministic tier depends on that placement.

## 2. The estate (committed source; nothing generated is committed)

`samplerepos/src/` — one fictional airline (house style: shape grounded in public NuGet/MSBuild conventions, identity provably fictional; `tools/check-public-clean.sh` must pass — and this spec extends that gate's includes to `*.yml` and `*.config`):

- **`Cassowary.Core/`** — producer, `Cassowary.Core` **1.2.0**: declared in the csproj AND packed by `.github/workflows/pack.yml` with `-p:PackageVersion=1.2.0` (same version deliberately: the equal-version both-sources shape — ci wins, project loser noted). Team `platform`.
- **`Cassowary.GroundOps/`** — producer of `Cassowary.GroundOps.Diesel` **1.0.0** (the "external-feel" package): a REAL second package owned by fictional team `ground-crew`, **declared external** via the bootstrap sidecar's `externalPackages` — the Diesel-delta run (§3 stage 4) exercises the declared-external boundary, T5/T6, and cross-team ownership against it, offline and fictional (never a counterfeit of a real package id; exact-id NuGet vacancy recorded at authoring and re-verified at implementation).
- **`Cassowary.Booker/`** — consumer: direct `PackageReference` on `Cassowary.Core` 1.2.0 (drift-current) + `Cassowary.GroundOps.Diesel` 1.0.0 (the packed version — drift-current). Every consumer csproj sets **`RestorePackagesWithLockFile=true`** (a `Directory.Build.props` at each repo root) — without it the whole tier silently degrades to pins-only. Own `nuget.config`: `<packageSources><clear/></packageSources>` + relative `feed-history/` (offline by construction; config-file-relative paths are guaranteed by the net10.0 SDK floor the estate pins).
- **`Cassowary.CrewWeb/`** — consumer pinned on `Cassowary.Core` **1.1.0** (drift-behind): same offline `nuget.config`.

Committed: sources (csproj, workflows, nuget.config), `sidecars/` (the bootstrap set below), `scans/` (stage-3 bytes: facts.ndjson + scan-manifest.json per repo), `build.ps1`, `README.md`. **Generated, gitignored (ANCHORED under `samplerepos/` — the repo already commits lockfiles under testdata-ingest and an unanchored name-ignore would trap future fixtures), never committed:** `samplerepos/feed/`, `samplerepos/feed-history/`, `samplerepos/out/`, `samplerepos/.scratch/`, and `samplerepos/**/packages.lock.json` (bin/obj ride the existing unanchored rules) (its SHA-512 `contentHash` fields bind to nupkg bytes that are not committed — regenerating lockfiles each run is the honest shape; `-LockedMode` exists only to DEMONSTRATE lock-drift detection, and its README separates hash-churn noise from graph drift).

## 3. The harness (`samplerepos/build.ps1` — PowerShell 5.1, offline)

Stages, idempotent, individually runnable (`-Stage pack|restore|scan|plan|all`). 5.1 constraints are enumerated in the README (no `&&`/ternary/`??`/`$IsWindows`; explicit `$LASTEXITCODE` checks; array splatting for conditional flags; `$HOME` not `~`; **every ua artifact goes through `ua … --out`, never `>`/`Out-File`** — BOM/UTF-16).

1. **pack** — CLEANS `feed/` and `feed-history/` (a stale old nupkg beside a new one is drift's exit-5 trap), then `dotnet pack` each producer: the current 1.2.0 into BOTH folders; plus a `-p:Version=1.1.0` historical pack of `Cassowary.Core` into `feed-history/` ONLY. Deterministic-build flags on (`ContinuousIntegrationBuild`), understanding zip timestamps still vary — nothing depends on nupkg bytes: **`feed/` is latest-only (drift's grammar); `feed-history/` carries both versions (NuGet restores fine from it). The nupkg FILENAME is the only contract.**
2. **restore** — `dotnet restore` per consumer against `feed-history/`; lockfiles land per-project (generated). With `-LockedMode`, a pre-existing lockfile is honored (`--locked-mode`) and lock DRIFT fails loudly instead of silently rewriting.
3. **scan** — for each repo: REFUSES to run if the copied tree has no `packages.lock.json` (a fresh-checkout `-Stage scan` without restore would silently degrade the tier to pins-only); copy `src/<Repo>` to `.scratch/<Repo>` (pinned names = stable repo keys), `git init`, write a scratch `.gitignore` (`bin/`, `obj/`), `git add` sources **and the lockfile** (tracked — tracemap must see it), commit with **pinned identity and dates** — `git -c user.name=… -c user.email=…` (per-invocation, no session leak) and `GIT_AUTHOR_DATE`/`GIT_COMMITTER_DATE` pinned with an **explicit `+00:00` offset** (bare dates parse in local time and would churn the epoch across machines), env vars saved/removed in a `finally`. Then real `dotnet <tracemap> scan --repo <abs> --out scans/<Repo> --index-ci-producers` (`--TracemapCli <path-to-dll>` REQUIRED — no default path in a public repo; README documents the sibling build). The stage commits `facts.ndjson` + `scan-manifest.json` and DELETES every other generated artifact (no gitignore negation games). Regeneration honesty: `scannedAt`, `scanId`, **and `commitSha`** churn regardless (the tracked lockfile's `contentHash` binds to nupkg bytes that vary — identical content does NOT yield an identical SHA); the pinned dates narrow the noise, and regen review is "trust the harness run + diff the human-readable fields", never a line-diff of hashes (verified: no ua golden pins scan SHAs).
4. **plan** — the full chain with the committed bootstrap sidecars, run TWICE:
   - **Core delta** (`sidecars/delta.json`, 1.2.0→1.3.0): `ua ingest --scans-root samplerepos/scans --producer sidecars/producer-evidence.v0.json --ownership sidecars/ownership.v0.json --delta sidecars/delta.json --out out/fixture-core` → `ua registry` (asserts `Cassowary.Core` **ci-defined**, one entry, project loser noted; Diesel produced + declared-external ⇒ the anomaly SURFACED) → `ua drift --folder-feed feed` (asserts CrewWeb 1.1.0 **behind**, Booker 1.2.0 **current**, Diesel 1.0.0 **current**, and — the tier's whole point — the installations carry **`"evidence": "lockfile"`**, not `declared-pin`) → `ua plan` + `ua report` to `out/` (asserts BOTH consumers affected).
   - **Diesel delta** (`sidecars/delta-external.json`, 1.0.0→1.1.0): same chain to `out/fixture-diesel` — NOW the delta target is the declared-external package: **T5 fires**, the Diesel producer is externally owned (**T6/T6a + `external-request-await`**), and Booker classifies affected through it. This is the run that exercises the cross-team/external machinery the estate was shaped for.
   - `ua ownership init` appears ONLY as enumeration sanity — exactly `ua ownership init --scans-root samplerepos/scans --self platform --unassigned --out out/ownership-sanity.json` (gitignored target; the committed cross-team sidecar is HAND-AUTHORED — init's one-team modes cannot express 3×platform + ground-crew, by design).

## 4. Acceptance criteria

1. `build.ps1 -Stage all -TracemapCli <dll>` on a clean checkout + .NET 10 SDK: green, producing feeds, real lockfiles, scans, and a plan whose `repos[]` shows BOTH consumers affected by the Cassowary.Core delta — verified on macOS (pwsh) and Windows PowerShell 5.1; exact commands in `samplerepos/README.md`.
2. Stage-4 assertions green: `Cassowary.Core` ci-defined; the Diesel produced-and-declared-external anomaly surfaced (never silently resolved); drift behind/current exactly as the real nupkg names dictate.
3. The committed scan bytes load through `ua ingest --scans-root samplerepos/scans` (acceptance 4's own root; `testdata-ingest/tracemap-rich/scans/` enumeration tests untouched).
4. **Selftest and validator counts are unchanged** (154 / 30); every existing golden byte-identical; `tools/check-public-clean.sh` green with `*.yml` + `*.config` added to its includes; the exact-id NuGet vacancy re-verified and recorded in the README.
5. `samplerepos/README.md` documents the estate, stages, determinism boundaries (nupkg bytes, lockfile hashes, scan clocks), the 5.1 constraint list, the placement invariant, that the public clone→build→selftest quickstart is unchanged, and the v2 path (a real feed SERVER — Docker/BaGet — out of scope here).

## 5. Out of scope (typed, honest, logged)

- **A feed server (Docker/BaGet/Artifactory)** — v2; folder feeds already exercise the full consumer truth.
- **npm/other ecosystems** — rides the multi-ecosystem versioning decision.
- **Selftest/corpus integration** — never (the harness IS the separate integration tier); a future fixture copies bytes into `fixtures/` under its own spec.
- **Estate growth beyond these four repos** — a new repo lands only with the spec that needs it.
- **`ua scan-estate` driving the harness** — the harness scans scratch copies directly; a bridge is a later slice.
- **Committing binaries** — if any future spec needs a committed nupkg, it MUST first add `*.nupkg binary` to `.gitattributes` (the CRLF-corruption blocker stands even though this spec dissolves it by not committing any).

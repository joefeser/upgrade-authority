# upgrade-authority

**Evidence-based dependency-upgrade orchestration for .NET/NuGet estates — plan the upgrade, verify the edits, open the PRs. Humans merge.**

`upgrade-authority` (the `ua` CLI) answers the question existing tooling leaves open: *a new version of a package exists — who has to move, in what order, and what exactly changes?* It builds an impact plan from real repo scans (via [tracemap](https://github.com/joefeser/tracemap)), orders the work into waves that respect producer/consumer relationships, generates the exact file edits as verified patches, and opens pull requests for the waves that carry edits (one repo checkout per push run). It never merges, never guesses, and never claims a fact it cannot trace to evidence.

```
ua scan-estate            # ONE command: freshness-check repos → cached tracemap scans →
                           #   bootstrap sidecars → ingest → plan → report
   (or, step by step:)
   tracemap scan ×N repos
   → ua ownership init      # generate ownership.v0 from the scan set (scan-estate does this for
                           #   you on first run — the standalone command is for the piecewise path)
   → ua ingest              # scans → planner inputs (multi-repo, estate-scale via --scans-root)
   → ua plan                # impact plan: who's affected, in what order, with uncertainty visible
   → ua report              # human-readable markdown
   → ua apply               # evidence-verified edit sites → git-apply-compatible patches
   → ua push                # branch + gated commit per edit-bearing wave; optional PR (--pr) — a human merges
```

Two execution notes: **push acts per repo checkout** (a plan whose edits span multiple repos is
currently split by running ingest/push per repo — per-repo plan filtering is not yet a command),
and **only waves containing edits** produce branches/PRs (a producer-only "republish" wave is
plan-level information — the PR body carries it, but there is nothing to commit in that repo).

## Why it exists

Version-bump bots solve *one repo at a time*. Real estates are graphs: one repo produces packages that other repos consume, and a single library upgrade ripples through producers before consumers. Doing that by hand across many repos is spreadsheet-driven and error-prone; doing it with per-repo bots produces a flood of uncoordinated PRs with no ordering, no cross-repo view, and no proof the bump is even applicable.

`upgrade-authority` treats the estate as the unit of work:

- **Waves, not floods** — producers republish before consumers bump, prerequisites stated explicitly ("stated, not verified live"), gated waves carry their conditions into the PR body.
- **Evidence or silence** — every fact in a plan traces to an input you control: scan facts, lockfile rows, or a declared sidecar. Absence of evidence is `unknown`, never `safe`. Nothing is inferred from names or conventions.
- **Verified edits** — `ua apply` re-checks every edit site against the actual checkout before writing a patch (package id + attribute + old version must match exactly, in one place); drifted evidence refuses rather than overwrites.
- **Offline by default** — zero NuGet packages, no telemetry, no network. The only network path is the explicit `--pr` flag.

## Quick start

```bash
git clone https://github.com/joefeser/upgrade-authority
cd upgrade-authority
dotnet build src/UpgradeAuthority        # ~5 s; no package restore beyond the SDK

# full self-check: 130+ cases incl. real committed scan data (offline)
dotnet run --project src/UpgradeAuthority -- selftest

# try it on the committed demo estate (real tracemap scans, sanitized)
dotnet run --project src/UpgradeAuthority -- ingest \
  --scans-root testdata-ingest/tracemap-rich/scans \
  --producer testdata-ingest/tracemap-rich/sidecars/producer-evidence.v0.json \
  --ownership testdata-ingest/tracemap-rich/sidecars/ownership.v0.json \
  --delta testdata-ingest/tracemap-rich/sidecars/delta.json \
  --out /tmp/demo
dotnet run --project src/UpgradeAuthority -- report /tmp/demo
```

Requires the .NET 10 SDK. Everything else is self-contained.

## From zero on your estate

The demo above runs on committed data. On your own repos, the entry point is `ua scan-estate` —
one command takes a folder of repo checkouts to a plan, and **bootstraps every sidecar you need**
on first run (nothing to create by hand):

```powershell
$ua = "run","--project","src/UpgradeAuthority","-c","Release","--"

# 0) prerequisite: the evidence provider — clone + build tracemap once
#    git clone https://github.com/joefeser/tracemap; dotnet build -c Release
#    then pass its bin\Release\net10.0\tracemap.dll below

# 1) one command: freshness-check every repo → cached tracemap scans → bootstrap → plan → report
dotnet $ua scan-estate --repos-root C:\repos --out C:\ua-estate `
  --tracemap C:\tracemap\bin\Release\net10.0\tracemap.dll `
  --self my-team `
  --delta-package Example.Package --delta-old 1.0.0 --delta-new 2.0.0

# First run CREATES the estate working dir — this is the whole init state:
#   C:\ua-estate\scans\<repo>\              cached tracemap scans (re-runs rescan only movers)
#   C:\ua-estate\ownership.v0.json          BOOTSTRAPPED — every repo this run SCANNED set to my-team;
#                                           hand-edit the real teams (repos that skipped appear when they first scan)
#   C:\ua-estate\producer-evidence.v0.json  BOOTSTRAPPED — externalPackages lists the delta target; extend it
#   C:\ua-estate\delta.json                 BOOTSTRAPPED template — swap in your real delta when ready
#   C:\ua-estate\fixture\                   planner inputs (ingested evidence + copies of the sidecars)
#   C:\ua-estate\plan.json, report.md, scan-estate.v1.json

# 2) hand-edit the two bootstrapped sidecars (ownership teams; known external packages) and/or swap
#    in your real delta.json, then RE-RUN WITHOUT the --delta-* flags (they bootstrap the template
#    only; with a real delta in place, a --delta-package that differs from it is a typed error):
#    dotnet $ua scan-estate --repos-root C:\repos --out C:\ua-estate --tracemap ... --self my-team
#    Existing sidecars are sacred — re-runs never overwrite your edits; ownership only appends
#    repos it hasn't seen (assigned to my-team until you edit them).

# 3) the boundary and the extras (each is optional, each reads the fixture dir; each needs a
#    truth file YOU provide — ua never fetches):
dotnet $ua registry C:\ua-estate\fixture                 # internal / external / unknown boundary (no inputs needed)
dotnet $ua drift C:\ua-estate\fixture --feed feed-versions.v1.json     # YOU write/export this: the versions your feed serves
dotnet $ua audit C:\ua-estate\fixture --advisories advisory-versions.v1.json   # YOU write/export this: operator rows or an OSV/GHSA export
dotnet $ua export-campaign C:\ua-estate\fixture --out C:\ua-estate\campaign    # per-wave packets + campaign manifest
```

Useful `scan-estate` flags the short form omits: `--update-checkouts` (fetch + fast-forward to
trunk tip first — a behind checkout makes evidence stale twice over), `--index-deps-json --build`
(closure evidence from build-output `deps.json` — converts "unknown" repos into answers; `--build`
compiles each checkout, which restores from your configured NuGet feeds and **executes the repo's
build scripts with your privileges — only point it at repos you trust**), `--worktree` (scan
isolated worktrees, never touching your checkout), `--scope <file>` (declare repos out of scope,
visible in the report), `--parallel <N>` (parallel fetches). Repos must be clean and at their
origin trunk tip, or they are visibly skipped — that precondition is the point.

## The commands

| Command | What it does |
|---|---|
| `ua scan-estate` | One command from a folder of repo checkouts to a report: freshness precondition (every scanned repo at its origin trunk tip, or visibly skipped), SHA-cached tracemap scans (the scans folder IS the cache — re-runs rescan only movers), sidecar bootstrap, then ingest → plan → report. `--scope <file>` declares repos out of scope (visible in the report, never silently dropped); `--exclude <glob>` passes tracemap folder exclusions through. |
| `ua ingest` | Converts tracemap scan output (facts.ndjson + scan-manifest.json) into planner inputs. Multi-repo; `--scans-root <dir>` ingests a whole scans tree in one command; `--scope <file>` filters discovery with a visible scope statement in the plan. |
| `ua ownership init/update` | Generates and maintains the ownership file (which team owns each repo) from the scan set — the last hand-maintained input. `scan-estate` bootstraps and maintains it for you on first run; the standalone command exists for the piecewise path. |
| `ua drift` | Outdated discovery: compares the estate's installed versions (lockfile rows + exact pins) against feed truth — a `feed-versions.v1` file or a flat folder feed of `*.nupkg` names — and emits a canonical `drift.v1` report (behind / current / ahead / unclassified / unknown-feed) plus single-change delta candidates (`--emit-deltas`). Never fetches live ("stated, not verified live"); classification shares apply's comparer, so drift can never disagree with the upgrade-only policy. |
| `ua registry` | The producer registry: assembles every package the estate produces (with provenance — `project-declared` from scan `PackageProduced` facts, `operator-declared` from the sidecar override layer, `ci-defined` from CI-workflow pack definitions) and cross-references everything the estate consumes against it — canonical `registry.v1` output: `internalPackages` / `externalPackages` / `unknownPackages` (consumed but neither produced nor declared — the honest residue, never defaulted) plus `anomalies.producedAndDeclaredExternal` (both at once — surfaced, never silently resolved). No feed lookups: "not-in-registry = not-ours" is stated, not verified live. |
`ua plan` | The impact plan: three-state repo classification (affected / not-affected / unknown), waves with statuses and prerequisites, findings (evidenced version disagreements inside a repo). Byte-deterministic; `--out <file>` writes canonical bytes directly (PowerShell 5.1-safe). |
| `ua report` | Markdown rendering of the plan — waves, gates, evidence, gaps, findings, scan-health notes. `--out <file>` writes canonical bytes directly. |
| `ua apply` | Derives edit sites from evidence (direct pins, CPM central pins, `VersionOverride`, packages.config), applies the upgrade-only policy (never downgrades), verifies each site against a checkout, emits per-wave unified diffs. |
| `ua push` | Executes verified patches for one repo checkout per run (edit-bearing waves): branch per wave, gated commit messages, worktree-based (your checkout never switches branches), rollback on failure, optional PR via GitHub (`--pr`; github.com or `--github-api` for GitHub Enterprise). **Never merges.** |
| `ua audit` | Advisory worst-status report: joins an operator-provided `advisory-versions.v1` truth file (operator-provided rows, or OSV/GHSA exports) against the estate's installed versions — per (repo, version) classification with worst-status arithmetic (`affected` > `unclassified` > `not-affected`), per-advisory reasons preserved, export gaps rendered `unclassified` with a manual-review note, never fabricated clean. |
| `ua export-packet` | Emits one wave as a self-describing [HACP](docs/contracts/hacp/README.md) task packet (digest-bound, RFC 8785 canonical) — the seam an external executor/reviewer consumes; ua's own execution of the same wave (`apply`/`push`) remains the reference path. |
| `ua export-campaign` | The whole plan as a `campaign-manifest.v1` — every wave's packet id + digest, input/plan content hashes over raw bytes, the complete-estate campaign id — the handoff artifact for coordinating outside ua (review tooling, queues, a human process). |
| `ua selftest` | The full offline deterministic self-check (160 cases, byte-exact on Windows/macOS/Linux). The fixture validator is a separate companion command: `node tools/validate-fixtures.mjs` (31 goldens). |

## Honesty model (the short version)

- Classification is three-state; `not-affected` requires *positive* evidence (complete coverage + the repo's own lockfile closure), never absence of a match.
- Package-relevant scan problems (unreadable lockfiles, unsupported manifests, unevidenced constraints) are **gaps**; compile/build health (broken C# files, SDK issues) is visible in **Scan notes** but never blocks — a diagnostic about code you're not touching shouldn't strand a repo as "unknown" forever.
- Version disagreements *within* a repo are **findings** ("Worker resolves Newtonsoft.Json to 12.0.3 [delta from-version] while Api resolves to 13.0.1"); disagreements *across* repos are what waves encode.
- Producer evidence from scans is declaration-only ("this project builds a package") — publication status is never invented.

See [docs/FEATURES.md](docs/FEATURES.md) for the full tour and [docs/COMPARISONS.md](docs/COMPARISONS.md) for how this differs from Dependabot, Renovate, Sourcegraph Batch Changes, and Maestro/Darc.

## Documentation

- [Features](docs/FEATURES.md) — the complete capability tour, organized by the pipeline
- [Comparisons](docs/COMPARISONS.md) — us vs the existing tooling landscape
- [Specs](docs/specs/) — every behavior is specified and reviewed before implementation (`SPEC-000`…`SPEC-025`)
- [Fixture corpus](fixtures/MANIFEST.md) — 31 golden fixtures, byte-exact, including data from real scans
- [Review history](docs/reviews/) — every review round, every finding, every disposition

## Status

V0 feature-complete: the full chain above is implemented, specified, and pinned by a 160-case deterministic selftest that runs byte-exact on Windows, macOS, and Linux (checkout line endings are pinned by `.gitattributes`; a selftest guard fails loudly on a CRLF checkout). The fixture corpus includes real tracemap scan output for multi-project estates — central package management, version overrides, multi-TFM lockfiles, legacy `packages.config`, CI-workflow producer definitions, and producer/consumer graphs.

Posture: ua is the deterministic **engine** with published contracts (plan/report/registry/drift/audit, HACP task packets, campaign manifests) — coordination, queues, and multi-user operation live outside it (the integration contract for a review/coordination product is at [docs/contracts/ua-wits-integration.md](docs/contracts/ua-wits-integration.md)). Sketches not built: the decision-return contract (consumed receipts authorizing execution), typed gates beyond wave conditions.

## License

[Apache-2.0](LICENSE). Contributions are accepted under the same license (a `CONTRIBUTING`/CLA arrangement may be added later; until then, opening a PR implies licensing it Apache-2.0).

## Credits

Built by Joe Feser with a coordinated multi-agent review pipeline (specs are fought over before code exists; every PR goes through adversarial review rounds before merge). Evidence provider: [tracemap](https://github.com/joefeser/tracemap).

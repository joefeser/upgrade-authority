# upgrade-authority

**Evidence-based dependency-upgrade orchestration for .NET/NuGet estates — plan the upgrade, verify the edits, open the PRs. Humans merge.**

`upgrade-authority` (the `ua` CLI) answers the question existing tooling leaves open: *a new version of a package exists — who has to move, in what order, and what exactly changes?* It builds an impact plan from real repo scans (via [tracemap](https://github.com/joefeser/tracemap)), orders the work into waves that respect producer/consumer relationships, generates the exact file edits as verified patches, and opens pull requests for the waves that carry edits (one repo checkout per push run). It never merges, never guesses, and never claims a fact it cannot trace to evidence.

```
tracemap scan ×N repos
   → ua ownership init      # generate ownership.v0 from the scan set
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

# full self-check: 90+ cases incl. real committed scan data (offline)
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

## The commands

| Command | What it does |
|---|---|
| `ua ingest` | Converts tracemap scan output (facts.ndjson + scan-manifest.json) into planner inputs. Multi-repo; `--scans-root <dir>` ingests a whole scans tree in one command. |
| `ua ownership init/update` | Generates and maintains the ownership file (which team owns each repo) from the scan set — the last hand-maintained input, optional where scans declare producers. |
| `ua plan` | The impact plan: three-state repo classification (affected / not-affected / unknown), waves with statuses and prerequisites, findings (evidenced version disagreements inside a repo). Byte-deterministic. |
| `ua report` | Markdown rendering of the plan — waves, gates, evidence, gaps, findings, scan-health notes. |
| `ua apply` | Derives edit sites from evidence (direct pins, CPM central pins, `VersionOverride`, packages.config), applies the upgrade-only policy (never downgrades), verifies each site against a checkout, emits per-wave unified diffs. |
| `ua push` | Executes verified patches for one repo checkout per run (edit-bearing waves): branch per wave, gated commit messages, worktree-based (your checkout never switches branches), rollback on failure, optional PR via GitHub (`--pr`; github.com or `--github-api` for GitHub Enterprise). **Never merges.** |

## Honesty model (the short version)

- Classification is three-state; `not-affected` requires *positive* evidence (complete coverage + the repo's own lockfile closure), never absence of a match.
- Package-relevant scan problems (unreadable lockfiles, unsupported manifests, unevidenced constraints) are **gaps**; compile/build health (broken C# files, SDK issues) is visible in **Scan notes** but never blocks — a diagnostic about code you're not touching shouldn't strand a repo as "unknown" forever.
- Version disagreements *within* a repo are **findings** ("Worker resolves Newtonsoft.Json to 12.0.3 [delta from-version] while Api resolves to 13.0.1"); disagreements *across* repos are what waves encode.
- Producer evidence from scans is declaration-only ("this project builds a package") — publication status is never invented.

See [docs/FEATURES.md](docs/FEATURES.md) for the full tour and [docs/COMPARISONS.md](docs/COMPARISONS.md) for how this differs from Dependabot, Renovate, Sourcegraph Batch Changes, and Maestro/Darc.

## Documentation

- [Features](docs/FEATURES.md) — the complete capability tour, organized by the pipeline
- [Comparisons](docs/COMPARISONS.md) — us vs the existing tooling landscape
- [Specs](docs/specs/) — every behavior is specified and reviewed before implementation (`SPEC-000`…`SPEC-015`)
- [Fixture corpus](fixtures/MANIFEST.md) — 23 golden fixtures, byte-exact, including data from real scans
- [Review history](docs/reviews/) — every review round, every finding, every disposition

## Status

V0 feature-complete: the full chain above is implemented, specified, and pinned by a 97-case deterministic selftest that runs byte-exact on Windows, macOS, and Linux (checkout line endings are pinned by `.gitattributes`; a selftest guard fails loudly on a CRLF checkout). The fixture corpus includes real tracemap scan output for multi-project estates — central package management, version overrides, multi-TFM lockfiles, legacy `packages.config`, and producer/consumer graphs.

Roadmap sketches (not built): persistent storage behind the planner, an action queue for long-running campaigns, vulnerability-intelligence ingestion, multi-user operation with roles.

## License

[Apache-2.0](LICENSE). Contributions are accepted under the same license (a `CONTRIBUTING`/CLA arrangement may be added later; until then, opening a PR implies licensing it Apache-2.0).

## Credits

Built by Joe Feser with a coordinated multi-agent review pipeline (specs are fought over before code exists; every PR goes through adversarial review rounds before merge). Evidence provider: [tracemap](https://github.com/joefeser/tracemap).

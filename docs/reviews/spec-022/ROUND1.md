# SPEC-022 round 1 — triage (2026-10-10)

**Quorum record:** two adversarial stand-ins (pipeline lens, corpus-isolation lens). Pipeline verdict: **REWORK** (two premise-level blockers); corpus verdict: **FIX THEN SHIP**. Per doctrine §4 the spec leaves review and returns revised, starting fresh at round 1. The reviewers verified the surviving architecture in code: the isolation proof (no selftest/validator walker can see `samplerepos/` as placed), the ci-defined both-sources shape (matches shipped `Ingest.cs` + the F-ci golden), and the drift folder grammar.

| # | Finding | Severity | Disposition → v2 |
|---|---|---|---|
| P1 | Behind-shape vs one-version-per-id: restoring an older pin needs the old nupkg in the feed, which makes drift exit 5 — acceptance 1 and 3 jointly unsatisfiable | **Blocker** | Accepted — **two generated feeds**: `feed/` (latest-only, what drift reads) and `feed-history/` (both versions, what `nuget.config` restores from). NuGet restores fine from a two-version folder; only drift's grammar refuses it — so each consumer of feeds gets the folder its contract allows |
| P2 | Stage 4 cannot run: multi-dir ingest requires explicit `--ownership`/`--delta`; the estate has neither, and drift/plan need a delta (chicken-and-egg) | **Blocker** | Accepted — committed `samplerepos/sidecars/` bootstrap set: `delta.json` (Cassowary.Core single change), `producer-evidence.v0.json` (b1b shape — the scans supply ci-defined producers), `ownership.v0.json`; stage 4 passes all three explicitly |
| P3 | tracemap requires a git repo (commitSha per fact); sources are committed inside the ua repo | Major | Accepted — stage 3 scans SCRATCH COPIES (copy → `git init` → pinned-identity/date commit → scan), pinned scratch names = stable repo keys |
| P4 | Regen diffs are noise: scanId/scannedAt/commitSha churn every run | Major | Accepted — pin `GIT_AUTHOR_DATE`/`GIT_COMMITTER_DATE`/identity (identical content ⇒ identical commitSha); state honestly that `scannedAt`/`scanId` make byte-stable regen impossible — regen review is "trust the run", diff for human-readable fields only. (Verified: no ua golden pins scan SHAs) |
| P5 | Lockfile `contentHash` churns with every re-pack — `--locked-mode` noise | Major | Accepted — **lockfiles are NOT committed** (generated each run, gitignored); the two failure modes (hash churn vs graph drift) documented; `-LockedMode` becomes an optional demonstration flag |
| P6 | "Offline by construction" false without `<clear/>` (user config inherits nuget.org) | Major | Accepted — `nuget.config` carries `<packageSources><clear/></packageSources>` + the relative `feed-history/` source; SDK floor stated |
| P7 | The external dependency has no offline acquisition path | Major | Accepted — fictionalized as a SECOND producer owned by a different fictional team (`Cassowary.GroundOps`, team ground-crew) + `externalPackages` declaration: exercises the DECLARED-external boundary + T5 + cross-team ownership, offline and fictional. Never a counterfeit of a real package id |
| P8 | `feed/` committed-or-not unstated; `.gitignore` gaps | Major | Accepted — **nothing binary is committed**: `feed/`, `feed-history/`, lockfiles, `out/`, scratch all generated + gitignored; pack stages CLEAN their folders first (the stale-nupkg exit-5 trap); the repo stays all-text |
| P9 | Packed version vs ci-claimed version mismatch | Minor | Accepted — workflow override pins the SAME version as the csproj (equal-version both-sources still exercises precedence; warning fires on distinct (provenance, version) claims) |
| P10 | PS 5.1 hazards unenumerated | Minor | Accepted — spec/README enumerate: no `&&`/ternary/`??`/`$IsWindows`; `$LASTEXITCODE` checks after every native call; array splatting for conditional flags; `$HOME` not `~`; **`ua … --out` for every artifact, never `>`/`Out-File`** (BOM/UTF-16) |
| P11 | tracemap seam: personal default path in a public repo; dll-vs-tool ambiguity | Minor | Accepted — `--TracemapCli <path>` REQUIRED (no default); README documents the expected `dotnet build` of the sibling checkout |
| P12 | D020 citation points at doc-14; the decision lives in doc-11/12 | Minor | Accepted — pointer fixed |
| P13/Q13 | Isolation claims verified true; gate sweeps committed JSONs (fine) | Question | Noted — in v2's placement invariant |
| C1 | `.gitattributes` `* text=auto eol=lf` can CRLF-corrupt committed nupkgs | **Blocker** | Dissolved by P8 (no binaries committed); if a future spec commits any nupkg it MUST add `*.nupkg binary` first — recorded as a standing rule |
| C2 | feed committed-or-not decision required | **Blocker** | Same as P8 — not committed |
| C3 | = P5 | Major | Same as P5 |
| C4 | Acceptance 5's "or grown" invites corpus coupling to harness bytes | Major | Accepted — v2 states: **zero new corpus fixtures**; selftest/validator counts unchanged, full stop; any future fixture copies bytes into `fixtures/` under its own spec |
| C5 | Placement invariant load-bearing but unstated | Major | Accepted — hard rule in v2: `samplerepos/` lives at the repo ROOT, never under `fixtures/` or `testdata-ingest/` (anything under those roots is selftest input by construction) |
| C6 | = P7 | Major | Same as P7 |
| C7 | gitignore negation trap for scans' tracked+generated mix | Minor | Accepted — "discarded" artifacts are DELETED by the stage itself; no negation patterns |
| C8 | public-clean gate blind to `.yml`/`.config` | Minor | Accepted — the spec extends the gate's includes (`*.yml`, `*.config`) |
| C9 | naming diligence: no real Cassowary Air; exact NuGet ids vacant at authoring | Minor | Noted — recorded as an acceptance line (re-verify at implementation) |
| C10 | quickstart contract unaffected — say so | Question | Accepted — README states it |

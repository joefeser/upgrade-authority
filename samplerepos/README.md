# samplerepos — the deployable-proof estate (SPEC-022, D020 v1)

A fictional airline estate ("Cassowary Air" — provably fictional; no real airline carries the name and
the exact NuGet ids `Cassowary.Core`, `Cassowary.GroundOps.Diesel`, `Cassowary.Booker` were vacant at
authoring, re-verified 2026-10-10) whose packages are **really packed, really restored, really scanned,
and really planned** — reproducible on any machine with the .NET 10 SDK and a tracemap checkout.
No network, no Docker, no feed server.

This is the **integration tier**. The offline deterministic selftest NEVER runs this harness
(placement invariant: `samplerepos/` lives at the repo root, never under `fixtures/` or
`testdata-ingest/` — anything under those roots is selftest input by construction). The public
clone → `dotnet build` → `selftest` quickstart is unchanged.

## The estate

| Repo | Team | Role | Truth it carries |
|---|---|---|---|
| `src/Cassowary.Core` | platform | producer `Cassowary.Core` 1.2.0 | declared in the csproj AND packed by `.github/workflows/pack.yml` at the same version — the **equal-version both-sources** shape (ci-defined wins, project loser noted) |
| `src/Cassowary.GroundOps` | ground-crew | producer `Cassowary.GroundOps.Diesel` 1.0.0 | the "external-feel" package: produced by another fictional team + **declared external** in the bootstrap sidecar — the produced-and-declared-external anomaly, surfaced |
| `src/Cassowary.Booker` | platform | consumer | Core 1.2.0 (drift-current) + Diesel 1.0.0 (drift-current) |
| `src/Cassowary.CrewWeb` | platform | consumer | Core 1.1.0 (drift-behind) |

Consumers set `RestorePackagesWithLockFile=true` (`Directory.Build.props`) and restore offline via
`nuget.config` with `<packageSources><clear/></packageSources>` + the relative `../../feed-history`
folder — **without that property the whole tier silently degrades to pins-only** (which is why the
scan stage refuses to run without lockfiles present).

## Running it

```powershell
# build tracemap once (sibling checkout):
dotnet build ~/src/joefeser/tracemap/src/dotnet/TraceMap.Cli -c Release
$tracemap = "~/src/joefeser/tracemap/src/dotnet/TraceMap.Cli/bin/Release/net10.0/tracemap.dll"

# then, from the repo root:
powershell -File samplerepos/build.ps1 -Stage all -TracemapCli $tracemap
# (stages are individually runnable: pack | restore | scan | plan; add -LockedMode to demonstrate lock-drift refusal)
```

Stages: **pack** (clean + pack producers into `feed/` latest-only and `feed-history/` both versions —
`feed/` is what `ua drift --folder-feed` reads, because drift's grammar refuses one id at two versions;
`feed-history/` is what NuGet restores from) · **restore** (real lockfiles) · **scan** (scratch copies
→ `git init` → pinned-date/identity commit → real tracemap `--index-ci-producers`) · **plan** (the full
ua chain twice: the Core delta — ci-defined registry entry, the anomaly, drift behind/current with
**`"evidence": "lockfile"`**, both consumers affected; and the Diesel delta — T5/T6 fire,
`external-request-await`, Booker affected through the external target). Every stage-4 assertion is
machine-checked in the script.

## Determinism boundaries (honest)

- **nupkg bytes are never stable** (zip timestamps) and are **never committed** — the FILENAME is the
  only contract (it is all drift's folder grammar reads). Nothing anywhere byte-compares a nupkg.
- **Lockfile `contentHash` fields** bind to nupkg bytes → lockfiles are regenerated every run, never
  committed (gitignored). `-LockedMode` exists to DEMONSTRATE lock-drift detection: with it, a
  pre-existing lockfile is honored and drift fails loudly instead of silently rewriting. Hash-only
  churn after a re-pack is expected noise; a graph change is real drift.
- **Scan bytes**: `scannedAt`, `scanId`, and `commitSha` churn on regeneration (the tracked lockfile's
  contentHash binds to varying nupkg bytes, so identical trees do not yield identical SHAs — the
  pinned `+00:00` dates and `git -c` identity in the scan stage narrow the noise, nothing more).
  Regeneration review is "trust the harness run + diff the human-readable fields", never a line-diff
  of hashes. Verified: no ua golden pins any scan SHA.
- **Committed**: sources, sidecars, `scans/*/facts.ndjson` + `scan-manifest.json` (the ci-scans
  precedent — every other generated artifact is deleted by the stage itself).

## PowerShell 5.1 constraints honored by build.ps1

No `&&`/`||` chaining, no ternary/`??`, no `$IsWindows` (use `$env:OS`); explicit `$LASTEXITCODE`
checks after every native call; conditional flags via array splatting (empty-string args bite);
`$HOME` not `~` for native paths; **every ua artifact goes through `ua … --out`** — never `>` or
`Out-File` (both write BOM/UTF-16 on 5.1); pinned git dates carry an explicit `+00:00` (bare dates
parse in LOCAL time and would churn across machines) and are env-saved/removed in a `finally`;
identity rides `git -c user.name=… -c user.email=…` per invocation.

## The HACP seam (SPEC-023)

The plan stage also exports each run's waves as **HACP v0.3-candidate task packets**
(`out/packet-core.json`, `out/packet-diesel.json`) via `ua export-packet` — ua's first
contract-record surface for external coordinators (WITS-shaped). The pinned schemas live at
`docs/contracts/hacp/` (Apache-2.0, copied from joefeser/hacp), the field mapping and constants in
`docs/contracts/ua-hacp-profile-v0.1.md`. ua's own emission-time checks enforce the contract shape;
for full ajv validation on a developer machine with the hacp checkout + node:

```powershell
node -e "const {default:A}=require('<hacp>/node_modules/ajv/dist/2020.js');const fs=require('fs');const ajv=new A({strict:false});ajv.addSchema(JSON.parse(fs.readFileSync('<hacp>/schemas/v0.3-candidate/common-defs.schema.json')));const v=ajv.compile(JSON.parse(fs.readFileSync('<hacp>/schemas/v0.3-candidate/task-packet.schema.json')));console.log('VALID:',v(JSON.parse(fs.readFileSync('samplerepos/out/packet-diesel.json'))))"
```

## v2 path (out of scope)

A real feed SERVER (Docker/BaGet for an Artifactory-feel), multi-ecosystem (npm) estates, and
bridging `ua scan-estate` to drive this harness — each its own future slice.

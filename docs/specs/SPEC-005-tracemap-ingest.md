# SPEC-005 — Tracemap ingest converter

**Status:** draft — round 1 review (brief-006)
**Author:** ZCode (coordinator)
**Date:** 2026-10-02
**Depends on:** SPEC-001 (input schemas), SPEC-003 (planner), tracemap checkout @ local main (schema-pinned)

## 1. Purpose

Bridge real tracemap scan output to the planner's input format. `ua ingest` reads a tracemap output directory (facts.ndjson + scan-manifest.json) plus optional sidecar inputs (producer-evidence.v0, ownership.v0) and a delta, and emits a fixture-directory the planner can consume directly.

## 2. Inputs (tracemap output, schema-pinned from source)

### 2.1 `facts.ndjson` (one JSON object per line — `code-fact.v1` schema)

Each fact: `{ factId, scanId, repo, commitSha, factType, ruleId, evidenceTier, evidence: { filePath, startLine, endLine, snippetHash, extractorId, extractorVersion }, properties: { key: value } }`

Relevant `factType` = `"PackageReferenced"`. Key `properties` (source-verified from ScanEngine.cs):
- `packageName` (or `package`, `name` — alias resolution order: packageName|package|name)
- `version` (safe values only; redacted → `versionHash` + `redactionReason`)
- `ecosystem` (e.g. `"nuget"`, `"npm"`, `"python"`)
- `manifestKind` (e.g. `"csproj"`, `"packages.config"`, `"packages.lock.json"`, `"package.json"`)
- `dependencyGroup` (e.g. `"PackageReference"`, `"packages.config"`, `"lockfile"`, `"dependencies"`)
- `targetFramework` (optional)
- `dependencyScope` (e.g. `"runtime"`)

### 2.2 `scan-manifest.json`

`{ scanId, repoName, remoteUrl, branch, commitSha, scannerVersion, scannedAt, analysisLevel, buildStatus }`

### 2.3 Lockfile facts (`factType = "PackageReferenced"` with `manifestKind = "packages.lock.json"`)

Properties include: `dependencyRelation` (`"direct"` / `"transitive"` / `"project"`), `resolvedVersion`, `dependencyNames` (array or null if >256 chars).

## 3. Mapping rules (tracemap → planner input)

| Tracemap field | Planner field | Rules |
|---|---|---|
| `repo` | `Fact.repo` | Direct copy |
| `properties.packageName` (alias-resolved) | `Fact.packageId` | Direct copy |
| `properties.version` | `Fact.declaredConstraint` | Direct copy (or `"redacted:" + versionHash` if null) |
| `properties.manifestKind` | `Fact.format` | `csproj/vbproj` → `"packagereference"`; `packages.config` → `"packages.config"`; `packages.lock.json` → skip (goes to lockfile); `Directory.Packages.props` → `"cpm"`; else `"other"` |
| `properties.manifestKind` | `Fact.constraintSource` | Same as format for lockfile; `csproj` → `"Project"`; `packages.config` → `"packages.config"`; CPM central → `"Directory.Packages.props"`; CPM override → `"VersionOverride"` |
| `properties.targetFramework` | `Fact.tfm` | Direct copy (empty → null) |
| `evidence.filePath` | `Fact.path` | Direct copy |
| `commitSha` | `Fact.commitSha` | Direct copy |
| (multiple facts same repo+pkg) | `Fact[]` | One planner Fact per tracemap fact (dedup by repo+pkg+path) |

**Lockfile mapping** (facts with `manifestKind = "packages.lock.json"`):
| Tracemap | Planner lockfile-rows.v0 |
|---|---|
| `repo` | `repo` |
| `properties.packageName` | `rows[].packageId` |
| `properties.dependencyRelation` | `rows[].type` (`"direct"` → `"direct"`, `"transitive"` → `"transitive"`, else → `"unknown"`) |
| `properties.resolvedVersion` | `rows[].version` |
| `properties.dependencyNames` | `rows[].names` (null preserved) |

**Scan coverage**: every repo in the manifest (or all distinct `repo` values across facts) gets `{ repo, status: "complete" }`. If a repo has zero facts, it gets `{ repo, status: "gaps", gaps: ["no facts emitted"] }` — we don't assume silence means safety.

## 4. Sidecar pass-through

`producer-evidence.v0.json` and `ownership.v0.json` are copied from the input directory to the output directory if present. The delta (`package-delta.v1`) is copied as-is. If sidecars are missing, the converter emits empty ones (with a warning printed to stderr) — the planner will report producer-unknown and ownership-unknown correctly.

## 5. Command

```
ua ingest <tracemap-output-dir> --out <fixture-dir> [--producer <path>] [--ownership <path>] [--delta <path>]
```

If `--producer`/`--ownership`/`--delta` are omitted, looks for those files in `<tracemap-output-dir>`.

## 6. Acceptance criteria

1. A synthetic tracemap output (matching the exact schema above) converts to a valid fixture directory.
2. The resulting fixture passes `ua plan` and `ua selftest`.
3. Redacted versions (versionHash instead of version) are handled — the constraint shows `"redacted:<hash>"`, not a crash.
4. `packages.lock.json` facts produce valid `lockfile-rows.v0.json`.
5. Fact dedup: same repo+pkg+path from multiple scans → one Fact.
6. Repos with zero facts get coverage gaps, not silence.

## 7. Out of scope

SQLite reading (use `tracemap export` first if you only have an index); Python/npm ecosystems beyond schema acceptance (planner only understands NuGet today); the reverse direction (planner → tracemap).

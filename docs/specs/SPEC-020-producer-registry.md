# SPEC-020 — Estate producer registry + the "us vs the internet" boundary

**Status:** implemented — review converged round 2 (SHIP; records in `docs/reviews/spec-020/`)
**Author:** ZCode (coordinator); Joe (2026-10-09): "determine if the repo produces nuget or npm packages, so we can cross-reference us vs a random package on the internet"
**Date:** 2026-10-09
**Depends on:** SPEC-001/002 (producer-evidence.v0, fusion rules — externalPackages) · SPEC-011/014 (scan-set discovery) · SPEC-018 (canonical report conventions) · tracemap `PackageProduced` facts (shipped) · **tracemap CI-workflow indexing (NOT shipped — delegated slice, §2b)**

## 1. Purpose

The producer/external boundary — "is this package ours or the internet's?" — is today a hand-maintained sidecar (`externalPackages`), bootstrapped by scan-estate with a single guess (the delta target). At estate scale that's backwards: the estate can **derive its own registry** from evidence. This spec adds `ua registry`: assemble every package the estate produces (repo → packages, with provenance), cross-reference everything the estate consumes against it, and report the boundary — internal producers, external dependencies, and unknowns (consumed but neither produced nor declared external) — visibly. The manual sidecar demotes from bootstrap to override.

## 2. Discovery sources (provenance-first, never guessed)

- **(a) project-declared** — tracemap `PackageProduced` facts (shipped): a project declaring `PackageId`/version produces that package. Already flows into producer-evidence at ingest (B1(b)).
- **(b) ci-defined** — the gap Joe identified (2026-10-08): pack definitions living in `.github/workflows` (`dotnet pack` with properties overridden in CI) rather than the project file. **This is a tracemap-side slice — delegated to the tracemap agent's channel** (issue-managed in that repo; we do not implement their side). When their facts land, ua's consuming contract is pinned NOW so the fix lands once: **the fact's provenance property maps to the entry's `provenance` field** (whatever shape they choose, its provenance-carrying property is recorded at fusion exactly as PackageProduced's is); review their PR against this line and record the landed shape here. Until then the registry reports the gap honestly: repos whose workflows mention `dotnet pack`/`npm publish` are NOT inferred (parsing CI from ua would violate "ua never reads repos") — the registry simply lacks that source and says so in its provenance summary.
- **(c) operator-declared** — the existing `producer-evidence.v0` sidecar (externalPackages + hand-declared producers). **Wins on conflict** (fusion rules unchanged); the sidecar is now the override layer, not the bootstrap.

**Provenance is recorded ONCE, at fusion time** (not reconstructed later, where the losing source is already gone): `ProducerEntry` gains an optional `provenance` field — sidecar entry ⇒ `operator-declared`, `PackageProduced` fact ⇒ `project-declared`, the future CI fact ⇒ `ci-defined` (a backward-compatible, explicitly-versioned `producer-evidence.v0` addition; absent ⇒ the entry predates this spec — recognized by the in-band marker scan-appended entries already carry (`EvidenceNote: "tracemap PackageProduced (…)"`): marker absent ⇒ `operator-declared`, present ⇒ `project-declared`). The registry reads exactly that field. Conflicts resolve operator > ci > project (the existing sidecar-wins rule, extended); the loser is noted in the fusion-time warning that already fires (Ingest's producer-conflict warning names both versions — extended to name both provenances).

## 3. Command

```
ua registry <fixture-dir> [--out <file>]
```

- Consumes the loaded producer-evidence (post-fusion, as the planner sees it) + the input's package/lockfile evidence (SPEC-018 inventory surface).
- **Identity & consumed**: package identity is case-insensitive (OrdinalIgnoreCase — NuGet ids; matching Drift's grouping) with Drift's spelling rule (lockfile spelling wins, else pin; ordinal-first among variants). `consumed` = ANY consumer fact or ANY lockfile-class row for the package (null-version rows count — the packageId is the evidence); external declarations for never-consumed packages are echoed in `externalPackages` (declared truth is the boundary's input, not its guess); produced-but-never-consumed packages report the producer entry's own spelling (only one exists). All three boundary lists are ordinal-sorted.
- Output `registry.v1` (canonical JSON, house writer; `--out` per the SPEC-017/018 convention — stdout without it, sanitized bypass, bare flag refuses):
  - `producers[]`: `{repo, packageId, producedVersion?, provenance}` — ordinal by (repo, packageId).
  - `boundary`: `{internalPackages, externalPackages, unknownPackages, anomalies}` — internal = produced by the estate (any provenance); external = declared external and NOT produced; **unknown = consumed by the estate, produced nowhere, declared nowhere** — the honest residue (visible, never defaulted to external). `anomalies.producedAndDeclaredExternal: [ids]` lists packages BOTH produced and declared external (the scan-estate bootstrap template creates exactly this on a stale externalPackages list) — **surfaced, never silently resolved**: the planner's own behavior for that overlap is unchanged (out of scope here, logged as a follow-up finding), but the report never lets it vanish.
  - `provenanceSummary`: counts per provenance + the ci-defined source's availability note (until the tracemap slice lands: "ci-defined source not yet available — project-declared + operator only").
- Exit 0 for any successfully loaded input — unknowns and anomalies are report content, not failures; typed errors per house conventions (1 usage, 3 load/`UaException`, 4 internal).

## 4. What it deliberately does NOT do

- No planning behavior changes: the registry is a **report over the same fused evidence** the planner uses. A future slice may let `scan-estate` seed `externalPackages` from a prior registry (closing the bootstrap loop) — separate spec if wanted.
- No npm: V0 is NuGet-only; consumed packages are the NuGet inventory. The output shape carries no ecosystem field beyond what inputs already have (when npm lands, it versions this schema).
- No network, ever: the boundary is estate-evidence vs declared-external, not a feed lookup ("us vs a random package on the internet" resolves to: not-in-registry = not-ours; what the internet knows about it stays feed truth's problem — SPEC-018's `--feed`).

## 5. Acceptance criteria

1. Registry over the committed estate (**corelib + svc + billing + shipping** scans — corelib is where the corpus's only `PackageProduced` fact lives; producer sidecar with `producers: []`, the b1b shape): Contoso.Core listed with `project-declared` provenance (the fused corelib fact); with an operator sidecar claiming it instead ⇒ one entry, `operator-declared`; Newtonsoft.Json lands external (sidecar-declared, not produced); a consumed-but-unproduced-undeclared package lands in `unknownPackages` — visible, never defaulted (selftest).
2. Operator-override: sidecar producer for a package also scan-discovered ⇒ one entry, provenance `operator-declared`, weaker source noted (selftest).
3. Canonical output: byte-golden (`--out` == stdout capture, no BOM; sanitized identical; bare `--out` refuses) (selftest).
4. `F-registry` fixture golden pins the boundary sets and provenance summary byte-exactly (real ingest inputs); existing 124+ cases untouched (GOLDEN-CHANGES).
5. Determinism: two runs byte-identical; no clock bytes (selftest).
6. Counts swept; validator grows registry.v1 structural checks (closed key set, ordinal orders, **pairwise** boundary disjointness internal ∩ external = ∅ etc., case-variant identity across the three lists) (selftest + validator).

## 6. Out of scope (typed, honest, logged)

- **tracemap CI-workflow indexing** — the other agent's channel; this spec consumes whatever shape lands and records it (§2b).
- **Seeding sidecars from registries, feed lookups, license/vulnerability enrichment** — later slices.
- **npm/other ecosystems** — versioned follow-up when multi-ecosystem arrives.

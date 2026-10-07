# PR #6 round 1 triage — tracemap ingest (2026-10-03)

**Quorum batch:** **Kiro REWORK** (real-scan-verified, 4 blockers + 6 majors + 7 minors — logged verbatim: [kiro-review.md](kiro-review.md)) · Qodo 9 findings (inline + summary body) · Codex 7 inline · Sourcery 5 inline. Raw: [quorum-inline-raw.json](quorum-inline-raw.json) · [review-bodies.json](review-bodies.json). **All accepted. None rejected.**

**The core insight (Kiro's, verified by running real tracemap scans):** SPEC-005's input model was derived from reading tracemap's C# source, but tracemap's ACTUAL scan output differs in six critical ways. The synthetic test data agreed with the converter because both were written to the same wrong model. This is exactly the "goldens must come from demonstrated reality, not from spec" lesson (D019/fidelity rule) applied to inputs.

## Root-cause fix: use real tracemap output as the test fixture

Kiro scanned a two-repo estate (CPM + VersionOverride + multi-target lockfiles / packages.config legacy) with tracemap `main @ 32ab4c3`. That scan output becomes the committed acceptance fixture. The converter is then validated against reality, not against my model of reality.

## Disposition table

| Finding | Source | Fix |
|---|---|---|
| **B1: coverage `complete` for failed/partial scans** | Kiro (kill-one) | `status: gaps` whenever `buildStatus != "success"`, `analysisLevel != full`, or any `AnalysisGap` fact exists for the repo. Carry gap details. |
| **B2: lockfile version collapse across TFMs/lockfiles** | Kiro | Rows keyed by (lockfilePath, tfm, packageId). Multiple lockfile repos → typed error (not silent break). Multi-lockfile support is a planner change (separate PR). |
| **B3: transitive parent fabrication** | Kiro | Derive `via` from parent rows' `dependencyNames` (comma-split). When no parent found, `via` stays null; planner says "parent unknown" instead of picking one. |
| **B4: CPM/VersionOverride don't exist in tracemap output** | Kiro | SPEC-005 gains an explicit limitation: CPM constraints arrive as `version: ""` from csproj facts. Ingest emits a gap per empty-version fact. The `Directory.Packages.props` mapping rows removed. |
| **Empty versions ≠ null versions** | Kiro + Codex | `version: ""` → gap "constraint not evidenced", NOT `declaredConstraint: "unknown"` |
| **`dependencyNames` is comma-joined string** | Kiro + Codex | Split on `,` → array; preserve null; use for `via` derivation |
| **M1: missing delta exits 0** | Kiro + Qodo | Non-zero exit; write to temp dir then rename (atomic output) |
| **M2: one-repo-per-scan** | Kiro | Accept multiple scan dirs: `ua ingest <dir1> <dir2> ... --out <fixture>` |
| **M3: repo identity from directory name** | Kiro | Key on normalized `remoteUrl` when present; `repoName` as label |
| **M4: placeholder values ("?", "unknown")** | Kiro + Qodo | Skip malformed facts; record ingest gap naming the factId |
| **M5: no committed acceptance fixture** | Kiro | Commit Kiro's real scan output as `fixtures/ingest-real/tracemap/`; add selftest case |
| **M6: sidecars not validated** | Kiro + Qodo | schemaVersion check; missing ownership → loud gap in plan, not just stderr |
| **VersionOverride before csproj in switch** | Qodo 6 | Reorder pattern match |
| **Npm/python facts enter NuGet path** | Kiro m2, Qodo 4 | Ecosystem check: non-nuget → skip + gap |
| **Fact sort order** | Kiro m4 | Sort output facts by repo |
| **Redacted prefix normalization** | Kiro m5 | Normalize `version-hash:` prefix |
| **TFM on lockfile rows** | Kiro m6 | Preserve TFM in lockfile rows |
| **`--out` required** | Kiro m7 | Error if missing |
| **SPEC-005 number conflict** | Kiro m1 | Queue updated (this PR = SPEC-005; harness = SPEC-006) |
| **`PackageReference Update` items** | Kiro m3 | Skip + gap (modification, not a consumer edge) |

## Plan

1. Update SPEC-005 with the real input model (Kiro's table = the authority)
2. Commit Kiro's real scan output as the acceptance fixture
3. Rewrite Ingest.cs: coverage from buildStatus/AnalysisGap, lockfile per-TFM keying, via from dependencyNames, CPM gaps, remoteUrl identity, multi-dir input, typed errors
4. Add ingest selftest case
5. Delete the stray `--out/` directory

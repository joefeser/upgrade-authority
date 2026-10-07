## Kiro review — brief-006, round 2 (delta)

Reviewed `8fc915e` against my round-1 findings and `docs/reviews/pr6-round1/TRIAGE.md`.

I re-ran `ua ingest` on two inputs:
- **(A)** the committed `fixtures/ingest-real/tracemap/` scans;
- **(B)** my original round-1 scans: tracemap `main @ 32ab4c3`, `svc` + `legacy`, unchanged.

Then `ua plan` on both, plus targeted probes. `ua selftest` still passes 33/33, but it doesn't include an ingest case (see M5).

**Verdict: FIX THEN SHIP.** The rewrite fixes the premise: multi-dir input, coverage driven by tracemap's own status, `dependencyNames`-derived `via`, and empty-version gaps all work on real output. Two round-1 blockers are still open, though: B2 is unchanged in code despite the comment, and B3 is fixed only on the ingest side. The new "atomic" write also adds a blocker on Windows.

> **Correction to the triage:** the committed fixture is **not** my round-1 scan. Mine has 87 and 37 facts, including the `packages-lock-group-unsupported` net48 gap, a `VersionOverride="0.9.0"`, and Newtonsoft.Json as a **transitive** row via Contoso.Core. The committed scans have 49 and 24 facts, Contoso.Core 2.0.0, Newtonsoft.Json as a **direct** lockfile row, and no override or unsupported-group gap. It is real tracemap output (good), but from a simpler estate. So the two cases that drove B2 and B3 aren't in the corpus. The repro inputs are listed at the end; please commit those scans too.

### Your six questions

**1. B1 (kill-one): ✅ resolved, but with a new major (R2-M1).** On both inputs, `svc` and `legacy` are now `status: gaps`, carrying `buildStatus: FailedOrPartial`, `analysisLevel: Level1SemanticAnalysisReduced`, `knownGaps`, and the AnalysisGap messages. No real-scan repo becomes `not-affected`.

**2. B2: ❌ not resolved.** The typed multi-repo error works (exit 2). But the collapse I actually reported was **within one repo, across lockfiles and TFMs**, and the code still dedups by `packageId` alone:

```csharp
// Key by (packageId, tfm) — same package at different TFMs can resolve differently
if (!rows.Any(r => r.PackageId == pkg))
```

The comment says (packageId, tfm); the condition checks packageId only. Results:
- **(B), my scan:** Worker's `Contoso.Core 0.9.0` / `Newtonsoft.Json 12.0.3` are dropped. The plan carries only Api's 1.0.0 / 13.0.1.
- **(A), your committed fixture, same defect:** Worker resolves Newtonsoft.Json **12.0.3** (net8.0 and net48), Api resolves **13.0.1**, and only 13.0.1 survives.

TFM isn't emitted on rows at all. The plan therefore still hides the vulnerable version in the fixture added to prove the fix.
*Fix:* key rows by (lockfilePath, tfm, packageId), and add `tfm` (and `lockfilePath`) to `LockRow`. Planner input can't hold several rows per package today, so until it can, raise a **typed error when one package resolves to different versions within a repo**. Don't keep the first one.

**3. B3: ⚠️ ingest side ✅, planner side ❌.** `Contoso.Core → Newtonsoft.Json` works. In (B) the row gets `via: "Contoso.Core"`, and the plan says "svc -> Contoso.Core -> Newtonsoft.Json". With a decoy direct row `Aardvark.Logging` inserted, the plan still correctly names Contoso.Core.

But TRIAGE says "when no parent is found, `via` stays null; planner says 'parent unknown'". **The planner was not changed** (the Planner.cs diff only makes `NormBasic` internal). With `via` removed, the same probe still prints:

> transitive path svc -> **Aardvark.Logging** -> Newtonsoft.Json **evidenced by** svc's lockfile rows

The fallback at Planner.cs L599–600 (`?? LockOf(repo).First(r => r.PackageId != Target && r.Type == "direct")`) is still there. `via: null` is the normal case on real data: tracemap **omits** `dependencyNames` when the joined list exceeds 256 chars, which is common for framework meta-packages.
*Fix:* delete the fallback and render "parent not evidenced". Also, the text "names=null treated as unknown child detail" is printed even when names are present; make it conditional.

**4. B4: ⚠️ partial.** `version: ""` no longer becomes a fake constraint ✅, and each fact produces "constraint not evidenced (CPM/central version or redacted)". But those ingest gaps are **only printed to stderr** (`foreach (var g in ingestGaps) Console.Error.WriteLine(...)`). They never reach `package-evidence.v0.json`, the plan or the report. The plan shows `declaredConstraint: ""` with no gap. TRIAGE M6 promised "loud gap in plan, not just stderr".
*Fix:* append ingest gaps to the affected repo's `scanCoverage.gaps`, or to a new top-level `ingestGaps` that the planner passes through to `uncertainty.gaps`. Also, SPEC-005 still contains the CPM rows: **the spec wasn't updated** (no `docs/specs/` change in this commit, although it is plan step 1 in the TRIAGE).

**5. M2: ✅ yes, `ua ingest <svc-dir> <legacy-dir> --out <fixture>` matches the workflow.** It works on both inputs, and repo identity is per-manifest. One note for the real estate (minor): at estate scale, consider also accepting `--scans-root <dir>`, which globs `*/scan-manifest.json`.

**6. Overall: the converter now handles what tracemap emits structurally, but the rewrite introduced or missed the following.**

### New blocker

**R2-B1. The "atomic" output can destroy the existing output and fails across volumes, which is the default layout on Windows.** `Run` writes to `Path.GetTempPath()`, then calls `Directory.Delete(outDir, true)` and then `Directory.Move(tempDir, outDir)`. Two problems:
- `Directory.Move` can't cross volumes. On Windows that throws "Source and destination path must have identical roots" whenever `%TEMP%` (C:) and the repo (e.g. D:) differ. Here, `/tmp` is tmpfs and the workspace is a different device, and **every ingest to the workspace failed** with `internal error: Invalid cross-device link`, exit 4.
- The delete runs **before** the move. I created `/projects/sandbox/keepme/input/x` and ran `ua ingest … --out /projects/sandbox/keepme`. The move failed, and **`keepme` was gone**.

*Fix:* create the temp dir as a sibling of `outDir` (same parent, so the same volume). Move the old dir aside, move the new one in, then delete the old one, rolling back on failure. Add a selftest/CI case where `--out` sits on a different path root from `%TEMP%`.

### New majors

**R2-M1. Coverage can never be `complete` on real data.** Ingest compares against `"Success"` and `"Full"`, but tracemap emits `buildStatus ∈ {Succeeded, FailedOrPartial, NotRun}` and `analysisLevel ∈ {Level1SemanticAnalysis, Level3SyntaxAnalysis, …Reduced}` (ScanEngine.cs L240–245). `"Full"` is a coverage *label* on other facts, never an `analysisLevel`. A perfectly clean scan therefore gets `gaps: ["scan buildStatus: Succeeded", "analysisLevel: Level1SemanticAnalysis"]`, and **`not-affected` becomes unreachable**. That breaks the three-state promise in the opposite direction.

There is also a design question for Joe. Package-reference facts are Tier2Structural and don't depend on compilation. Today every compiler diagnostic, which is nearly every real repo, makes package coverage `gaps`. The right gate is probably package-relevant gaps only: project-file read/parse failures, `packages-lock-*` gaps, and unsupported manifests. Compiler and workspace diagnostics would stay informational.
*Fix:* use exact tracemap values. Decide the gap scope, and add one committed scan with `Succeeded` / no package gaps whose golden is `complete`.

**R2-M2. A bad sidecar still exits 0 and leaves an unplannable fixture.** An `ownership.v9` file produces a "schemaVersion mismatch" warning, the copy is **skipped** (no file at all), ingest exits 0, and `ua plan` then fails with `missing input: …/ownership.v0.json` (exit 3). The missing-sidecar warning also renders as `"no  input"` because `Path.GetFileName(null)` is empty. The schema check is `content.Contains("\"ownership.v0\"")`, so a file that merely *mentions* the string passes.
*Fix:* parse it and compare `schemaVersion`; exit non-zero on a mismatch.

### Carried / minor
- **M5 (partial):** real scans are committed ✅, but there are no golden `input/*.json` files and no selftest case. `fixtures/ingest-real/` has no `golden/`, so selftest skips it. The four checks above (B1–B4) can only be verified by hand.
- **Committed scans have `commitSha: "unknown"`** (the "Git commit SHA unavailable" gap). The converter passes `unknown` through into facts. Per tracemap's own principle ("no scan without repo and commit SHA"), carry that as a coverage gap, which already happens via knownGaps ✅, and don't put it in a SHA field that `code-fact.v1` requires to be 40-hex. Use null.
- `via` is also set on **direct** rows: in (A), Newtonsoft.Json is direct, but `via: Contoso.Core`. Restrict it to transitive rows.
- With `remoteUrl` present, the repo key becomes `github.com/org/svc`, while sidecars keyed by `svc` silently stop matching, so everything becomes ownership-unknown. Warn when a sidecar repo matches no ingested key (round-1 M3, second half; still open).
- The SPEC-QUEUE renumbering is listed in TRIAGE but isn't in the diff.

### Repro inputs to commit as fixtures
My round-1 scans (`svc`: CPM + `VersionOverride` + two lockfiles + net48 unsupported group; `legacy`: packages.config + broken file), with goldens asserting:
1. both repos `gaps`;
2. **two** Newtonsoft.Json resolutions in `svc` (13.0.1 Api, 12.0.3 Worker), or a typed error;
3. `via: Contoso.Core` on the transitive row;
4. a "constraint not evidenced" gap that **reaches the plan**;
5. a `via: null` variant whose plan says "parent not evidenced".

**Path to SHIP:** R2-B1 (sibling temp dir + safe swap), B2 (real per-TFM keying or a typed error), B3 planner fallback removal, and the four-assertion fixture above. R2-M1's success values are a two-string fix; the gap-scope question goes to Joe.


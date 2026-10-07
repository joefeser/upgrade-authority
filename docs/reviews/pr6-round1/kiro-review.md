## Kiro review — brief-006 (SPEC-005 tracemap ingest), round 1

**Verdict: REWORK (SPEC-005 §2–§3 only; most of `Ingest.cs` is reusable).**

The premise is right: a converter, with tracemap kept as the evidence source. The problem is that SPEC-005's "source-verified" input model doesn't match what tracemap actually emits. The synthetic test data was written to that model, so it agrees with the converter and can't catch the mismatch.

**How I checked.** I tested against **real tracemap output**, not hand-written facts:
- Built tracemap `main @ 32ab4c3` with .NET 10.0.401.
- Scanned a two-repo sample estate with `tracemap scan`:
  - `svc`: CPM, a `VersionOverride`, two projects each with a `packages.lock.json`, one of them multi-targeted `net8.0;net48`.
  - `legacy`: packages.config, plus a file that doesn't compile.
- Ran `ua ingest`, then `ua plan` on the output.

`ua selftest` still passes 33/33. The defects are all on the ingest path, which selftest doesn't exercise.

### What tracemap actually emitted vs. what SPEC-005 assumes

| SPEC-005 assumption | Real tracemap output (sample scan) | Effect after `ua ingest` → `ua plan` |
|---|---|---|
| CPM: `Directory.Packages.props` → `cpm`; overrides → `VersionOverride` | Tracemap never reads `Directory.Packages.props` or the `VersionOverride` attribute. CPM `PackageReference`s arrive as `manifestKind: csproj`, **`version: ""`** | `declaredConstraint: ""`, format `packagereference`. **Worker's `VersionOverride="0.9.0"` pin is lost entirely.** The planner's CPM path (F7) can never fire on real data. |
| Redaction = `version` null → `versionHash` | Missing versions are `version: ""`, not null. Lockfile redaction uses the prefix `versionHash: "version-hash:<h>"` | Empty constraints pass as "known". Lockfile redaction is ignored (`Version = resolvedVersion ?? version` → null). |
| `dependencyNames` is an array or null | It is a **comma-joined string** (`"Newtonsoft.Json"`). `code-fact.v1` properties are `string`-only | The converter discards it (`Names = null`) and sets `Via = null` with the comment "tracemap doesn't store the parent". **It does**: the parent row's `dependencyNames` lists its children. |
| One lockfile per repo, deduped by package id | One fact per **(lockfile, TFM, package)**. `svc` has two lockfiles: Api resolves Newtonsoft.Json **13.0.1**, Worker resolves **12.0.3** | Rows deduped by `packageId`, first wins. **Worker's 12.0.3 (the vulnerable version) disappears**, and the plan only shows 13.0.1. |
| Every repo with ≥1 fact is `complete` | Both scans report `buildStatus: FailedOrPartial` and `Level1SemanticAnalysisReduced`. There are 40+ `AnalysisGap` facts, including `packages-lock-group-unsupported` for Worker's net48 group | **Both repos are marked `complete` with no gaps.** |
| One input dir holds a multi-repo `facts.ndjson` + manifest | `tracemap scan` is **one repo per output dir**, with a single-repo `scan-manifest.json` | There is no way to ingest an estate; I had to `cat` the facts files together by hand. Only the first manifest is read. |

### Blockers

**B1. Coverage claims `complete` for repos that tracemap marked as failed or partial.** Ingest ignores `buildStatus`, `analysisLevel`, `knownGaps` and every `AnalysisGap` fact. `complete` is exactly what lets the planner classify a repo `not-affected` "with positive evidence" (SPEC-003 §2). So a failed scan with a lockfile and no matching package becomes a confident `not-affected`. That breaks tracemap's own rules ("Failed build is not a clean repo"; "partial analysis must be labeled partial") at the one point where they matter.
*Fix:* `status: "gaps"` whenever the manifest isn't fully successful, or whenever any `AnalysisGap` exists for the repo. Carry `gapKind`/`message` into `gaps[]`.
*Acceptance check:* a real scan with `buildStatus: FailedOrPartial` must never yield `not-affected`.

**B2. Version-specific lockfile evidence is silently collapsed.** Deduping by `packageId` across all lockfiles and TFMs keeps whichever row comes first. In my scan the plan reports Newtonsoft.Json 13.0.1 for `svc`, while Worker actually resolves **12.0.3**. For a vulnerability-driven upgrade tool, keeping the safe version and dropping the vulnerable one is the worst possible direction.

`break; // V0: one lockfile (one repo)` then also drops every other repo's lockfile without a warning (brief Q3). **It should be a typed error, not a silent break.**
*Fix:* rows keyed by (lockfilePath, tfm, packageId). Multiple lockfiles need the planner's per-repo lockfile input; that has been an open item since PR #4 M3. Until it exists, refuse.

**B3. The plan names a transitive parent that isn't evidenced.** With `via` null, the planner fills the gap with the first `direct` lockfile row. I added a direct row `Aardvark.Logging` and the plan stated: "transitive path svc -> **Aardvark.Logging** -> Newtonsoft.Json **evidenced by** svc's lockfile rows". That is a fabricated citation. The real parent (Contoso.Core) **is** recoverable from `dependencyNames`, which ingest throws away.
*Fix:* split `dependencyNames` on `,` into `names`; derive `via` from the parent rows whose names contain the child. Where no parent is found, `via` stays null, and the planner must say "parent unknown", not pick one. That planner rule belongs in this PR because ingest is what feeds `via: null`.

**B4. CPM and `VersionOverride` don't exist in tracemap's output, but SPEC-005 §3 maps them.** The CPM rows in the mapping table describe input tracemap never produces. Real CPM repos lose every constraint (`""`) and every override.
*Fix:* SPEC-005 must state the limitation explicitly. Then either:
- ingest emits a gap per `version: ""` fact (for example "constraint not evidenced — CPM/central version not indexed"), or
- add a tracemap slice that indexes `PackageVersion`/`VersionOverride` (it fits tracemap's static-evidence charter; same pattern as the `PackageProduced` recommendation in PR #1 B1).

Empty-string versions must never pass as a real constraint.

### Majors

**M1. The required delta can be missing and ingest still exits 0.** `error: required delta not found` is printed, then exit 0, leaving a fixture dir with no `delta.json`. `ua plan` fails later with "missing input". *Fix:* exit non-zero and don't write a partial directory (write to a temp dir, then rename).

**M2. The estate input model is wrong** (see table). SPEC-005 needs `ua ingest <scan-dir>... --out` (several scan dirs, each with its own manifest), or it should consume `tracemap combine` output. The latter has `index_sources` with per-repo provenance, and the PR #2 design notes say `package-impact` already uses it.

**M3. Repo identity is taken from `fact.repo` (= `manifest.repoName`, the directory name).** Two estates' `service` repos collide, which undoes PR #4's round-2 fix (`org-a/service` ≠ `org-b/service`). `remoteUrl` is ignored. *Fix:* key on the normalized `remoteUrl` when present, with `repoName` as a label. Warn when sidecar repo keys (ownership/producer) match no ingested repo. Otherwise, a typo in `ownership.v0.json` silently becomes "ownership unknown" for the whole estate.

**M4. Placeholder values invent data.** Missing `repo`/package/`filePath`/`commitSha` become `"?"`. A missing version becomes the string `"unknown"`, which is indistinguishable from a real constraint. *Fix:* skip the fact and record an ingest gap that names the factId.

**M5. No acceptance case is committed.** SPEC-005 §6 lists six criteria. The PR contains **none** of the synthetic tracemap input and no selftest case for `ingest`; only the converter's *output* is committed. That output sits in a stray **`--out/`** directory at repo root (5 files, almost certainly `GetOpt` swallowing `--out` as the scan dir). *Fix:*
- delete `--out/`;
- commit a real `tracemap scan` output (sanitized; the two repos above work fine) as `fixtures/ingest-*/tracemap/`, with golden `input/*.json`;
- add a selftest case.

Generating it from tracemap itself is the only way to make "source-verified" true.

**M6. Sidecars are copied without validation.** `producer-evidence`/`ownership`/`delta` are `File.Copy`'d with no `schemaVersion` check (the PR #4 M2 gap again). Defaulting `selfTeamId: "unknown"` silently makes every repo `ownership: unknown`, so nothing gets scheduled and the plan has no waves, with only a stderr warning. That should be loud in the plan itself: a gap, or a typed refusal.

### Minors
1. Spec numbering: SPEC-QUEUE.md defines **SPEC-005 = demonstrated-case acceptance harness**. This PR takes the number for ingest without updating the queue.
2. `manifestKind` is always `csproj` for project files (tracemap hardcodes it), so the `vbproj` arm and most of the `dependencyGroup` fallbacks are dead code. The `_ => packagereference` default also maps unknown ecosystems (npm `package.json`) to NuGet PackageReference. Use `other` plus a gap, as SPEC-005 §3 itself says.
3. `PackageReference Update="…"` items are emitted by tracemap as ordinary references. They modify an existing reference and don't add one, so they shouldn't count as a consumer edge.
4. Output order: `Facts = evidence.Values.SelectMany` follows dictionary insertion order, not repo order. Sort by repo for byte-stable output (the permutation discipline from PR #3).
5. `redacted:` + the lockfile's `version-hash:` prefix would give `redacted:version-hash:…`. Normalize the prefix. Passing the hash through is fine (Q2): tracemap already publishes it, and it is a 32-char truncated hash of a version string, not a secret.
6. TFM is dropped from lockfile rows. Multi-targeted repos (net48 + net8.0) need it, because the vulnerable version often lives only in the legacy TFM.
7. Default `--out ingested-fixture` in the cwd overwrites silently. Require `--out`.

### Answers to the brief
- **Q1 schema fidelity:** no. CPM, empty versions, `dependencyNames` type, lockfile cardinality and the `AnalysisGap`/manifest status fields all differ from the spec (table above).
- **Q2 redaction:** passing through `versionHash` is safe, but empty-string versions are the real gap.
- **Q3 lockfile grouping:** `break` is wrong. Error now, multi-lockfile later.
- **Q4 coverage gap message:** "no facts emitted" is fine for the zero-fact case, but that case is rare. The common case, a partial scan with facts, is marked `complete` (B1).
- **Q5 missing from the synthetic data:** `AnalysisGap` facts, `buildStatus`, empty CPM versions, comma-string `dependencyNames`, multiple lockfiles/TFMs, and one-repo-per-scan.

**Kill one:** `status: complete` for any repo with ≥1 fact (B1). Every other defect makes a plan less precise. This one makes it confidently wrong, as soon as the tool meets real data.


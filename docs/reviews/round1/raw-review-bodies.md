===== REVIEW by sourcery-ai (COMMENTED) =====
Sorry @joefeser, this account has used its review budget of 1,500,000 diff characters for the last 7 days.

You can request another review in 3 hours and 10 minutes by commenting `@sourcery-ai review`. [Upgrade](https://app.sourcery.ai/login?connection=github&git_namespace_id=260923&from_surface=github_bot&intent=rate_limit_hit) to get a review now.

===== REVIEW by qodo-code-review (COMMENTED) =====


===== REVIEW by chatgpt-codex-connector (COMMENTED) =====

### 💡 Codex Review

Here are some automated review suggestions for this pull request.

**Reviewed commit:** `f9066931af`
    

<details> <summary>ℹ️ About Codex in GitHub</summary>
<br/>

[Your team has set up Codex to review pull requests in this repo](https://chatgpt.com/codex/cloud/settings/general). Reviews are triggered when you
- Open a pull request for review
- Mark a draft as ready
- Comment "@codex review".

If Codex has suggestions, it will comment; otherwise it will react with 👍.




Codex can also answer questions or update the PR. Try commenting "@codex address that feedback".
            
</details>

===== REVIEW by joefeser (COMMENTED) =====
## Kiro review — brief-001, round 1

**Verdicts**
- **SPEC-000 (V0 definition): FIX THEN SHIP**
- **SPEC-001 (evidence & graph model): FIX THEN SHIP** (1 blocker; the premise "consume tracemap" holds)

I checked the claims against tracemap `main` @ `32ab4c3` (2026-09-11), not just the brief. Most of the findings below come from that check.

---

### Blocker

**B1. SPEC-001 §4 rung 4 ("V0's primary rung") doesn't exist in tracemap.** Tracemap emits no producer-side facts: no `IsPackable`, `<PackageId>`, `GeneratePackageOnBuild`, `<Version>` or `.nuspec` extraction anywhere in `src/dotnet` or `rules/rule-catalog.yml`. Its package facts (`PackageReferenced`, package-config, lockfile rows) are **consumer-side only**. Rungs 1–3 are also out of reach in V0 (the spec admits this for 1 and 3; rung 2 "nuspec-in-index" doesn't exist either). So as written, the producer map has **zero real inputs**. The only ways to fill it would be naming heuristics, which the spec bans, or inventing producers, which F3 bans. Expect the implementation to quietly fall back to `PackageId == project name`.
*Fix (pick one, write it into §2):* (a) add input #3, a sanitized `producer-evidence.v0` sidecar. Mark its facts as `source: fixture-declared` and treat them as rung 4. It is honest and can be built this week. (b) Add a small tracemap slice that emits `PackageProduced` facts from project properties and nuspec files. That fits tracemap's static-evidence charter. Doing (a) now and (b) in parallel is the right call.
*Acceptance check:* "Every `ProducerClaim` cites an `Evidence` whose kind appears in the pinned input schema. No claim may be derived from a project, assembly, or repo name."

### Majors

**M1. F4's transitive path can only be reconstructed sometimes, and the spec doesn't say when.** Tracemap's package-upgrade-impact requirements say it does not inspect transitive dependency graphs. The only transitive signal is a checked-in `packages.lock.json`. Its rows carry `direct`/`transitive`/`unknown` plus the child dependency names, and that names field is **set to null when the joined string exceeds 256 chars** (`ProjectFileReader.cs` ~L419). Many estates don't commit lockfiles at all.
*Acceptance check:* split F4 into **F4a**, with a lockfile, where the path is reconstructed through the intermediate library, and **F4b**, without a lockfile, where transitive exposure must be `Unknown` with a gap. It must **not** be "excluded".

**M2. "Excluded" is where silent resolution will happen (answers Q3).** SPEC-000 §2.4 asks for a reason for every exclusion. The cheap way to implement that is "no matching reference → not affected", which turns missing evidence into a clean result. That contradicts SPEC-001 §4 ("missing observation yields `Unknown`").
*Fix:* inclusion becomes three-state: `affected | not-affected (with positive evidence: scan coverage complete, lockfile present) | unknown`.
*Acceptance check:* a repo whose scan has project-file gaps lands in `unknown`, never `not-affected`.

**M3. A contradiction is recorded, but the wave still gets placed.** Agrees with Qodo #3. The spec says "record contradictions, never silently resolve", and it also says "higher rungs outrank lower". When the planner must emit an order, "outrank" *is* the silent resolution.
*Fix:* define "outrank" as affecting **confidence display only**, never which claim survives. Any repo downstream of a contradicted producer goes into a wave marked `provisional/blocked-on: <contradiction id>`. Also define "corroboration count" as the number of **independent** sources (distinct repo + commit + rule). Otherwise two facts from the same file get counted twice.
*Acceptance check:* F3-conflict's downstream consumers appear only in a provisional wave that points to the contradiction.

**M4. F2 needs ownership input that §2 never declares.** Tracemap doesn't emit team ownership, so "ownership boundary is external" has no source. That leaves invention, or a CODEOWNERS guess.
*Fix:* add an explicit `ownership.v0` input (repo → team, plus a self-team id). When a repo is missing from it, ownership is `Unknown`, not "ours".
*Acceptance check:* F2's R4 step is labeled `external — request/await`, not an executable step for our team. Agrees with Qodo #9.

**M5. The fixtures are prose, so the fidelity rule has nothing to hold.** "Orders correctly" and "readable without training" can't be checked. The incident this rule exists to prevent was exactly the kind of substitution that vague expectations allow.
*Fix:* before build, commit each fixture as **input files plus a golden expected `plan.json`**. Pull that piece of SPEC-005 forward. F1 is also underspecified: "R3 consumes R2's output". What package is that, and who publishes it? Every edge needs to be named.
*Acceptance check:* `plan.json` for F1–Fn equals the golden file, or the diff is recorded under the §7.1 substitution process.

**M6. Missing demonstrated shapes (Q4).** This list is ordered by how often the case shows up in real alerts:
- **F5: third-party package flowing through an internal package.** For example, a Dependabot/GHAS alert on a public package pulled in transitively via internal P1. The likely wave: R1 bumps and publishes → consumers bump P1. This is the most common real alert flow, and F1–F4 only cover internal deltas.
- **F6: packages.config legacy repo.** D005 says it is ingested, but no fixture exercises it.
- **F7: central package management** (`Directory.Packages.props` plus `VersionOverride`). One edit fans out across projects, and an override hides the pin.
- **Cycle:** R1 consumes from R5, which consumes P2. V0 must at least *detect and refuse* the cycle rather than emit an order. Full handling can stay in SPEC-003.

### Minors

1. **ReleaseUnit (Q5):** acceptable for V0 *if* each unit carries `basis: assumed-same-repo | evidenced` in the JSON, not just in prose. Add an **F1b** with two independently versioned package sets in one repo. The expected output is "one unit, assumption flagged". That locks in the warning, so fixing the assumption later is a deliberate golden-file change. Note that tracemap can't currently evidence shared versioning (see B1).
2. `PackageIdentity` = id + version conflates two things. Producer claims are about the id, while consumer edges are about versions. Split them into `PackageId` and `PackageVersion?`, so an unknown version can still form an edge (agrees with Qodo #6).
3. Repo identity key: tracemap facts carry `sourceRepo` URLs plus scan IDs. Define the normalization (case, `.git` suffix, mirrors) or the same repo will appear twice.
4. SPEC-000 §5.1 "on Joe's work machine" isn't repeatable by agents. Add a Windows, network-disabled run (for example `windows-latest` with network blocked). Joe's hands-on run stays as the final sign-off.
5. The SPEC-001 §2 "blocking task" has partly been done here. Pinned `package-delta.v1` = `{version, sourceRepo, sourceCommitSha, changes[{id, packageName, ecosystem, changeType, oldVersion, newVersion}]}` per `samples/package-deltas/package-delta.example.json`. Record the commit SHA in the spec.
6. Doctrine (context only): agree with Qodo #5. "Zero *new* majors ends review" can close review with round-1 majors still unfixed. Use "zero *open* blockers/majors" instead.

### Q6 — the one thing to kill
**Kill rungs 1–3 from the V0 text and make rung 4 real (B1).** Listing evidence V0 can't obtain will lead to stub interfaces and "confidence" fields with nothing behind them. Meanwhile the one rung V0 actually depends on has no producer. Everything else in these specs is fixable with acceptance checks. Producer evidence is the core of the product, and right now it has no source.

**Scope verdict (Q1):** less than one week is realistic **only** with fix B1(a) and golden fixtures (M5). Without B1, the week goes into discovering that the producer map is empty.



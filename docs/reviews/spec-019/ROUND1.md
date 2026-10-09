# SPEC-019 round 1 — triage (2026-10-09)

**Quorum record:** stand-ins (same as prior spec rounds — external quorum rides the implementation PR).

| # | Finding | Severity | Disposition | Fix applied |
|---|---|---|---|---|
| 19-1 | deps.json facts carry `version` and are NOT excluded from the consumer-fact path (only `packages.lock.json` is skipped) — rule-(b) affectedness from build output, CPM correlation pollution, drift `declared-pin` rows from bin/, apply edit sites inside `bin/` | **Blocker** | Accepted | §4: skipped exactly like lockfile rows — lockfile-class only; acceptance pins zero consumer facts with bin/ paths |
| 19-2 | The freshness signal never reaches the planner (facts say `unknown` by design; run manifests aren't inputs) — the amended not-affected rule was unimplementable | **Blocker** | Accepted | §5.3: `build-freshness.v0.json` fixture input (scan-estate writes, ingest copies, LoadEngine reads); ABSENT ⇒ conservative (deps.json closure alone never proves not-affected); Q1/Q2 qualifier templates pinned |
| 19-3 | Worktree-mode cache semantics undefined (HEAD-vs-trunk-tip mismatch ⇒ perpetual rescans; fingerprinting the operator's bin certifies a tree never scanned; `.wt-*` leftovers) | Major | Accepted | §6: cache bumps to v2; worktree reuses on the trunk-tip sha with its fingerprint RECORDED at scan time (never recomputed against the checkout); `.wt-*` cleanup mirrors tmp/old; §7.3 scoped |
| 19-4 | Parallel fan-out would make the alternate-dedupe first-wins rule timing-dependent | Major | Accepted | §2: eligibility/dedupe/freshness decisions sequential name-ordinal BEFORE fan-out; only fetch/scan/build parallelize |
| 19-5 | "`stale` ⇒ unknown" stated absolutely — demotes repos with independent lockfile closures; contradicts SPEC-003/F10 | Major | Accepted | §5.3: stale demotes only repos whose SOLE closure is build-resolved; lockfile rule stands byte-identical; mixed closures name both kinds |
| 19-6 | Cache metadata versioning/comparison unpinned; trunk recorded as-typed splits entries | Minor | Accepted | v2 bump (old ⇒ rescan); trunk recorded RESOLVED |
| 19-7 | Rescue branch `<yyyymmdd>` put wall-clock bytes in the manifest; same-day collision; identity/untracked scope unstated | Minor | Accepted | `ua/rescue-<sha8-of-HEAD>`; all stray work incl. untracked; repo's own git identity (none ⇒ typed skip) |
| 19-8 | "newest mtime" certifies the wrong set (indexed subset); boundary + committer-date unpinned | Minor | Accepted | OLDEST (min) mtime — conservative; committer date; `>=`; subset imprecision stated with the qualifier as backstop |
| 19-9 | `provenance` vs SPEC-007's identical-fields rule; validator blind to input rows | Minor | Accepted | provenance joins the identity set (SameRow); group-invariance stated |
| 19-10 | Named a nonexistent classification ("not-allowed"); `<basis>` ambiguous vs no-wall-clock rule | Minor | Accepted | not-affected; Q1/Q2 byte-exact templates; basis = layer name, never timestamps |
| 19-11 | Build-failure semantics unstated; not-found Note text missed the excluded-by-globs cause | Minor | Accepted | build failure ⇒ visible skip; Note text extended |
| 19-12 | Drift would present deps.json resolutions as `evidence: "lockfile"` | Question → accepted | §4: `evidence: "deps.json"` (the one-word drift change) |
| 19-13 | No mixed lockfile+deps.json fixture | Question → accepted | §7.8: second fixture (mixed closures, divergence fires T10 as today) |


## Post-round-2 amendment (Joe-directed, 2026-10-09)

Joe: worktrees should share ONE configurable root (`--worktree-root <dir>`, e.g. `D:\ua-worktrees` —
less clutter), with a stated risk analysis for worktree deletion (prior incident: an agent killed a
worktree and its session files; base repo was never git-damaged). Amendment applied to §3.2:
configurable root with a mandatory `ua/` namespace inside it (a shared root can never mix our
satellites with anyone else's); `--force` removal ONLY for worktrees we created under the namespace
(build outputs make them untracked-dirty — that's what --force is for); crash sweep = recorded paths
+ exact patterns under `<root>/ua/` only, then `git worktree prune`; base-repo safety stated in the
spec (satellite semantics — deletion cannot corrupt the base). Acceptance 7.5 extended: a foreign
worktree beside the namespace must SURVIVE a run.

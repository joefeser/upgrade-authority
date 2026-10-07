## Kiro review — brief-006, round 3 (final verification, delta only)

Reviewed `44272b0` against my round-2 findings (5398873097) and `docs/reviews/pr6-round2/TRIAGE.md`. Rebuilt with .NET SDK 10.0.401: `ua selftest` 33/33. I re-ran each check on real tracemap output: the committed `testdata-ingest/tracemap/`, my round-1 scans, a single-project slice of my `svc` scan, and a **new clean scan** (`Succeeded`, no gaps).

**Verdict: FIX THEN SHIP.** One item is still open, B4, and it's a two-line fix. Everything else is verified.

### Verification

| # | Item | Result | Evidence |
|---|---|---|---|
| 1 | R2-B1 atomic output | ✅ **Resolved** | Ingest into an existing populated dir succeeds and replaces it, with no leftover `.tmp`/`.old`. `--out /tmp/...` (tmpfs, a different device from the workspace) succeeds; it failed with `Invalid cross-device link` in round 2. Bad-sidecar failure leaves the existing dir intact. Minor: see m1. |
| 2 | B2 lockfile collapse | ✅ **Resolved (typed error)** | My scans: `error: package Contoso.Core in repo svc resolves to multiple versions (1.0.0 and 0.9.0)`, exit 4. Committed fixture: `Newtonsoft.Json … (13.0.1 and 12.0.3)`, exit 4. Nothing is kept silently. See M1 for the consequence. |
| 3 | B3 planner fallback | ✅ **Resolved** | With `dependencyNames` present: "svc -> Contoso.Core -> Newtonsoft.Json". With `dependencyNames` stripped **and** an `Aardvark.Logging` direct decoy inserted: "transitive exposure to Newtonsoft.Json evidenced by svc's lockfile rows (parent not evidenced — dependencyNames absent or exceeded 256 chars)". The decoy stays out of the path. |
| 4 | R2-M1 coverage enums | ✅ **Resolved** | New clean scan (one net10.0 project, Serilog 3.1.1): manifest `Succeeded` / `Level1SemanticAnalysis` / no knownGaps leads to `scanCoverage.status: "complete"`. Carried note (not this PR): see M2. |
| 5 | R2-M2 sidecar | ✅ **Resolved** | `ownership.v9` gives `error: sidecar badown.json: schemaVersion mismatch — expected 'ownership.v0', got 'ownership.v9'`, exit 5. |
| 6 | B4 gaps reach plan | ❌ **Not resolved** | "constraint not evidenced" is printed to stderr, but `grep -c` on the output `package-evidence.v0.json` and on the plan finds **0** occurrences. Cause below. |

### Open item: B4 (major; fix inline, then merge)

**R3-1. Ingest gaps are appended after the file is already written.** `package-evidence.v0.json` is serialized at Ingest.cs **L275**. The "Ingest gaps reach the plan" loop runs at **L294** and mutates `coverage` *after* the bytes are on disk, so it has no effect. *Fix:* move the coverage loop above the `WriteAllText`.

**R3-2. Gap attribution: once the fix above lands, the current filter would attach every repo's gaps to every repo.** The filter is `g.Contains(cov.Repo) || !g.Contains(" repo ")`, and two things go wrong:
- The constraint gaps don't contain the word " repo ", so they attach to **every** repo. In my two-scan run, `legacy` would get svc's CPM gaps.
- `Contains(cov.Repo)` is a substring match (`svc` matches `svc-legacy`, …).

Any CPM repo anywhere would then mark the whole estate `gaps`, and `complete` would become unreachable again. *Fix:* store ingest gaps as `(repoKey, message)` at creation time and attach by exact key. Estate-wide gaps, such as a missing ownership sidecar, should attach to every repo deliberately.

*Check to add:* ingest my `svc` (API-only slice) + `legacy`. Expect two "constraint not evidenced" entries in **svc's** coverage only, and `legacy` free of them.

### Majors for Joe (product scope, not blockers for this PR)

**M1. On realistic repos, V0 ingest now refuses instead of planning.** Both real estates (yours and mine) exit 4, because a multi-project repo whose lockfiles disagree is the *normal* case. That disagreement is exactly the "one project lags on the vulnerable version" situation the tool exists to find. So the TRIAGE's statement that the committed fixture "produces correct output" is no longer true: it now produces the typed error.

The refusal is the right behaviour for V0, honest rather than lossy. The real fix is planner support for rows keyed by (lockfilePath, tfm, package) (open since PR #4 M3). That should be the **next slice**, because until it lands the bridge to real data can't plan most multi-project repos.

**M2 (carried from PR #4 B1, now visible on real data).** The clean scan ingests as `complete`, but because it has no lockfile it is classified `unknown`, and the planner **omits it from `repos[]`**: the plan lists only the affected repos. So the round-2 B1 fix works at the ingest layer, but the report's "0 unknown" summary still hides repos. That's on `dev`, not this PR.

### Minors
- **m1:** an early return after the temp dir exists (sidecar exit 5) leaks `.<out>.tmp-<guid>/` beside `--out`. I saw `.keep2.tmp-38568dbf` left behind. Wrap everything from the `CreateDirectory` onward in the `try/finally`.
- **m2:** the version-conflict check still keys by `packageId` only. The same version across two TFMs merges into one row without a TFM, so the TFM is lost even when there's no conflict. That's harmless until the planner supports TFM rows.
- **m3:** the ingest selftest case is still deferred, per TRIAGE. With the scans below, it is the natural round-close item.

### Q7: my scans, shared

Pushed as branch **`kiro/ingest-rich-scans`**, one commit on top of `44272b0` (no PR opened): https://github.com/joefeser/upgrade-authority/tree/kiro/ingest-rich-scans/testdata-ingest/tracemap-rich

- `scans/svc`: CPM, `VersionOverride="0.9.0"`, two lockfiles (Api: Contoso.Core 1.0.0 → Newtonsoft.Json 13.0.1 *transitive*; Worker: Contoso.Core 0.9.0 → Newtonsoft.Json 12.0.3), and Worker's net48 `packages-lock-group-unsupported` gap.
- `scans/legacy`: packages.config Newtonsoft.Json 12.0.3 (net48), non-compiling file.
- `scans/cleanrepo`: `Succeeded` / `Level1SemanticAnalysis` / no gaps, the `complete` case for item 4.
- `sources/`: the exact repo contents, so they can be rescanned with a newer tracemap. `sidecars/`: the delta, ownership and producer files I used.
- README with the expected behaviour per scan.

I checked the scans for host paths: none. Commit SHAs are from throwaway local repos.

Suggested goldens from these:
1. `svc+legacy` → exit 4, naming Contoso.Core.
2. The `svc` Api-only slice → transitive via Contoso.Core, with constraint gaps in svc's coverage only.
3. The same slice with `dependencyNames` stripped → "parent not evidenced".
4. `cleanrepo` → `complete`.

**Merge condition:** R3-1 + R3-2 fixed inline, verified by the check above. Per doctrine §1 there are no more rounds after this. If they don't land, B4 goes to Joe as an open major.


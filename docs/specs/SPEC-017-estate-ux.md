# SPEC-017 — `ua scan-estate` + estate scope + incremental scans + `--out`

**Status:** implemented — review converged round 2 (SHIP; records in `docs/reviews/spec-017/`); branch `feature/scan-estate`
**Author:** ZCode (coordinator); requested by Joe 2026-10-07 (work-machine session) and 2026-10-08 (proven manual pipeline)
**Date:** 2026-10-08
**Depends on:** SPEC-011 (`--scans-root` ingest) · SPEC-014 (`ua ownership`) · SPEC-004 (report/CLI) · runbook `docs/planning/15-signoff-runbook.md` §4 (the manual procedure this absorbs, Steps 1–3)

## 1. Purpose

The manual estate pipeline is proven end-to-end (tracemap scan loop → `ua ownership init` → sidecars → `ua ingest --scans-root` → plan → report; verified on a real 130-repo estate 2026-10-08) but costs six hand-run steps and three hand-written files. This spec turns runbook §4 Steps 1–3 into **one command**, makes re-runs **incremental** (SHA-cached scans — the scans folder IS the cache, no database), lets the operator **declare repos out of scope** without lying about coverage, and ends the PowerShell 5.1 redirection encoding pain (`--out` on `plan`/`report` writes canonical bytes directly).

The north-star campaign flow gains its step 1 (freshness precondition): every scanned repo is provably at its origin trunk tip, or visibly skipped.

## 2. Command surface

Usage grammar (not paste-safe script — PS 5.1 one-liners in the runbook):

```
ua scan-estate --repos-root <dir> --out <dir> [--tracemap <dll>] [--scope <file>]
               [--self <teamId>] [--allow-stale] [--exclude <glob>]...
               [--delta-package <id> --delta-old <v> --delta-new <v>]

ua ingest  <scan-dirs…|--scans-root <dir>> --out <fixture-dir> [--scope <file>] …   # new optional flag
ua ownership init|update … [--scope <file>]                                          # new optional flag
ua plan   <fixture-dir> [--out <file>]     # --out/-o new; stdout unchanged without it
ua report <fixture-dir> [--out <file>]
```

Every value flag (including `--out`, `-o`, `--scope`, `--tracemap`, `--repos-root`) requires a value: a flag as the last token ⇒ typed error, exit 1 (never a silent stdout fallback). `--exclude` is repeatable and is parsed by collecting ALL occurrences — it is a **tracemap scan pass-through** (folder globs like `"Migrations/**"`), NOT scope exclusion (§3); the name is kept for tracemap familiarity and the help text says so.

## 3. Estate scope config (`estate-scope.v0`)

**File** (operator-authored, referenced explicitly — never auto-discovered; a stray `scope.v0.json` in a discovered scan dir is ignored with a warning note, same rule as SPEC-011 §3):

```json
{"schemaVersion":"estate-scope.v0","include":["alpha","beta"],"exclude":["legacy"]}
```

- Both arrays optional. **Absent or empty arrays are dropped at validation**; a scope that validates to nothing behaves exactly like no scope input (no plan field, no report line). A present-but-empty `include` beside a non-empty `exclude` ⇒ typed error (`include:[] names nothing — delete the file or list repos`). Entries are **repo checkout folder names** — identical to scan-dir names by construction (the scan loop names `<out>/scans/<repo-folder-name>`). Matching is **exact, case-sensitive (ordinal)** — deterministic; a differently-cased name is a different repo (planner-identity precedent).
- `include` present ⇒ ONLY listed names are in scope. `exclude` always subtracts. A name in BOTH lists ⇒ **typed error** (the config contradicts itself; fix the file). Duplicates within one list ⇒ warning + collapse.
- **One shared validator** (single implementation, SPEC-014's "one algorithm" doctrine) runs at every surface: `scan-estate`, `ingest --scope`, `ownership --scope`, and fixture load. Same malformed file ⇒ same typed message everywhere. Exit codes: file-not-found ⇒ exit 1 (usage, naming the path); invalid JSON or wrong `schemaVersion` ⇒ exit 5 at ingest/ownership (sidecar-class); at plan load ⇒ typed error exit 3 (`UaException`, as all load errors).
- **Hard rule: ignored ≠ not-affected.** Out-of-scope repos are never classified (they have no evidence and claim none) and never silently vanish:
  - `scan-estate` does not scan them; the run manifest records `status: out-of-scope` (name listed).
  - `ua ingest --scans-root --scope` skips their scan dirs at discovery (note to stderr) so a stale cached scan cannot speak for an excluded repo. **Excluded children still register their repo name for alternate-snapshot detection** — excluding `svc` must not let `svc-net48` sneak in as the "first" snapshot; the alternate is then skipped with the alternate note (visible, correct). Discovery (both `ingest` and `ownership`) also ignores **dot-prefixed children** (`.name.tmp-*`/`.name.old-*` swap leftovers must never be discovered as scans; note to stderr).
  - `ua ownership init|update --scope` skips them at discovery the same way, same alternate rule.
  - The report carries a **scope statement** (§5). Each surface names what IT knows: the report states the mode, the configured include count, and the configured exclude names; the scan-estate run manifest (§4.4) additionally names every observed out-of-scope repo (the wrapper sees the repos-root universe; the plan never pretends to).
- **Explicit scan dirs are deliberate acts and are never filtered.** Passing an out-of-scope repo's dir explicitly to `ingest`/`ownership` warns ("out of scope by config — included because explicit") and includes it.
- Ingest copies the validated scope file verbatim into `<fixture>/input/scope.v0.json` (the flag's bytes win if a previous copy existed); the loader validates and the plan echoes it (§5). No scope input ⇒ no scope field, no report line — **zero churn for every existing fixture**.

## 4. `ua scan-estate` — the wrapper

One preflight: `git --version` fails ⇒ typed error (git is required for freshness; failing per-repo with a misleading reason is worse). For each child **directory** of `--repos-root` (depth 1, name-ordinal order; non-directory children are not enumerated):

**4.1 Eligibility and freshness precondition** (north-star step 1) — first failing check wins, each producing a visible reason:

| # | Check | On failure |
|---|---|---|
| F0 | name starts with `.` | `status: ignored`, reason `dot-directory` (not a repo; recorded, not scanned) |
| F1 | not a git repo / zero commits (`git rev-parse HEAD` fails) | skipped: `not a git repository or no commits` — **never bypassable** (tracemap requires a commit SHA) |
| F2 | no `origin` remote | skipped: `no origin remote` — **never bypassable** (`--allow-stale` accepts a repo that IS stale against a trunk; with no remote there is no trunk to be stale against, and alternate-checkout dedupe (§4.2) keys on the origin URL) |
| F3 | `git fetch origin` fails | skipped: `fetch failed: <first line of stderr, capped 200 chars>` (unless `--allow-stale`) |
| F4 | neither `origin/main` nor `origin/dev` exists | skipped: `no origin/main or origin/dev branch` (`origin/main` preferred when both exist) (unless `--allow-stale`). V0 knows main/dev only — a `master`-trunk estate is visibly skipped with this reason; preference config waits for that real need |
| F5 | `git rev-parse HEAD` ≠ trunk tip | skipped: `HEAD <sha7> is not at <trunk> tip <sha7>` (unless `--allow-stale`). The check is **content identity** (SHA equality): a detached or feature-branch checkout whose tip IS the trunk tip passes, because the cache and the scan are keyed on content |
| F6 | working tree dirty (`git status --porcelain` non-empty — includes untracked) | skipped: `working tree not clean (uncommitted or untracked changes; --allow-stale to scan anyway)` (unless `--allow-stale`) — protects the cache invariant SHA ⇔ content |

Out-of-scope repos (§3) are handled before all of this: not examined, `status: out-of-scope`. Skips are **never fatal** — one behind repo must not block the estate (broken-child precedent); they are listed per-repo on the console and in the run manifest (§4.4). `--allow-stale` waives **F3–F6 only**; F1/F2 always refuse.

**4.2 Alternates, the scan loop, and the cache.** Cache = `<out>/scans/<repo>/`. For each fresh, in-scope repo, in name-ordinal order:

- **Alternate-checkout dedupe (before scanning):** repos are identified by their normalized `origin` URL (ingest's own normalization; every F2-passing repo has one). A later repo whose origin matches an earlier repo's ⇒ skipped: `alternate checkout of <first-name> (same origin) — one scan per repo; pass scan dirs to ingest explicitly to combine deliberately`. Never scanned, never reused. (This is scan-estate's enforcement of ingest's deliberate-combination rule — two clones passed explicitly would otherwise collide at ingest's exit 4.)
- **Reuse test (all must hold):** cached `scan-manifest.json` `commitSha` == current `git rev-parse HEAD`; `facts.ndjson` present; cache metadata (below) matches this run's scan conditions ⇒ **reused**, no scanner invocation.
- `facts.ndjson` present but NO `scan-manifest.json` ⇒ **warning + skip** (`cached scan has facts.ndjson but no scan-manifest.json — remove or complete it`); resume-safe: never deleted, never overwritten.
- **Cache metadata** `<out>/scans/<repo>/scan-estate.json` (written by scan-estate, read before reuse): `{schemaVersion:"scan-estate-cache.v1", exclude:[<globs>], tracemapSha256:<dll hash>, staleScan:<bool>}`. `staleScan` is true when the scan was taken with freshness waived (F3–F6 under `--allow-stale`). Reuse additionally requires: exclude list equal AND tracemap dll content hash equal AND `staleScan == (this run would skip this repo without --allow-stale)`. The dll hash is compared only when `--tracemap` is given — a no-tracemap all-reuse run is legal and succeeds. Rationale: `--exclude` changes and tracemap upgrades (same dll path, new bytes) must not silently serve old scans; a dirty/behind scan must not later speak for a clean tree at the same SHA — a clean re-run rescans and clears the flag. Metadata absent (older run) ⇒ rescan (safe default).
- Otherwise **scanned** via the seam: `dotnet <tracemap-dll> scan --repo <abs-path> --out <tmp>` with one `--exclude <glob>` per flag, in flag order. The scan lands in a temp sibling `.name.tmp-xxxxxxxx` of the final dir, then swaps atomically (old aside as `.name.old-xxxxxxxx` → new in → delete old; Ingest's swap pattern). Before swapping, stale `.name.tmp-*`/`.name.old-*` leftovers for that repo are cleared (an interrupted run must not strand discoverable garbage). tracemap failure ⇒ temps cleaned, previous cache dir untouched, repo skipped with reason `tracemap exited <n>`, run continues. If any repo needs a scan and `--tracemap` is absent ⇒ **typed error before scanning anything** (exit 1).
- **Scanner seam (testability contract):** the invocation is an internal overridable delegate `(repoPath, scanOutDir, headSha, excludes) → exit code`, defaulting to the `dotnet` child process above; argv composition is a separate pure internal helper. Selftest substitutes a stub that writes `facts.ndjson` + `scan-manifest.json` whose `commitSha` is the passed `headSha` (reuse can fire) and counts invocations.

**4.3 Sidecar bootstrap** (in `<out>/`; existing files are hand-edited operator data and are never overwritten). Runs AFTER the manifest is written (§4.4 order) so bootstrap failures never lose the skip record:

- `ownership.v0.json`: absent ⇒ `ownership init --all-self --self <team>` over this run's fresh scan dirs, passed **explicitly** (`--self` required — teams are never invented); present ⇒ `ownership update --existing --new-self` over the same explicit dirs. `update` refuses `--self` (by design), so scan-estate itself reads the existing file's `selfTeamId`, compares against `--self` when given, and refuses on mismatch (typed error); then invokes `update` without `--self`.
- `producer-evidence.v0.json`: absent ⇒ minimal template `{"schemaVersion":"producer-evidence.v0","externalPackages":[{"packageId":"<delta-package>"}]}` (the delta target MUST be listed — an unlisted external target yields all-conditional waves). The packageId source: `--delta-package` when given, else the existing `delta.json`'s `changes[0].packageName` (the flagship flow — real delta pre-placed, no flags); neither source ⇒ typed error. Both sources and they disagree ⇒ typed error. Present ⇒ untouched, with a note to edit `externalPackages` if the delta target changed.
- `delta.json`: absent ⇒ `package-delta.v1` single-change template (runbook's sample shape: `sourceRepo https://example.invalid/x.git`, `sourceCommitSha` 40 zeros, `ecosystem nuget`, `changeType updated`, `id bootstrap`), requiring all three of `--delta-package/--delta-old/--delta-new` (typed error listing the missing ones); present ⇒ untouched (the real delta IS the real use case) — `--delta-*` flags alongside an existing delta warn and lose. A pre-placed delta with ≠1 changes ⇒ typed error here (the planner's SPEC-003 §1a refusal surfaces at bootstrap instead, naming the file).

**4.4 Order of operations, chain, run manifest.**

1. Scan loop (§4.2) → statuses known for every repos-root child.
2. Write `<out>/scan-estate.v1.json`: `{"schemaVersion":"scan-estate.v1","repos":[{"name","status","reason"?,"commitSha"?}]}` in name-ordinal order; `status ∈ scanned | reused | skipped | out-of-scope | ignored`. Written now — before any later step can fail — so the skip record always survives.
3. If the fresh scan set is empty ⇒ typed error (exit 1) naming the composition: `no repos to ingest (0 fresh: N skipped, M out-of-scope, K ignored — see scan-estate.v1.json)`. No sidecar files are created on this path.
4. Sidecar bootstrap (§4.3). Failures propagate exit codes (manifest already persisted).
5. Ingest the fresh set as **explicit dirs, name-ordinal order** (stale-skipped repos' cached scans deliberately do not enter the run; alternates were already deduped in §4.2) with all three sidecars explicit plus `--scope` when given → `<out>/fixture`.
6. `plan --out <out>/plan.json`, `report --out <out>/report.md` (§6 bytes).
7. Console summary (stderr): `estate: N scanned, N reused, N skipped, N out-of-scope, N ignored → <out>/report.md` plus, when nonzero, `note: skipped repos are listed in <out>/scan-estate.v1.json`. Exit 0 even with skips (they are visible, not errors); ingest/plan failures propagate their exit codes.

## 5. Scope statement in plan and report

With a scope input present, `plan.v1` gains (positioned after `repos`; omitted otherwise). Key order is fixed and shared: `["mode", "includedCount", "outOfScope"]` (`includedCount` only in include mode):

```json
"scope": {"mode": "exclude", "outOfScope": ["legacy", "spikes"]}
"scope": {"mode": "include", "includedCount": 3, "outOfScope": ["spikes"]}
```

`outOfScope` = the configured exclude list (**deduped, ordinal — the config's own order never reaches the plan**, which is what keeps the input-permutation case green), **omitted when empty** (house style for optional arrays; exclude-mode with no exclude names cannot occur — it validates to no scope); `includedCount` = the config's deduped include count. The report Summary gains one line, directly after the Repositories line (before the Version-disagreements line when present):

```
- Scope: 2 repos out of scope by config: legacy, spikes
- Scope: include-mode — 3 repos in scope by config, 1 out of scope by config: spikes
```

The statement is the config's echo, not a coverage claim: out-of-scope repos have no classification because they have no evidence, and the plan never pretends they are `not-affected`. The include-mode complement is not enumerable by the plan (it never sees the universe); when driven by `scan-estate`, those names live in `scan-estate.v1.json`. Lockstep artifacts updated together: `src/UpgradeAuthority/Canonical.cs` (the byte producer), `tools/canonicalize-goldens.mjs` (`KEY_ORDER.root` gains `scope` after `repos`; new `KEY_ORDER.scope`), `docs/schemas/plan.schema.v1.json`, `docs/schemas/input-schemas.md`.

## 6. `--out <file>` on `ua plan` and `ua report`

Writes the canonical artifact bytes (exactly the string stdout carries: UTF-8 no BOM, LF, trailing newline) directly to the file — the cure for PS 5.1 `>`/`Out-File` decoding the UTF-8 stream with the legacy codepage and mangling `→`/`—` (note: `Out-File -Encoding utf8` on PS 5.1 also prepends a BOM — `--out` bytes are the canonical ones). Parent directory created if missing. With `--out`: stdout prints nothing (the file IS the artifact); stderr notes `wrote <file>`. `-o` alias accepted. Without `--out`: byte-identical stdout behavior as today. The two channels are machine-checked equal in both directions (§7.8).

## 7. Acceptance criteria

1. **Equivalence (load-bearing):** on a repos folder, one `scan-estate` run produces `<out>/fixture`, `plan.json`, `report.md` byte-identical to the manual sequence — scan (stub seam) → `ownership init --all-self` → sidecar templates → `ingest` of the same fresh scan dirs explicitly in name-ordinal order → `Canonical.Write(LoadEngine(...).BuildPlan())` → `Report.Render` — all in-process, offline, deterministic (selftest; child-capture methodology per §7.8 for the `--out` files). The wrapper's own alternate-dedupe is machine-checked: a repos-root with two clones of one origin ⇒ the second is visibly skipped (`alternate checkout of <first-name>`), the fresh set contains one, exit 0 (selftest).
2. **Incremental:** re-run with no repo changes performs **zero** scanner invocations (counted at the seam); exactly one repo bumped to a new trunk-tip commit ⇒ exactly that repo re-scans, all others reused (selftest). **Cache-key honesty:** changing `--exclude` between runs ⇒ rescan; swapping in a different-bytes tracemap dll ⇒ rescan; a dirty `--allow-stale` scan followed by a clean run at the same SHA ⇒ rescan (never silent reuse) — each counted at the seam (selftest).
3. **Freshness:** a behind repo is skipped with reason in console + manifest; the same run with `--allow-stale` scans it; dirty-tree skip likewise (selftest). A no-origin repo is skipped even with `--allow-stale` (selftest).
4. **Cache honesty:** facts-without-manifest warns and skips (selftest); a simulated scanner failure leaves the previous cache dir intact with no `.tmp`/`.old` residue and records `tracemap exited <n>` (selftest); `ua ingest --scans-root` and `ua ownership` ignore dot-prefixed children (a planted `.leftover` dir with facts is not ingested) (selftest).
5. **Scope:** excluded repo not scanned; report carries the §5 statement; an excluded repo's leftover cached scan dir is not ingested via `--scans-root --scope`; **excluding `svc` on the committed scans tree does not let `svc-net48` in** (alternate note, both `ingest` and `ownership`); explicit-dir-out-of-scope warns but includes; include∩exclude and `include:[]`+exclude ⇒ typed errors; include-mode `scan-estate` run with a non-included repo ⇒ manifest lists it `out-of-scope` and the report states include-mode (all selftest).
6. **Bootstrap + preservation:** first run creates ownership/producer/delta; a hand-edited ownership survives re-runs via `update`; existing delta/producer are never overwritten; producer template derives its packageId from a pre-placed real `delta.json` with no flags (selftest); missing `--self` on bootstrap, `--self` mismatch with existing selfTeamId, missing `--tracemap` when a scan is needed, missing `--delta-*` trio with no delta file, `≠1`-change pre-placed delta ⇒ typed errors (selftest).
7. **Ordering + empty set:** every-skipped or all-out-of-scope repos-root ⇒ manifest fully written, typed error, no sidecar files (selftest). **Fixture pin:** new `F-scope` fixture (existing inputs + `scope.v0.json`) pins the scope field and report line byte-exactly and passes the input-permutation case; validator covers the new shape; **every existing golden byte-identical** (GOLDEN-CHANGES.md records the addition and the zero-churn claim).
8. **`--out` byte-equality:** `ua plan --out F` / `ua report --out F` files are byte-identical to captured child-process stdout of the same commands without `--out` (selftest, both commands); a bare trailing `--out` on both commands refuses exit 1 (selftest).
9. **Shared validator:** the same malformed scope file produces the same typed message at `ingest --scope` and at plan load (selftest).
10. **Totals:** existing 100 cases untouched; new cases offline and deterministic; README/FEATURES/runbook/doc-16 case-count mentions updated; `node tools/validate-fixtures.mjs` green (24 goldens).

## 8. Out of scope (typed, honest, logged)

- **Building tracemap from source** — `--tracemap <dll>` points at a built assembly (runbook's one-time `dotnet build`); coupling ua to tracemap's source layout would break independently.
- **Parallelism** — sequential; freshness fetches dominate at estate scale and are inherently per-repo.
- **Recursive `--repos-root` discovery** — depth 1, matching `--scans-root`.
- **Scope auto-discovery / non-folder-name keys** — explicit flag, folder names; remote-URL-keyed scoping waits for a real need (the report statement is names-as-configured).
- **Deleting out-of-scope cached scans** — operator data; ingest skips them visibly.
- **Multi-change deltas** — V0 single-change rule unchanged (SPEC-003 §1a).
- **The tracemap deps.json evidence slice** — separate work; consumed here via `--scans-root` regardless of manifest kinds.
- **Trunk preference beyond main/dev** (e.g. `master`, custom trunks) — visibly skipped with reason (§4.1 F4); preference config waits for a real need.

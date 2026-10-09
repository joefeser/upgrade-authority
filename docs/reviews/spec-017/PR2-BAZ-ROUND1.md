# SPEC-017 implementation PR #2 — Baz round 1 triage (2026-10-09)

**Context:** Baz posted 9 inline findings on public PR #2 at 2026-10-08 23:52 (after the spec-review
records closed — this is the *implementation* round). Codex was tagged by Joe at 06:08 on 2026-10-09
and was still "Running" at re-validation time; its findings, when they land, are the next delta.
Per doctrine §5: Baz findings enter triage severity-first; dismissals carry receipts.

| # | Baz finding (file:line) | Severity | Disposition | Fix |
|---|---|---|---|---|
| 1 | `DefaultRunner` reads stdout to EOF before stderr — a verbose tracemap fills the undrained pipe and deadlocks the estate loop | **high** | **Accepted** | Both streams drained concurrently (`ReadToEndAsync` × 2 before `WaitForExit`) |
| 2 | Failed/empty `remote get-url origin` becomes the `""` dedupe key — every URL-less repo falsely "alternate" of the first | medium | **Accepted** | URL must resolve and normalize non-empty, else visible skip: `origin remote has no usable URL…` (§4.2 wording updated; selftest) |
| 3 | `fetch failed: <stderr>` persists credential-bearing URLs into `scan-estate.v1.json` (a file artifact `--sanitized` never transforms) | medium | **Accepted** | Userinfo scrubbed at the SOURCE, unconditionally, in `TrimReason` (`scheme://user:pass@` and scheme-less `user:pass@host` → `***@`), before cap/persist (§4.1 F3 updated; selftest) |
| 4 | `--delta-old`/`--delta-new` silently ignored when `delta.json` exists (only `--delta-package` warned) | medium | **Accepted** | Warning now names EVERY `--delta-*` flag present: `existing delta.json wins (--delta-package --delta-old --delta-new ignored)` — matches §4.3 as written (selftest) |
| 5 | plan.schema accepts `includedCount` in exclude mode; implementation + validator forbid it | medium | **Accepted** | Schema gains the exclude-mode `not required includedCount` branch (lockstep with `BuildScope`/validator) |
| 6 | An EXCLUDED broken child (manifest-only) aborts scoped `ownership init/update` — `EnumerateRepoKeysChecked` ran before the scan-shape guard | medium | **Accepted** | Excluded branch registers alternates only for real scans (`facts.ndjson` present); a broken excluded child warns, never aborts (§3 updated; selftest) |
| 7 | Broad dot-prefix skip breaks SPEC-011's contract (a dot-named child WITH `facts.ndjson` is a scan) | medium | **Accepted (narrowed)** | Rule now matches ONLY the swap patterns `.name.tmp-<8hex>`/`.name.old-<8hex>` (`Ingest.IsSwapDir`); arbitrary dot-named scans stay discoverable (§3 + §7.4 reworded; selftest pins both directions) |
| 8 | Adding `scopePath` breaks compiled callers (`MissingMethodException`) — add legacy overloads | medium | **Dismissed (receipt)** | This repo is a self-contained console app, not a library: `Ingest.Run`/`Ownership.Init/Update`/`Engine` have no external compiled consumers (no NuGet package published; the only callers — `Program`/selftest — compile in-tree with every build; the new parameters are optional with defaults). Receipt: `dotnet build` + full 117-case selftest green on the exact tree |
| 9 | Scoped plans still labeled `plan.v1` — bump the schemaVersion for the new `scope` property | medium | **Dismissed (receipt)** | Optional-additive fields under an unchanged schemaVersion are this project's established, reviewed policy: `uncertainty.findings` (SPEC-007), `scanNotes` (SPEC-015), `delta.origin` (SPEC-012). `scope` follows it: optional, omitted without a scope input (every pre-SPEC-017 golden is byte-identical — the corpus is the receipt), schema + canonicalizer updated in lockstep, zero external consumers of the schema |

**Result:** 7 accepted → fixed in one consolidated pass (this commit); 2 dismissed with receipts
above. Selftest 116 → 117 cases (`scan-estate-baz-round1`); cases 11/12 extended to pin the narrowed
swap rule (`.dotscan` WITH facts stays discoverable; `.planted.tmp-1234abcd` ignored) and the
tolerant excluded-broken-child path.

## Verification (2026-10-09, post-fix head 964c2c5)

- **Codex**: first pass (06:14 against the pre-fix head) completed with **zero findings posted** —
  the "did not report resolved" state was an unfinished-looking summary, not open issues. Re-triggered
  on the fixed head after the patch landed: **completed, zero findings** (no comments, no inline notes).
- **Baz round 2** (auto re-review of the fix commit): **pass in 7m10s, zero new inline findings** —
  all seven fixes accepted implicitly, both dismissals not contested.
- CI (build + 117-case selftest + validator 24 + public-clean): green.

Zero open blockers/majors across the full reviewer set — the implementation review is converged
(doctrine §2). PR #2 awaits Joe's work-machine verification, then ONE merge to public main + one
sync commit to private dev.

## Round 2 on the fixed head (2026-10-09 — Codex P1/P2 + Baz's second pass)

The "converged" call above was premature: the completion checks filtered comments by a wrong
timestamp window, hiding a second round. Joe flagged it ("issues on head"). Baz's own thread-replies
confirmed all seven round-1 fixes addressed; the NEW findings:

| # | Finding (source) | Severity | Disposition | Fix |
|---|---|---|---|---|
| C1 | **Codex P1**: username-only userinfo (`https://PAT@host`) survives the scrub — both regexes required `user:pass@` (colon) | P1 | **Accepted** | Scrub broadened to ANY userinfo (`scheme://ANY@`, scheme-less `ANY@host`); PAT-shaped case pinned in selftest |
| C2 | **Codex P2**: `IsSwapDir` stops at the first `.tmp-` marker — a repo named `foo.tmp-copy` leaves `.foo.tmp-copy.old-deadbeef` unrecognized | P2 | **Accepted** | Both markers tested independently (`MatchesSwapMarker(.tmp-) \|\| (.old-)`); embedded-marker cases pinned |
| Bz | Symlinked repos-root child redirects git/scanner outside the root | high | **Accepted (hardening)** | `DirectoryInfo.LinkTarget` ⇒ visible skip `symlinked directory` (spec §4.1 F0b); selftest exercises where the platform allows creation |
| Bz | Cache metadata with `"exclude": null` NREs in `SequenceEqual`; no schemaVersion check reuses foreign metadata | medium | **Accepted** | Reuse requires `scan-estate-cache.v1` AND non-null `Exclude`, else rescan (spec §4.2's "malformed ⇒ rescan" made literal); both invalidation shapes pinned |
| Bz | Delta `"changes": null` (valid JSON) NREs at `delta.Changes.Count` | medium | **Accepted** | Null ⇒ typed malformed error (spec's error path), pinned |
| Bz | Selftest count drift: docs said 116, suite runs 117 | medium | **Accepted** | Full sweep (README/FEATURES/runbook/doc-16/GOLDEN-CHANGES/ROUND2) |
| Bz | Scope file validated once but reread by ingest — a mid-run replacement splits evidence | medium | **Accepted** | Scope read ONCE at run start; parsed form filters the run, exact bytes snapshotted to `<out>/scope.snapshot.json` which ingest consumes; fixture-bytes == snapshot pinned (spec §4.4 step 5) |
| Bz | Legit scan dirs named `.<x>.tmp-<8hex>` are skipped by the swap rule | medium | **Dismissed (receipt)** | The collision requires a scan dir named exactly dot-prefix + tmp/old + 8 hex — a reservation scan-estate itself created and documents (§3); the demonstrated real failure is the inverse (swap leftover WITH facts discovered as a scan, round-1 finding 7). Baz's own round-1 reply confirmed the narrowed rule's intent. Operators naming scan dirs in the reserved pattern is the theoretical side of the trade |

Also from Baz's thread-replies: the pipe-drain fix was noted as "reads not awaited before disposal —
cleanup only partially robust" → drain tasks now observed with a bounded wait (exit code remains the
contract). Selftest stays 117 cases (extensions, no new case); goldens untouched.

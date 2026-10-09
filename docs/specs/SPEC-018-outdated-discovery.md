# SPEC-018 — Outdated discovery: `ua drift` + feed truth + delta candidates

**Status:** implemented — review converged round 2 (SHIP; records in `docs/reviews/spec-018/`)
**Author:** ZCode (coordinator); north-star step 3 (Joe, 2026-10-08: "determine ALL packages that need updating")
**Date:** 2026-10-09
**Depends on:** SPEC-007/008 (lockfile rows — the installed-version inventory) · SPEC-009 §3 (exact-pin classification, the comparer) · SPEC-017 §6 (`--out` canonical-file convention) · SPEC-003 §1a (single-change planning — unchanged)

## 1. Purpose

The delta is hand-written today. On a real estate nobody knows *which* packages are old without opening every csproj — the north-star's step 3 is the stated real gap. This spec adds the **drift report**: compare the estate's installed-version inventory (lockfile rows today; deps.json-derived rows ride the same surface once the tracemap slice lands) against **feed truth** (an operator- or folder-feed-provided file of latest versions), classify every installation, and emit **ready-to-run single-change delta candidates** for the drifted packages. Feed truth is never fetched live: the file (or the local folder feed it was generated from) IS the truth, as-of its own provenance — "stated, not verified live" stays the house style. Orchestration (running many deltas deepest-layer-first) is the campaign layer, not this spec.

## 2. Command surface

```
ua drift <fixture-dir> (--feed <file> | --folder-feed <dir>) [--out <file>] [--emit-deltas <dir>]
```

- Exactly one truth source per run: both flags, or neither ⇒ typed error (exit 1). Repeated value flags are first-wins (existing `GetOpt` behavior).
- Every value flag requires a value (SPEC-017 §2 rule; bare flag ⇒ exit 1, never a silent stdout fallback).
- Without `--out`, the canonical `drift.v1` JSON goes to stdout (a canonical artifact — `--sanitized` bypass, byte-identical to unsanitized stdout, same as plan/report); with `--out`, the file carries exactly those bytes and stdout prints nothing.
- Exit 0 even when everything is behind — a report, not a gate. **Error precedence (evaluation order): flags → fixture load → feed load/validate → classify → emit.** Exit codes: 1 usage (missing/both/dangling flags, missing feed FILE — naming the path); 3 typed runtime errors (`UaException`: fixture-load incl. malformed optional scope, and the emit-deltas filename collision); 5 feed file invalid (sidecar-class precedent); internal faults stay 4.
- `--sanitized`: the drift stdout bypass is wired exactly like plan/report (verified byte-identical in acceptance 5).

## 3. Feed truth (`feed-versions.v1`)

```json
{"schemaVersion":"feed-versions.v1","source":"operator-provided","asOf":"2026-10-09","packages":[{"packageId":"Newtonsoft.Json","version":"13.0.3"}]}
```

- `source` ∈ `operator-provided` | `folder-feed`, **validated** (anything else ⇒ exit 5); `asOf` optional free string, echoed verbatim — the honesty marker for how fresh the truth is; **the key is omitted when absent** (never `null`). Neither field is ever generated from a clock.
- Validation: `schemaVersion` must match (else exit 5 naming it); empty `packageId`/`version` ⇒ typed error; duplicate `(packageId, version)` collapses silently (idempotent); the same `packageId` at TWO different versions ⇒ typed error — the feed contradicts itself. All id comparisons in this section are **case-insensitive (OrdinalIgnoreCase)** — NuGet ids.
- **Folder feeds** (`--folder-feed <dir>`): flat, depth-1, `*.nupkg` only (NuGet folder-source layout), enumerated in **ordinal filename order** so warnings/errors never depend on filesystem order. Filename grammar `{PackageId}.{version}.nupkg`, parsed as the **longest trailing substring** matching `core(.core){0,3}(-prerelease)?(+build)?` where each `core` is 1+ digits (greedy from the right; everything before it is the id). Worked: `Newtonsoft.Json.13.0.3.nupkg` → id `Newtonsoft.Json` v `13.0.3`; `X.13.0.3-beta.1.nupkg` → v `13.0.3-beta.1` (prerelease may contain dots); `Foo-Bar.1.0.0.nupkg` → id `Foo-Bar` (hyphens are legal in ids; the split lives at the dot-aligned version). A filename that does not resolve ⇒ **warning + skip** (never a guess). Identical `(packageId, version)` pairs collapse (case-variant filename spellings included), matching the file form's idempotence. **Documented ambiguity (NuGet's own):** ids ending in dotted numerics resolve to the longest version — `Foo.1.2.0.0.nupkg` reads as id `Foo` v `1.2.0.0`, not id `Foo.1` v `2.0.0` (§9). **One id at two versions in one folder ⇒ exit 5 naming both files** — latest-selection requires prerelease ordering, which V0 deliberately refuses (the comparer never orders prereleases); a real accumulating feed is therefore a §9 future slice, and the supported folder feed is the latest-only samplerepos harness shape. Missing/not-a-directory ⇒ exit 1. The in-run conversion produces exactly the `feed-versions.v1` shape with `source: "folder-feed"` and no `asOf` (the folder is the provenance).

## 4. Installed-version inventory

**Required fixture inputs** (the same files `ua plan` consumes, loaded by the same shared code path): `producer-evidence.v0.json`, `ownership.v0.json`, `package-evidence.v0.json`, and a **single-change `delta.json`** — the delta is a LOADER PRECONDITION ONLY (SPEC-003 §1a fires at load; drift-first runs bootstrap a placeholder via `ua scan-estate`, whose generated-delta shape this spec reuses); **drift never reads the delta's content**. Exactly one lockfile file (v0/v1/v2) may be present. Optional `scope.v0.json` is loaded-and-ignored (malformed still exits 3 — the shared validator runs; inventory, not planning — the scope statement lives in plan/report).

- **Lockfile rows**: every row with a non-null `version` is an installation `(repo, packageId, version, evidence: "lockfile")`, `repo` = the row's repo key verbatim. Rows with null version are skipped with a stderr note (unevidenced resolution never counts — the SPEC-007 rule).
- **Declared exact pins** (facts): a consumer fact whose `declaredConstraint` is an **exact numeric pin** is an installation with `evidence: "declared-pin"`. The test is extracted as one shared helper (`IsExactPin(constraint)`: `ParseCore` succeeds — ranges, floating, and prerelease constraints fail it — and no `redacted:` prefix), reusing apply's logic with two deliberate divergences from its edit-site rules: no line-evidence requirement (an unlocated fact is still an installation) and no comparison target. `VersionOverride` facts and CPM pins qualify when exact.
- **Casing and dedup**: installation grouping, dedup, and feed keying are **case-insensitive (OrdinalIgnoreCase)**. Identical `(repo, packageId, version)` collapses, `lockfile` evidence wins over `declared-pin`. A `(repo, packageId)` seen at TWO DIFFERENT versions keeps BOTH rows (a real discrepancy — visible, never merged away). **Reported spelling**: the lockfile spelling when any lockfile row exists for the group, else the pin spelling; among case-variant spellings of one source, ordinal-first wins — input-order-independent, so the permutation contract holds. The emitted delta's `packageName` is always the **estate (reported) spelling, never the feed's** (the planner matches ordinally — a feed-cased delta would silently plan to nothing).
- Forward-compat: deps.json-derived rows (tracemap slice, in flight) are expected to arrive as lockfile-class rows and need no drift change — this spec consumes the surface, not the source.

## 5. Classification

Per `(packageId, version)` vs the feed's latest, using **apply's comparer** (`CompareCore`) so drift can never disagree with the upgrade-only policy:

| status | meaning |
|---|---|
| `behind` | compare < 0 — the delta candidates' fuel |
| `current` | compare = 0 (4-segment padding: `13.0.3` == `13.0.3.0`) |
| `ahead` | compare > 0 — recorded, never a downgrade suggestion |
| `unclassified` | either side non-plain-numeric — honest, with a `reason` on the row from an **exhaustive template set**: D1 `installed {version} not a plain numeric pin`, D2 `feed latest {latest} not a plain numeric pin`, D3 `both installed {version} and feed latest {latest} not plain numeric pins` |
| `unknown-feed` | package absent from the truth file — visible absence, never silence (row-level status only; such packages carry `"latest": null`) |

## 6. Output — `drift.v1` (canonical bytes)

```json
{
  "schemaVersion": "drift.v1",
  "feed": {"source": "operator-provided", "asOf": "2026-10-09", "packageCount": 2},
  "summary": {"packagesObserved": 2, "behind": 1, "current": 1, "ahead": 0, "unclassified": 0, "unknownFeedPackages": 0},
  "packages": [
    {"packageId": "Newtonsoft.Json", "latest": "13.0.3",
     "statuses": {"behind": 1, "current": 1, "ahead": 0, "unclassified": 0},
     "installations": [
       {"repo": "billing", "version": "12.0.3", "evidence": "lockfile", "status": "behind"},
       {"repo": "shipping", "version": "13.0.3", "evidence": "lockfile", "status": "current"}
     ]}
  ]
}
```

- `packages[]`: only packages WITH installations, ordered by reported packageId (ordinal); `installations[]` ordered by `(repo, version)` ordinal; `reason` emitted only on `unclassified` rows. Per-package `statuses` counts installations for behind/current/ahead/unclassified (no unknown-feed key — that status is row-level and summarized as `summary.unknownFeedPackages`, counting packages). Summary's behind/current/ahead/unclassified count INSTALLATIONS; `packagesObserved` and `unknownFeedPackages` count PACKAGES. Fixed key order throughout (house canonical style: 2-space, LF, trailing newline); hand-rolled writer mirroring `Canonical.cs`. `feed.packageCount` = post-collapse truth-file size.
- Feed packages nobody installed are NOT listed (the feed's `packageCount` records the truth-file size; unobserved truth is the feed's business, not drift's).

## 7. Delta candidates (`--emit-deltas <dir>`)

- Per package with ≥1 `behind` installation: **one delta.json per DISTINCT behind version** — single-change `package-delta.v1`, planner-compatible as-is, full field set: `version: "package-delta.v1"`, `sourceRepo: "https://example.invalid/x.git"`, `sourceCommitSha` (40 zeros — the SPEC-017 bootstrap precedent for generated provenance), and one change: `id: "drift-<packageId>-<from>-<to>"`, `packageName` (the ESTATE spelling, §4), `ecosystem: "nuget"` (the inventory is NuGet-only by construction), `changeType: "updated"`, `oldVersion: <from>`, `newVersion: <latest>`.
- Filename `<safeId>.<from>.delta.json`; `<safeId>`/`<from>` replace every character outside `[A-Za-z0-9._-]` with `_`. **Filename collision after sanitization ⇒ typed error naming both package ids** (never a silent overwrite). The directory is created **iff ≥1 candidate is emitted** (an all-current estate writes nothing and creates nothing). Every candidate is precomputed and validated BEFORE the destination is touched, and a **non-empty destination directory ⇒ typed error (exit 3)** — stale candidates from a previous run must never sit beside (or masquerade as) the fresh set; clear it or pass a fresh directory. A refusal therefore never leaves partial or overwritten output.
- Which candidate to RUN is a human/campaign decision (deepest-layer-first orchestration = the design talk). Nothing here executes anything.

## 8. Acceptance criteria

1. **Folder-feed conversion:** flat dir with plain, dotted-id, prerelease-with-dots (`X.13.0.3-beta.1`), hyphenated-id (`Foo-Bar.1.0.0`), and numeric-ending-id (`Foo.1.2.0.0` → id `Foo` v `1.2.0.0`) names → correct truth; an unresolvable name warns + skips; one id at two versions ⇒ exit 5 naming both files; missing dir ⇒ exit 1 (selftest).
2. **Feed validation:** schemaVersion mismatch, bad `source`, and same-id-two-versions ⇒ exit 5 with the file named; duplicate identical entries collapse silently (selftest).
3. **Inventory:** lockfile-preferred dedup; exact-pin facts included as `declared-pin`; a repo seen at two versions keeps both rows; null-version lockfile rows skipped with a note; mixed-case estate spellings + lowercased feed → ONE package entry, the estate spelling echoed (selftest).
4. **Classification parity (structural):** the shapes table asserted row-by-row against `Apply.CompareCore` directly — `13.0.3` vs `13.0.3.0` = current (padding), short forms, 4-segment, prerelease on either side ⇒ `unclassified` (D1/D2/D3 reasons), floating; `unknown-feed` for absent packages — never a guessed version (selftest).
5. **Output contract:** canonical bytes to stdout; `--out` file byte-identical to captured child stdout, no BOM, bare flag refuses (SPEC-017 §6 method); `--sanitized` stdout byte-identical to unsanitized stdout (selftest).
6. **Delta emission:** one file per distinct behind version; every field present (`ecosystem: "nuget"`, estate-spelled `packageName`); swapping a generated delta into a fixture produces a plan whose `repos[]` contains the **expected affected set** (not merely "a valid plan"); emitted files byte-golden under `golden/deltas/` (selftest).
7. **Fixture pin:** `F-drift` = real `ua ingest` output over the committed **billing + shipping + svc + cleanrepo** scans (four repos) + `input/feed-versions.v1.json`, covering every status via a verified table — behind (billing Contoso.Payments 1.0.0 vs 1.5.0), ahead (shipping Contoso.Payments 2.0.0 vs 1.5.0), current (svc Serilog 3.1.1 lockfile + cleanrepo Serilog 3.1.1 **declared-pin** — cleanrepo has no lockfile rows, the survivor case), unclassified D2 ×4 (feed latest `13.0.3-beta.1` prerelease vs the Newtonsoft family: billing 13.0.3, shipping 12.0.3, svc 13.0.1 + 12.0.3), unknown-feed (Contoso.Core — absent from the feed; svc carries it at 1.0.0 AND 0.9.0, also pinning the keep-both-versions rule) — golden `drift.v1` byte-exact + `golden/deltas/` for the single behind candidate; driftless fixture (delta.json deleted) ⇒ exit 3 naming it, 2-change delta ⇒ exit 3 (selftest).
8. **Determinism + permutation:** two consecutive runs byte-identical, no clock bytes; the selftest grows a drift-permutation case (copy input/ + feed to scratch, reverse every array incl. feed `packages[]`, re-run, byte-compare) (selftest).
9. **Totals:** existing 117 cases untouched; counts swept in README/FEATURES/runbook/doc-16; validator grows drift.v1 checks (parse, schemaVersion, status vocabulary, summary↔packages consistency, canonical bytes) and reports 25 goldens; dual-fault precedence case (bad fixture + missing feed ⇒ 3) (selftest).

## 9. Out of scope (typed, honest, logged)

- **Live feed access** — no network, ever; feed truth arrives as a file or local folder ("stated, not verified live").
- **Latest-selection from accumulating folder feeds** (many versions per id) — requires prerelease-aware ordering V0 deliberately refuses; the supported folder feed is latest-only (samplerepos harness shape).
- **Multi-change / campaign orchestration** — running the candidates deepest-layer-first, batching, PR loops: the campaign layer (design talk; north-star step 6).
- **Markdown drift report** — `drift.v1` JSON only; rendering follows if it's wanted.
- **Recursive folder feeds, non-nupkg layouts** (global-packages cache hierarchies) — flat folder sources only until a real need exists.
- **Numeric-ending-id filename ambiguity** — `Foo.1.2.0.0` resolves longest-version (NuGet-compatible); packages genuinely named `Foo.1` need a `--feed` file until a real case exists.
- **deps.json ingestion** — the tracemap slice's ua-side consumption is its own queued spec; drift is ready for its rows by construction (§4).
- **Choosing the "right" from-version** when an estate is split — every distinct behind version gets a candidate; picking is a human/campaign decision.
- **Planner-wide case-insensitive package matching** — plan/apply compare package ids ordinally against the delta target (a pre-existing identity policy, not a drift behavior: a HAND-written delta carries the identical hazard on an estate whose repos spell one id with different casing). Drift mitigates by emitting the estate's maximal-coverage spelling; teaching the planner/apply NuGet case-insensitivity is its own queued slice.

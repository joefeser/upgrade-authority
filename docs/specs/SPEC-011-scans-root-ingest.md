# SPEC-011 — `--scans-root`: estate-scale ingest

**Status:** draft — round 1 review (brief-017)
**Author:** ZCode (coordinator)
**Date:** 2026-10-03
**Depends on:** SPEC-005 (ingest) · SPEC-007/008 (multi-row, multi-repo lockfiles — already shipped)

## 1. Purpose

One command ingests a whole scans tree. Today `ua ingest` takes explicit scan directories; a large estate means hundreds of arguments. `--scans-root <dir>` discovers them: every child directory of `<dir>` containing `facts.ndjson` is a scan dir, fed through the exact existing pipeline. This is the last CLI-shape change between the tool and a real estate run.

## 2. Command

```
ua ingest --scans-root <dir> --out <fixture-dir> [--producer …] [--ownership …] [--delta …]
ua ingest <scan-dir>… --scans-root <dir> --out <fixture-dir> …   # allowed: union of explicit + discovered
```

(`--out` is required, exactly as the CLI enforces today.)

- Without positional scan dirs, `--scans-root` alone is sufficient.
- **Discovery rule:** child directories of `<dir>` (non-recursive, depth 1) containing `facts.ndjson`, taken in **name order (ordinal)** — deterministic regardless of filesystem enumeration order.
- A child with `scan-manifest.json` but **no** `facts.ndjson` is a broken-looking scan: **warning to stderr, skipped** (never silently, never fatal — one broken scan must not block 149 good ones at estate scale).
- Children with neither file are not scans; ignored without comment.
- `<dir>` missing or not a directory ⇒ typed error (exit 1, existing ingest usage path).
- Explicit dirs and discovered dirs are combined; the same directory passed both ways is ingested once (dedup by resolved path; explicit order first, then discovered name order).

## 3. Sidecar rules at scale (one deliberate behavior change)

Today, with multiple scan dirs, a sidecar found in ANY dir silently wins (first match), and the delta auto-locates only in the first dir. At estate scale that is a wrong-metadata hazard: one repo's leftover sidecar would silently speak for 149 others. **New rule:** sidecar auto-discovery applies only when the run has **exactly one** scan dir (explicit or discovered). With two or more, `--producer`, `--ownership`, and `--delta` must be passed explicitly; each missing one is a **typed error** (exit 1) naming it — run-wide metadata is an estate-level decision, never a first-match guess. (Single-dir runs keep today's auto-discovery untouched.)

## 4. What does NOT change

Everything downstream: repo keying, coverage honesty, lockfile v2 emission, same-key conflict rules, the "same repo ingested twice" exit-4 hint, sidecar flags apply to the whole run; sidecar auto-discovery only ever applies to single-dir runs (§3). Multi-repo is already unlimited (SPEC-008). This spec is discovery + plumbing only.

## 5. Acceptance criteria

1. **Equality (the load-bearing case):** ingesting the committed estate via `--scans-root testdata-ingest/tracemap-rich/scans` produces **byte-identical** output files to ingesting the five scan dirs explicitly — machine-checked selftest case (both runs compared file-by-file).
2. **Determinism:** two consecutive `--scans-root` runs produce byte-identical output (selftest).
3. **Broken-child tolerance:** a scratch root with one manifest-only child + one good child ⇒ warning names the broken child, good child ingests, exit 0 (selftest).
4. **Typed error:** missing root ⇒ exit 1 naming the path (selftest).
5. **Union case:** explicit dirs + `--scans-root` together produce byte-identical output to passing every dir explicitly (selftest).
6. **Sidecar explicitness:** a two-dir run where a sidecar file IS present in a scan dir but its flag is omitted (delta, producer, and ownership — one case each) ⇒ typed error naming it — the test must catch the first-match hazard, not the trivial missing-file path; a single-dir run still auto-discovers (selftest, all four cases).
7. Zero golden churn; existing 71 cases untouched.

## 6. Out of scope (typed, honest, logged)

- **Recursive discovery** (nested trees): depth 1 only — a flat scans/ directory per estate is the convention tracemap produces; nesting needs a real consumer before it exists.
- **Per-repo sidecars** in scan dirs (e.g. ownership per repo): sidecars remain run-wide as today.
- **Filtering** (include/exclude globs): queue for when a real estate needs them.
- **Parallelism:** sequential; ingest is I/O-trivial next to the scans themselves.

# SPEC-014 — `ua ownership`: estate-scale ownership.v0 generation

**Status:** draft — round 1 review (brief-023)
**Author:** ZCode (coordinator)
**Date:** 2026-10-03
**Depends on:** SPEC-001 §2.4 (ownership.v0) · SPEC-011 (scan discovery — repo enumeration reuses it)

## 1. Purpose

Today an estate plan needs a hand-written `ownership.v0` listing every repo. At estate scale that is a manual blocker before `ua plan` schedules anything. `ua ownership` generates and maintains the file from the estate's own scan set: **init** creates it from discovered repos; **update** extends an existing file without touching manual assignments. The generated file is exactly the SPEC-001 shape — no schema change, no planner change.

## 2. Commands

```
ua ownership init   <scan-dirs…|--scans-root <dir>> --self <teamId> [--all-self|--team <team>|--unassigned] --out <file>
ua ownership update <scan-dirs…|--scans-root <dir>> --existing <file> [--new-team <team>|--new-self|--new-unassigned] --out <file>
```

Repo discovery: identical to SPEC-011 (explicit dirs + `--scans-root`; alternate snapshots collapse to one repo). **Repo identity is ingest's exact `RepoKey` algorithm, verbatim** — normalized manifest `remoteUrl` when present, else the FACT-level `repo` value (not the manifest's `repoName`: a facts-only scan, or one whose fact-level repo differs from the manifest name, keys differently) — for BOTH discovery dedup and entry matching. One implementation, shared with ingest — the spec forbids a second copy of the algorithm.

## 3. Semantics

### 3.1 init
- Enumerate every distinct repo in the scan set, ordinal order.
- **Assignment modes (exactly one required):**
  - `--all-self`: every repo → `selfTeamId` (the MVP estate: one team owns everything).
  - `--team <t>`: every repo → `<t>` (must differ from self by definition — typed error if equal).
  - `--unassigned`: NO ownerships emitted; the repo list is printed to stderr as a fill-in checklist (absent = unknown to the planner — honest, and no invented teams).
- Output: canonical JSON (`{schemaVersion, selfTeamId, ownerships:[{repo, team}]}`), repos ordinal-sorted, byte-deterministic. Trailing comment NOT possible in JSON — the checklist goes to stderr.

### 3.2 update
- Load `--existing` (schema-validated; malformed ⇒ typed error).
- **The whole file is preserved except the ownerships list**: `mirrors[]` and any future fields pass through verbatim (dropping mirror mappings would silently break the F-alias identity contract).
- Repos already present keep their team **verbatim** — manual edits are sacred. "Already present" is resolved through the file's own `mirrors[]` exactly as the planner does (SPEC-001 §3): an entry keyed by an alias and a discovered repo whose canonical name matches are the SAME repo — never re-added, never duplicated.
- Newly discovered repos get `--new-self` (→ selfTeamId) / `--new-team <t>` / `--new-unassigned` (omitted + checklist line). Exactly one required.
- Repos in the file but ABSENT from the scan set are **kept and warned** (a scan gap must not silently erase assignments — the runbook's honesty rule).

### 3.3 Never invented
The tool never guesses a team from a repo name, path, or convention — assignments come only from the flags (which represent a human decision) or the existing file.

## 4. Acceptance criteria

1. `init --scans-root …scans --self team-a --all-self` over the committed estate yields every distinct repo (billing, cleanrepo, legacy, shipping, svc — svc-net48 collapses into svc) in ordinal order — byte-golden selftest case.
2. `--unassigned` prints the checklist, emits a valid empty-ownerships file; `--team` equal to self refuses.
3. `update`: existing assignments preserved verbatim **including via alias keys** (an entry under a mirror alias + its canonical discovered form = one repo, one entry — no duplicates); `mirrors[]` preserved verbatim; new repos appended per flag; absent-but-assigned repos kept + warned — each a selftest case; output byte-deterministic.
4. Generated file feeds `ua ingest`/`ua plan` unchanged (selftest: generate → ingest with it → plan schedules what it should).
5. Zero planner/report changes; existing 83 cases untouched.

## 5. Out of scope

- Fetching ownership from GitHub teams/API (needs network + auth decisions — future spec).
- Renames/deduplication beyond SPEC-011's identity rules; multi-team conventions (prefix rules etc.) — a future `--map` flag once a real estate needs it.

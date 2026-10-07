# Round 1 triage — brief-001 / PR #1 (2026-10-02)

**Verdicts:** Kiro: SPEC-000 **FIX THEN SHIP**, SPEC-001 **FIX THEN SHIP** (1 blocker). Codex: 2× P1 (map to the blocker + M1). Qodo: 3 High + 6 Medium (all map below). Sourcery: rate-limited, no findings.
**Raw logs:** [raw-review-bodies.md](raw-review-bodies.md) · [raw-inline-comments.md](raw-inline-comments.md) — verbatim, unedited.
**Cross-reviewer convergence on the two biggest findings** (producer evidence, transitive paths) is itself signal: those were real.

| # | Finding (source) | Severity | Disposition | Fix |
|---|---|---|---|---|
| B1 | Producer-evidence rung 4 has no source in tracemap (Kiro blocker; Codex P1-1; Qodo #1) | **Blocker** | Accepted | SPEC-001 §2: new input `producer-evidence.v0` sidecar (fixture-declared facts, `source: fixture-declared`, treated as declared-source rung). Rungs 1–3 removed from V0 (moved to future rungs). **B1(b)** — add `PackageProduced` facts to tracemap — recommended to Joe as parallel work in his repo (his call; does not block V0). |
| M1 | Transitive paths unreconstructable from declared inputs; lockfile `names` null >256 chars (Kiro M1; Codex P1-2; Qodo #2) | Major | Accepted | SPEC-001 §2: `packages.lock.json` rows (direct/transitive/unknown + 256-char caveat). SPEC-000: F4 split into **F4a** (lockfile present → path reconstructed) / **F4b** (absent → `Unknown` + gap, never "excluded"). |
| M2 | "Excluded" invites silent resolution via no-match (Kiro M2) | Major | Accepted | Three-state inclusion: `affected \| not-affected (positive evidence) \| unknown`. SPEC-000 §2.4 + acceptance 2. |
| M3 | Contradiction recorded but wave still firm; "outrank" = silent resolution (Kiro M3; Qodo #3) | Major | Accepted | SPEC-001 §4/§5: precedence affects confidence display ONLY; downstream of contradicted producer → provisional wave `blocked-on: <contradiction id>`. Corroboration = independent sources (distinct repo+commit+rule). |
| M4 | F2 ownership has no input source; outside-team step looks executable (Kiro M4; Qodo #9) | Major | Accepted | SPEC-001 §2: `ownership.v0` input (repo→team + self-team id; missing → `Unknown`, never "ours"). SPEC-000 F2: R4 step = `external — request/await`, never executable. |
| M5 | Fixtures are prose; fidelity rule has nothing to hold (Kiro M5) | Major | Accepted | SPEC-000 §4: every fixture = input files + **golden `plan.json`** (SPEC-005 piece pulled forward); F1 edges fully named. |
| M6 | Missing demonstrated shapes (Kiro M6) | Major | Accepted | SPEC-000 §4: **F5** third-party-via-internal (most common real alert), **F6** packages.config, **F7** CPM + VersionOverride, **F8** partial publication → conditional next wave, **cycle** detect-and-refuse (full handling stays SPEC-003). |
| m1 | ReleaseUnit false prerequisite (Kiro m1; Qodo #7) | Minor | Accepted | `basis: assumed-same-repo \| evidenced` in JSON (not prose) + **F1b** independent-versioning fixture. |
| m2 | PackageIdentity conflates id and version (Kiro m2; Qodo #6) | Minor | Accepted | Split `PackageId` / `PackageVersion?` — unknown version still forms an edge. |
| m3 | Repo identity normalization undefined (Kiro m3) | Minor | Accepted | SPEC-001 §3: normalization rules (case, `.git` suffix, mirrors). |
| m4 | "On Joe's work machine" not agent-repeatable (Kiro m4) | Minor | Accepted | SPEC-000 §5: network-disabled Windows CI run (e.g. `windows-latest`, network blocked) + Joe hands-on as final sign-off. |
| m5 | Pinned schema (Kiro m5) | Minor | Accepted + verified | Coordinator verified against local tracemap checkout: `package-delta.v1` = `{version, sourceRepo, sourceCommitSha, changes[{id, packageName, ecosystem, changeType, oldVersion, newVersion}]}` per `samples/package-deltas/package-delta.example.json`. Recorded in SPEC-001 §2. |
| m6 | Doctrine "zero NEW" can close with open majors (Kiro m6; Qodo #5 High) | Minor (doctrine) | Accepted | REVIEW-DOCTRINE §2: "zero **open** blockers/majors"; delta rounds must verify carried blockers/majors resolved. |
| Q4 | Lane: required-quorum vs silence-=-proceed conflict (Qodo #4 High) | Lane config | Accepted | Lane: `minimumReturned: 1`, `preferAllReturned: true`; doctrine's 1-working-day silence rule governs owner-ferried reviewers; description updated. |
| Q10 | Lane: fallback can run past round 3 (Qodo #10) | Lane config | Accepted | Lane: fallback explicitly bounded WITHIN the 3-round budget (substitute reviewer for remaining cycles); after ceiling → typed human-decision stop, `postTerminalComment` retained. |
| Q8 | Waves omit publication-availability statements (Qodo #8) | Medium | Accepted | Folded into M6/F8: report states publication/restore prerequisites explicitly, claims no live verification. |

**Not accepted / deferred:** none rejected. B1(b) (tracemap `PackageProduced` slice) is recommended-but-deferred to Joe — it is work in his other repo.

**Round 2 scope (delta review):** verify B1, M1–M6, m1–m6, Q4, Q10 fixes; no new full re-review per doctrine §1.

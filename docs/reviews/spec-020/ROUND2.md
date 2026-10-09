# SPEC-019 + SPEC-020 round 2 — delta verification + verdicts (2026-10-09)

**Reviewer:** verification stand-in (delta brief; both specs in one pass — designed as a pair).

## SPEC-019 — VERDICT: SHIP

Both blockers (19-1 consumer-path exclusion, 19-2 the build-freshness fixture channel) and all three
majors verified fixed against ground truth, including contradiction checks (no LoadEngine filename
collision — the allowlist is exact; no Q1/Q2 collision with F10's lockfile-closure text — additive
qualifiers with lockfile-only repos pinned byte-identical; v2 cache bump safe both directions).
Five new minors returned → all folded inline (fetch ownership pinned; drift dedup precedence
lockfile > deps.json > declared-pin + validator vocabulary; `.worktrees/` moved outside the scans
root with `git worktree remove` cleanup; amendment markers added to the depends-on header; the
not-found Note's globs cause was already in). Zero open blockers/majors — doctrine §2 convergence.

## SPEC-020 — VERDICT: SHIP (after one residual fix, applied)

20-1's mechanism half was resolved (fusion-time provenance); the acceptance half still named the
wrong scan set — the corpus's only `PackageProduced` fact lives in `scans/corelib`, not
billing/shipping/svc, so acceptance 1 as written was unfalsifiable-by-construction. **Fixed inline**
(scan set now includes corelib; the absent-provenance fallback names its in-band marker — the
`EvidenceNote: "tracemap PackageProduenced (…)"` convention; produced-never-consumed spelling
clause added). 20-2/20-3 and all minors verified resolved. Zero open items.

Both specs proceed to implementation on Joe's go.

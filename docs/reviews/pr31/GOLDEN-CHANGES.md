# PR #31 golden changes (SPEC-015 implementation)

Rule approved by Joe 2026-10-04: package-relevant problems stay gaps; compile/build health becomes
visible notes. Deltas below were each adjudicated against spec §4 BEFORE capture.

| Fixture | Change | Spec rule |
|---|---|---|
| F9 | + `scanNotes` (5 compile-health lines); `svc coverage` gap detail now cites the lockfile-group problem (first package-relevant gap) instead of a compiler diagnostic; report gains `### Scan notes` | §2 compile→Note; §3 |
| F12 | billing/shipping coverage `gaps→complete`; their SPEC-012 G2 `(lockfile-evidenced)` coverage gaps REMOVED from the plan (the intended effect — compile noise was their only gap source); + scanNotes + report section | §4 |
| F13 | + scanNotes (svc); `svc coverage` gap detail as F9; push dry-run golden UNCHANGED (findings only) | §4 |
| F10/F11/F14/F15 + all others | byte-identical (F11's unevidenced-version gap is package-relevant and stays) | — |

Also: fixture inputs for F9/F12/F13 regenerated from the committed scans with the new ingest (their
inputs ARE ingest outputs); ownership golden untouched. findings-ux selftest assertion updated to
the new F12 truth (complete; no G2 gaps) — intentional, recorded here.

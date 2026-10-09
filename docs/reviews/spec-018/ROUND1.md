# SPEC-018 round 1 — triage (2026-10-09)

**Quorum record (doctrine §5):** no external reviewer CLIs in this environment; the round ran as
coordinator + two independent stand-ins (adversarial brief + verification brief, both with repo
access). The implementation PR gets the external quorum (Baz/Codex) as on SPEC-017's PR #2.

**Verdicts:** Reviewer A (adversarial): **FIX THEN SHIP** (0 blockers, 5 majors, 6 minors, 2 questions).
Reviewer B (verifier): **FIX THEN SHIP** (2 blockers, 5 majors, 6 minors, 1 question).
Heavy cross-reviewer convergence: the load-contract and casing seams were found by both.

| # | Finding (source) | Severity | Disposition | Fix applied |
|---|---|---|---|---|
| A5/B1 | "Same inputs, same code path" hides that LoadEngine REQUIRES a single-change delta.json (and 3 other files) — contradicts the spec's drift-first purpose; no acceptance check | **Blocker** (A: major) | Accepted | §4 now lists the full required input set; delta = loader precondition only, content never read; driftless/2-change acceptance cases added |
| A1/B2 | Casing seam: emitted delta packageName from the FEED spelling would silently plan to nothing (planner matches ordinally); the proposed smoke check blesses empty plans | **Blocker** (A: major) | Accepted | §4 pins: emitted packageName = ESTATE spelling, never the feed's; criterion 6 asserts the expected affected set, not just validity |
| A2 | `unclassified` promised "reason in the row" but §6's row shape had no reason field | Major | Accepted | `reason` on unclassified rows only; exhaustive templates D1/D2/D3 (validator-mappable, SPEC-003 precedent) |
| A3/B6 | Folder-feed grammar had two readings; greedy-longest misparses numeric-ending ids; prerelease-with-dots and hyphenated ids unpinned | Major | Accepted | Algorithm pinned (longest trailing `core(.core){0,3}(-pre)?(+build)?` from the right) with worked examples; numeric-ending ambiguity documented in §3+§9; criterion 1 extended with all four shapes |
| A4/B14 | Folder feed with two versions of one id: implied exit 5, never stated, wrong rationale (accumulation is NORMAL for real feeds) | Major | Accepted | §3: exit 5 naming both files, with the honest rationale (latest-selection needs prerelease ordering V0 refuses); accumulating feeds = §9 future slice; criterion 1 case added |
| B4 | The sketched fixture recipe could not cover all five statuses (billing/shipping carry only 2 packages; no declared-pin survivor possible) | Major | Accepted | Recipe pinned to billing+shipping+svc+cleanrepo with a VERIFIED status table (checked against the committed scans); cleanrepo = the declared-pin survivor; feed file location pinned (input/) |
| A8/B5 | "Passes input-permutation" not checkable — the existing loop only exercises plan goldens | Major | Accepted | §8: selftest grows a drift-permutation case (arrays reversed incl. feed packages[]) |
| A7/B7 | Emitted delta field set incomplete (`ecosystem` missing; hollow-delta smoke passes) | Major→minor (folded) | Accepted | §7 enumerates the full field set, `ecosystem: "nuget"` fixed; emitted files byte-golden under `golden/deltas/` |
| A6/B15 | `source`/`asOf` echo semantics; absent-key representation; folder enumeration order; packageCount pre/post collapse; repo identity | Minor | Accepted | §3/§6 pinned: source validated against the two-value set; asOf omitted when absent; ordinal filename order; post-collapse count; repo keys verbatim |
| A9 | "The SPEC-009 §3 classifier" doesn't exist as a shareable thing; apply's gate has a line requirement drift must drop | Minor | Accepted | §4 names the extracted `IsExactPin` helper + the two deliberate divergences (no line gate, no compare target) |
| A10/B8 | Exit-code holes and precedence; repeated flags | Minor | Accepted | §2: precedence sentence (flags → fixture → feed → classify → emit); missing feed FILE = exit 1; dual-fault case in criterion 9; first-wins repeated flags stated |
| B10 | `--emit-deltas` edges: zero-behind dir creation, safeId replacement set, collisions | Minor | Accepted | §7: dir iff ≥1 candidate; replacement set `[A-Za-z0-9._-]`; collision = typed error naming both ids |
| B9 | "Validator-aware" unfalsifiable | Minor | Accepted | Criterion 9 names the validator additions (parse, schemaVersion, status vocabulary, summary↔packages consistency, canonical bytes) |
| B11 | No `--sanitized` acceptance for drift | Minor | Accepted | Criterion 5 extended (sanitized stdout byte-identical) |
| B12 | "Shapes apply also judges" is not a spec | Minor | Accepted | Criterion 4: shapes asserted row-by-row against `Apply.CompareCore` directly (structural parity) |
| A12 | `unknownFeed` carried different units at summary vs per-package statuses | Minor | Accepted | Summary key renamed `unknownFeedPackages`; per-package statuses drop the key (row-level status only) |
| A11 | Smoke should assert apply-usability, not just plan-validity | Question | Accepted (folded) | Criterion 6's expected-affected-set assertion covers it |
| B13 | scope.v0.json behavior unstated | Minor | Accepted | §4: loaded-and-ignored (malformed still exit 3 — shared validator); scope is plan/report's statement |

**Not accepted / deferred:** none rejected. Real accumulating folder feeds deferred to §9 (needs
prerelease-aware ordering — a deliberate V0 refusal, now stated with its rationale).

**Round 2 scope (delta review):** verify the blockers/majors landed as described; no full re-review.

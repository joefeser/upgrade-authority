# SPEC-021 round 2 — delta verification + verdict (2026-10-10)

**Reviewer:** verification stand-in (delta brief; verified every round-1 blocker/major against the v2 text AND the cited code sites).

## Verdict: SHIP

All 20 round-1 dispositions verified landed and code-coherent, including the two blockers:
- **R1-1** — `affectedPkgs` fold verified against the actual closure logic (variant `touches` ⇒ `unknown`, never `not-affected`).
- **R2-1** — the per-repo-scoped-copies mechanism verified to work WITH the pinned multi-repo refusals (it is Apply.cs:39's own prescribed path); case preservation verified against `AttrPatterns`' span-scoped rewrite.

Spot-verified beyond the findings: the hand-rolled apply writer's conditional-field idiom (R1-2 fits it), the dedup/emission byte-determinism (stable sort + ordinal spelling tie-break ⇒ well-defined golden bytes), the F5 observables being real plan fields, and the load-rejection channel (LoadEngine, SPEC-007 precedent).

**Zero open blockers, zero open majors — doctrine §2 convergence; review ends.**

Four late minors returned → all folded inline (logged here per doctrine §3):
- **N1** — ingest warning rewording qualified to PACKAGE ids (repo identity stays exact; the same warning site also fires for repo-spelling variants where the old text stays true).
- **N2** — acceptance 6 now pins the case-insensitive push precondition refusal (differently-spelled delta ⇒ refusal, never a second branch).
- **N3** — §4 pointers corrected (SPEC-009 §3/§4/§5; SPEC-010 §3.1/§5).
- **N4** — ROUND1.md's R1-6/7 disposition text updated to describe what v2 actually does (the §6 rewrite was dropped in favor of the fold + typed rejection).

Proceeds to implementation.

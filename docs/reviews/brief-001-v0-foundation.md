# Review brief 001 — SPEC-000 + SPEC-001 (for Codex and Kiro)

**You are reviewing specs, not code. Nothing is built yet — that is the point. Fight it out now so we don't build the wrong thing.**

## Rules of engagement (see docs/specs/REVIEW-DOCTRINE.md)

This is **round 1 of at most 3** — bring your strongest objections NOW; issues raised after round 1 only carry weight if they are blockers. Zero new blockers/majors ends review immediately. Your output verdict must be one of: SHIP / FIX THEN SHIP / REWORK (premise wrong). Frame objections as missing acceptance checks wherever possible. If you disagree with the other reviewer, say so plainly — the coordinator breaks ties.

## Context (self-contained; you have no other session history)

We are planning a .NET dependency-upgrade orchestration platform for a large private estate (many repos; one repo publishes a compiled-together package family; private NuGet feeds; CI; alerts arrive via Dependabot/GHAS and commercial scanners). The core insight: repo→package **producer ownership** plus **ordered upgrade waves across release units** is the unsolved problem; tree-walking NuGet alone fails.

An existing Apache-2.0 project, **tracemap** (github.com/joefeser/tracemap), already indexes repos into deterministic static evidence and emits `package-delta.v1` + package-reference facts (sources, SHAs, rule IDs, gaps). Decision made: our new tool **consumes** tracemap evidence and does what tracemap explicitly excludes — resolution, sequencing, impact planning, campaigns.

**V0 constraint (owner's explicit intent):** working validation in **less than a week** on sanitized examples — small slices touched early, not a big-bang build. Must run locally on a Windows work machine, offline. An earlier agent incident (test cases refused/substituted; two regressions costing ~30 recovery hours each) produced a hard rule: demonstrated cases become explicit acceptance checks, and substituting/weakening them requires a recorded reason and review.

## What to review

Read these two files (paths from repo root):

1. `docs/specs/SPEC-000-v0-definition.md` — V0 scope, sanitized corpus fixtures F1–F4, acceptance criteria, Friday-2026-10-09 milestone.
2. `docs/specs/SPEC-001-evidence-and-graph-model.md` — tracemap evidence ingestion, graph entities, evidence-precedence ladder, uncertainty representation, HACP consideration.

## Questions to answer (verdict + issues by severity)

1. **Scope:** Does V0 as scoped deliver a convincing validation slice in <1 week, or is anything included that bakes in a wrong assumption we'd pay for later? Is anything obviously missing that would make the impact plan untrustworthy?
2. **Evidence ladder (SPEC-001 §4):** Is the precedence order (publish receipt > produced nuspec > evaluated MSBuild > declared source; YAML heuristics excluded) sound? Any rung we cannot actually obtain from tracemap evidence in V0?
3. **Uncertainty:** Is "record contradictions, never invent" enforceable as specified? Where would an implementation be tempted to silently resolve a conflict?
4. **Fixtures:** Do F1–F4 cover the shapes that made manual work hell (8-package repo, outside-team producer, ambiguous/conflicting evidence, direct+transitive mix)? What demonstrated shape is missing?
5. **ReleaseUnit assumption:** Treating one repo's packages as a single release unit (flagged assumption) — acceptable for V0, or a landmine?
6. **Biggest risk:** If you had to kill one thing in these specs before build, what and why?

## Output format

One verdict per spec (SHIP / FIX THEN SHIP / REWORK) + numbered issues with severity (blocker / major / minor) + any missing acceptance check you'd add. Be adversarial; the cost of a wrong spec is measured in 30-hour recoveries.

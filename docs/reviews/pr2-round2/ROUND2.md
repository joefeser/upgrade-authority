# PR #2 round 2 — inputs received and disposition (2026-10-02)

**Joe's process correction (recorded in doctrine §5 + lane):** *"With ack qodo and codex are a quorum so we normally wait for all reviews before patching."* Quorum = Codex + Qodo (auto-reviewers); one consolidated fix pass per round; Kiro/Sourcery additive, never blocking. Coordinator had patched round 1 on Qodo alone before Codex returned — noted as the process miss this correction prevents.

**Round-2 inputs (Joe: "Reviews done"):**

| Reviewer | Result | Logged |
|---|---|---|
| Sourcery | **APPROVED** (16:15Z, on fixed head; auto-generated Reviewer's Guide summarizes the corpus accurately) | [sourcery-guide.md](sourcery-guide.md) |
| Codex | Round 2 completed on `d57797e`: **2× P1 inline** (below) | in Codex summary comment + inline (quoted below) |
| Qodo | No round-2 findings returned post-fix (round-1's 13 fully dispositioned in round 1) | — |
| Kiro | Silent (quota); additive, blockers-only if late | — |

**Codex round-2 P1s — both accepted and fixed:**

1. **F5 — public-package attribution still not evidence-derived.** Producer absence only establishes *unknown* producer (F3a proves the rule); the golden still said "public package" + "public feed". **Fix:** `producer-evidence.v0` gained an optional `externalPackages: [{packageId, note?}]` declaration (evidence-backed attribution); F5's input declares N1 external; golden reasons and the wave-1 prerequisite now cite that declaration. Schema doc updated.
2. **F1b — shared prerequisite not byte-identical to F1's.** Deterministic renderer cannot format the same fact differently because P9/P10 exist. **Fix:** F1b wave-2 prerequisite copied exactly from F1 ("…to the configured feed…").

**Convergence check (doctrine §2):** zero open blockers, zero open majors. All 12 goldens pass the hardened structural validator; lane doctor-validated with the new codex+qodo quorum. **Corpus verdict: SHIP — merge.**

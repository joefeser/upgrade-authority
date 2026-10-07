# PR #16 fidelity notes (SPEC-010 implementation)

## Goldens
- F14/F15 (gated execution fixtures): minimal hand-authored INPUTS; plan/report goldens
  engine-derived then reviewed line-by-line against the SPEC-002/003 rules and their reference
  fixtures (F14 mirrors F2's chained-conditional shape; F15 mirrors F3b's provisional shape) —
  recorded as a deliberate expedience; the corpus validator + 23-fixture byte-checks guard them
  going forward. F13's push goldens: dry-run report byte-captured; local execution asserted
  structurally (shas embed git identity+time by design — SPEC-010 §3.6).
- No existing fixture changed.

## Notable implementation decisions
- Verification runs INSIDE a temporary worktree of the execution branch (base tree), then
  `git apply` + `git commit -a` there — the user's checkout never switches branches; commits
  update the branch ref (worktree NOT detached — a detached worktree silently orphans commits).
- A verification refusal inside push propagates apply's exit 6 (an apply refusal wherever it
  happens); push-specific refusals are exit 7. Both roll back branch+worktree first.
- GITHUB_TOKEN is unset/restored around the token-refusal selftest case.

## Deferred (needs Joe)
- One-time manual API validation against a real throwaway repo (spec acceptance 6): requires
  Joe's GITHUB_TOKEN and a throwaway private repo — flagged, not self-served.

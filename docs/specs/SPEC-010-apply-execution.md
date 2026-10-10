# SPEC-010 — `ua push`: apply execution (branches, commits, PRs)

**Status:** draft — round 1 review (brief-015)
**Author:** ZCode (coordinator)
**Date:** 2026-10-03
**Depends on:**
> **Amended by SPEC-021 (2026-10-10):** precondition refs compare case-insensitively (a differently-spelled delta for the same logical upgrade refuses — never a second branch/PR); branch/commit/PR text keeps the delta spelling (echo). SPEC-009 (`ua apply` v1 — verified patches) · SPEC-003 (waves/gates) · SPEC-004 (report prose conventions)

## 1. Purpose

Execute what `ua apply` verified: for each wave, create a git branch in the repo checkout, apply that wave's verified patch as a commit, and (opt-in) open a GitHub pull request. A human merges — the tool never merges, never pushes to a base branch, and never force-updates anything.

## 2. Command

```
ua push <fixture-dir> --repo <checkout> [--out <dir>] [--base <branch>] [--pr] [--dry-run]
```

- `--repo` **required for execution**; **optional under `--dry-run`** (a fixture-only dry run — no checkout, no git, no preconditions: base-dependent refusals are reported as `not-checked (no checkout)`; with `--repo` and `--dry-run`, all preconditions are CHECKED and recorded but nothing mutates).
- `--base <branch>` **required**: the branch PRs target. Never guessed, never defaulted (an assumed base is a silent wrong-target).
- `--out <dir>` **required**: where the execution report (`push.v1.json`) lands (atomic swap, house rule).
- `--dry-run`: compute and write `push.v1.json` (branch names, commit messages, PR titles/bodies, the exact commands that would run) but perform **no git mutations and no network calls**.
- `--pr`: also open pull requests via the GitHub REST API (`GITHUB_TOKEN` env var required; github.com only in v1 — GitHub Enterprise base-URL override queued). Without `--pr`, execution is local-only: branch + commit, no push, no API. **Local-only is the default** because V0's posture is offline-first; the network step is an explicit opt-in.
- Prerequisite (execution and `--repo` dry runs only): the `git` CLI on PATH (V0 shells out to git rather than reimplementing object storage; checked with a typed error up front). A fixture-only `--dry-run` performs **no git check, no git invocation** — it runs on machines without git (acceptance 2).

## 3. Execution model

Per wave with edits, on the single `--repo` checkout (multi-repo manifests remain a typed error, inherited from SPEC-009):

1. **Preconditions (typed refusals, exit 7 — nothing mutated):**
   - Checkout must be a git work tree; `git status --porcelain` must be **empty** (uncommitted changes refuse).
   - `--base` must exist in the checkout.
   - The target branch name must **not** already exist (idempotency-by-refusal — a re-run never force-updates; the existing branch is a human decision).
   - With `--pr`: `GITHUB_TOKEN` must be set; `git remote get-url origin` must yield a github.com repo (owner/name parseable).
2. **Branch:** `ua/{wave-N}/{pkg}-{oldVersion}-to-{newVersion}` — deterministic, wave-scoped, readable (package ids keep their dots; slashes/dots in versions are legal branch characters). Created from `--base` (`git branch <name> <base>` — no checkout juggling).
3. **Verification happens against the tree being committed (base), not the user's working tree:** a temporary worktree is created for the branch (`git worktree add` — the user's checkout is never left on another branch), and SPEC-009 §5's site verification + patch generation run against the WORKTREE path. A clean working tree on a different branch therefore cannot slip unverified bytes into the commit. Verification failure here is a typed refusal with rollback (rule 3b).
3b. **Rollback:** if anything fails between branch creation and a successful commit (worktree, verification, patch application, commit hooks/identity/signing), the worktree is removed and the just-created branch deleted — the invocation leaves the repo exactly as found, and a retry starts clean.
4. **Commit:** the wave's patch applied in the worktree, then `git commit` with a deterministic message:
   - subject: `ua: {pkg} {oldVersion} → {newVersion} (wave {N})` (ASCII arrow — commit subjects stay tooling-safe)
   - body: the wave's status and gates first (`wave-status: {status}`, and `condition: {text}` / `blocked-on: {cid}` when present — a cherry-picked or manually-opened commit must never look ready when it is gated), then prerequisites verbatim (each prefixed `prerequisite: `), plus `evidence: upgrade-authority apply.v1 verified {K} edit(s)` and `generated-by: ua push (SPEC-010)`.
5. **PR (only with `--pr`):** push the branch (`git push -u origin <branch>`), then `POST /repos/{owner}/{repo}/pulls` with title = the commit subject and a deterministic body:
   - the delta line (package, old → new, ecosystem);
   - the wave's status and its gates (`condition`/`blockedOn` when present, prefixed appropriately);
   - the prerequisites list;
   - the edited files with their evidence kinds;
   - the plan's findings for this repo (disagreements are decision-relevant for a reviewer);
   - `stated, not verified live` language preserved verbatim from the plan.
   PR creation failure (network, auth, 422) is a typed refusal (exit 7) AFTER the local branch+commit succeed — the local work stands, the report says exactly which step failed, and re-running refuses on the existing branch (the human decides: delete it, or push manually).
6. **Report:** `push.v1.json` (canonical serialization) in two objects — `waves[]` (deterministic: branch, base, commit message, intended commands, PR title/body text — **byte-exact goldens**) and `observed[]` (commit sha, PR url, failure point — asserted structurally, never goldened: SHAs embed identity and time by git's design).

## 4. Determinism

- All generated text (branch names, commit subjects/bodies, PR titles/bodies) comes from an exhaustive template table (§5) — byte-exact goldens for `push.v1.json` in dry-run mode; no timestamps, no hostnames, no user names in any generated artifact (commit author is the checkout's git config — the USER's identity, never invented by the tool).
- Wave order = plan order; one branch per wave-with-edits; skipped waves (no edits) are recorded, not branched.
- `--dry-run` output is a pure function of the fixture (not the checkout — it must run anywhere): it records intended commands with `<branch>`/`<base>` placeholders resolved from arguments.

## 5. Templates (exhaustive)

| id | text |
|---|---|
| P1 | branch: `ua/{wave-N}/{pkg}-{old}-to-{new}` |
| P2 | commit subject: `ua: {pkg} {old} → {new} (wave {N})` |
| P3a | commit body line: `wave-status: {status}` |
| P3b | commit body line: `condition: {text}` |
| P3c | commit body line: `blocked-on: {cid}` |
| P3 | commit body line: `prerequisite: {text}` |
| P4 | commit trailer: `evidence: upgrade-authority apply.v1 verified {K} edit(s)` |
| P5 | commit trailer: `generated-by: ua push (SPEC-010)` |
| P6 | PR title: = P2 |
| P7 | PR body heading: `## Upgrade {pkg} {old} → {new} (wave {N} of {M})` |
| P8 | PR body line: `- status: {status}` / `- condition: {text}` / `- blocked on: {cid}` |
| P9 | PR body line: `- prerequisite: {text}` |
| P10 | PR body line: `- edit: {path} ({kind}, {evidenceKind})` |
| P11 | PR body line: `- finding: {subject} — {detail}` |
| P12 | PR body footer: `Human merges. Generated by ua push; every edit was evidence-verified against this checkout before commit.` |

## 6. Acceptance criteria

1. **Local execution, machine-checked forever (selftest):** scratch git checkout of `sources/svc` (git init + commit, no network): `ua push F13 --repo <scratch> --base <b> --out …` creates branch `ua/wave-1/Contoso.Core-1.0.0-to-1.1.0`, one commit with the P2–P5 text (P3a–c when gated), working tree clean after, user's checked-out branch unchanged; the `waves[]` object of `push.v1.json` matches its golden byte-exactly; `observed[]` carries the real sha.
2. **Dry-run golden:** `--dry-run` on F13 (NO `--repo`) writes the deterministic `waves[]` object with intended commands recorded — byte-exact golden, fixture-only; with `--repo`, preconditions are checked and their outcomes recorded, still without mutation.
3a. **Gated-wave commits, each a selftest case:** local executions on gated plans — one conditional (F2: commit body carries `wave-status: conditional` + `condition: {text}`, no `blocked-on`) and one provisional (F3b: `wave-status: provisional` + `blocked-on: C1`) — so P3a–c are exercised, not just P3a on F13's ready wave.
3. **Typed refusals (exit 7), each a selftest case:** dirty work tree; missing `--base` branch; branch already exists (re-run after success refuses; nothing force-updated); **commit failure rolls back** (a scratch checkout with a failing pre-commit hook refuses AND `git for-each-ref` proves the branch never survived); **base-tree verification** (a base whose pin drifted refuses naming the site, even when the user's working tree matches the evidence).
4. **Idempotency:** after a successful local run, a second run refuses on the existing branch and mutates nothing (verified: `git for-each-ref` identical before/after).
5. **PR mode without network is honest:** `--pr` without `GITHUB_TOKEN` ⇒ typed refusal exit 7 before any mutation.
6. **API path (thin, manually validated once):** one recorded run against a real throwaway repo (scratch, private) with `GITHUB_TOKEN`: PR opened with the P6–P12 body, URL recorded in `docs/reviews/pr16/API-VALIDATION.md`; the selftest does NOT hit the network (offline posture preserved — the API call is a single reviewed function).
7. **Zero churn:** `ua plan/report/apply` outputs byte-identical; existing 58-case selftest green; new cases bring the total ≥ 63.

## 7. Out of scope (typed, honest, logged)

- **Merging PRs** — ever (human decision; a future flag would need its own spec and Joe's explicit call).
- **Multiple repos in one run** — per-repo execution, inherited from SPEC-009 v1.
- **GitHub Enterprise / other forges** — `--github-api <url>` override queued; v1 is github.com.
- **Rebase/force-push/retry semantics** on existing branches — refusal, human decides.
- **CI status waiting, review assignment, labels** — PR body text only; no API mutations beyond opening the PR.
- **Commit signing** — inherits the checkout's git config; no `--gpg-sign` flag in v1.

## 8. Open questions (coordinator defaults, Joe overrides)

1. Command name `ua push` (short, accurate). Alternatives considered: `submit`, `open`.
2. One PR per wave (matches the plan's gating semantics) rather than one PR per repo or per edit.
3. Local-only default with `--pr` opt-in (offline-first posture).
4. Worktree-based commits so the user's checkout never switches branches.

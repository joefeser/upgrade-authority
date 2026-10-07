# SPEC-013 — `--github-api`: GitHub Enterprise override for `ua push --pr`

**Status:** draft — round 1 review (brief-021)
**Author:** ZCode (coordinator)
**Date:** 2026-10-03
**Depends on:** SPEC-010 (`ua push`, PR mode)

## 1. Purpose

`ua push --pr` targets github.com only in v1. Estates on GitHub Enterprise need the API base URL and matching origin validation parameterized. This is deliberately narrow: one flag, one env var, and the two places host-awareness exists (origin validation, the API base).

## 2. Command surface

- `ua push … --pr --github-api <base-url>` — API base for PR creation. Default: `https://api.github.com`.
- Environment equivalent: `GITHUB_API_URL` (flag wins; both unset ⇒ default).
- **HTTPS required (loopback carve-out):** a base that is not `https://…` is a **typed refusal** — the request carries `GITHUB_TOKEN`, and plaintext transports are never acceptable for a credential, internal network or not. **One narrow exception:** `http://127.0.0.1:<port>` / `http://localhost:<port>` are accepted as an explicit testing carve-out — loopback traffic never leaves the host, which is materially different from the plaintext-transport threat, and it is what makes the override testable offline (§5.1b). Any other http base refuses.
- **No credentials in the base:** a base carrying userinfo (`https://user:pass@…`) is a **typed refusal** — bases are written into `push.v1.json`; embedded credentials must never land in an artifact.
- **Base normalization:** a trailing `/` is trimmed; a base ending in `/api/v3` is used as-is; a bare GHE host (e.g. `https://ghe.example.com`) is NOT auto-suffixed — the caller passes the full API base (`https://ghe.example.com/api/v3`), because guessing GHE layouts has caused silent 404s historically. A base that neither equals the default nor contains `/api/` logs an informational line naming the base in use (visibility, not refusal).
- PR endpoint: `{base}/repos/{owner}/{repo}/pulls` (unchanged path).

## 3. Host-aware origin validation

- Default base ⇒ today's rule (origin must be `github.com[:/]…`).
- Non-default base ⇒ origin must parse as an https/web remote `https://host[:port]/owner/repo(.git)` **or** an scp-style SSH remote `user@host:owner/repo(.git)` (common on GHE checkouts; owner/repo extracted identically) — the base names the forge; `git push` goes to `origin` whatever it is, and the PR API call goes to the base. Mismatch between the origin's host and the base's host is **not** an error (proxied/mirrored remotes are legal) but logs an informational line naming both (visibility).

## 4. Report + dry-run

- `push.v1.json` `waves[].commands` PR line renders the actual endpoint base (dry-run shows the override without any network).
- `observed[]` PR entries unchanged (URL comes from the API response).

## 5. Acceptance criteria

1. Dry-run with `--github-api https://ghe.example.com/api/v3` renders the GHE endpoint in the commands list — byte-checked selftest case.
1b. **The override is exercised on the real HTTP path:** a selftest starts an in-process loopback HTTP stub (allowed by the carve-out above), points `--github-api` at it with `--pr`, and asserts the stub received `POST /repos/{owner}/{repo}/pulls` with the auth header — proving `OpenPullRequest` itself uses the base, not just the report renderer. The stub returns a canned PR body; no external network is touched (offline VM safe).
2. Default base keeps github.com-only origin validation (existing case, unchanged); overridden base accepts a non-github origin (scratch GHE-style remote passes preconditions) and logs the informational mismatch line when hosts differ (selftest).
3. Normalization: trailing slash trimmed; bare host accepted with the informational `/api/` note (selftest asserts the note, never a refusal); **non-loopback `http://` base ⇒ typed refusal**; **userinfo in the base ⇒ typed refusal** (selftest, both).
3a. **SSH-origin support:** with an overridden base, a scratch checkout whose `origin` is scp-style SSH (`git@ghe.example.com:owner/repo.git`) passes preconditions and the report carries the extracted owner/repo (selftest).
4. `GITHUB_API_URL` env honored, flag overrides env (selftest).
5. Zero golden churn; existing 78 cases untouched.

## 6. Out of scope

- **A real-GHE end-to-end validation** — no GHE instance exists in this environment; the network call itself is the same single reviewed function, now parameterized. Logged: when a real GHE target exists, run the SPEC-010-style one-time validation and record it.
- OAuth/App auth flows, multiple forges in one run, per-repo API bases.

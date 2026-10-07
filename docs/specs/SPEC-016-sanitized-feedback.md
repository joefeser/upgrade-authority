# SPEC-016 — `--sanitized`: shareable console output

**Status:** draft — round 1 review (brief-027)
**Author:** ZCode (coordinator); requested by Joe 2026-10-04 ("a way to report sanitized errors back")
**Date:** 2026-10-04
**Depends on:** tracemap's sanitization conventions (category-only diagnostics, value hashing, safe-charset gating) · SPEC-000 (honesty)

## 1. Purpose

When `ua` runs on a real work machine and something fails, the error output must be **safe to paste back** (GitHub issue, chat, agent) without carrying proprietary data off that machine — filesystem paths reveal directory structures and usernames; internal hostnames/URLs reveal infrastructure; organization-specific names are themselves proprietary. tracemap solves this for scan facts (category-only messages, `version-hash:`, safe-value regexes); this spec applies the same philosophy to `ua`'s console streams.

**The rule:** `--sanitized` is a global flag on every command. When present, **everything written to stderr passes through a redactor**, and **stdout is redacted EXCEPT canonical artifact emissions** (`ua plan` / `ua report` print the artifact itself to stdout — those writes bypass the redactor so the command's canonical output, including when redirected to a file, is byte-identical with or without the flag; sharing a PLAN is a different act from sharing diagnostics, covered in §5). File artifacts (patches, `push.v1.json`, fixture outputs) are never transformed.

## 2. Redaction rules (order matters; each pass over the whole line)

| id | Pattern | Replacement | Notes |
|---|---|---|---|
| R1 | URLs `https?://…` and scp-style `user@host:path` | host allowlisted AND the URL carries no userinfo/query/fragment ⇒ verbatim; otherwise `[url#{h8}]` | Allowlist: `github.com`, `api.github.com`, `nuget.org`, `api.nuget.org`, `example.invalid`, `localhost`, `127.0.0.1`. **Any userinfo, query string, or fragment forces redaction even on allowlisted hosts** (credentials ride those parts). Runs FIRST |
| R1b | Bare hostnames standing alone (not part of a larger path/URL): FQDNs (≥1 dot), single-label DNS names, IPv4 literals (esp. private ranges), with optional `:port` — matched ONLY in known host-bearing contexts (the `host '…'` / `:port` shapes our own messages emit) | allowlisted ⇒ verbatim; else `[host#{h8}]` | Context-anchored matching (quotes/word-boundary after `host`/`base host` markers or a trailing `:port`) rather than free-floating dotted-token detection — package ids never appear in those anchors, so over-redaction risk is contained while single-label GHE names (`ghe`) and private IPs (the mismatch note prints both) are covered |
| R2 | Windows absolute paths (`X:\…`) and UNC (`\\server\…`) | `[path:{leaf}#{h8}]` | Leaf filename kept (diagnostic value, rarely proprietary); hash = first 8 hex of SHA-256 of the original — stable for correlation across lines |
| R3 | Unix absolute paths (leading `/`) | `[path:{leaf}#{h8}]` | Repo-relative paths (`src/Api/...`) never start with `/` and pass through |
| R4 | Email addresses | `[email#{h8}]` | Runs BEFORE R1b (an internal-domain email's host part would otherwise partially match the bare-host rule and leave the local part exposed). Acceptance case uses a non-allowlisted internal domain |
| R5 | Each comma-separated entry of `UA_REDACT` (env) | `[tok#{h8}]` | **The user-controlled proprietary-name list** (e.g. `UA_REDACT=Northwind,Contoso`) — substring, case-insensitive. This is the deliberate answer to "our internal package/repo names are themselves sensitive": the tool cannot know which names are proprietary; the operator does |

`{h8}` = first 8 hex chars of SHA-256 (lowercase) of the original text — deterministic, so the same path redacts identically across lines and runs (correlation without disclosure), mirroring tracemap's `messageHash` purpose.

**Not redacted in v1 (deliberate, honest):** package ids, repo names, and version numbers that don't match R1–R5 — they are the subject of the tool's templated errors and usually required for debugging; the `UA_REDACT` list covers the sensitive-name case. SHA-1/SHA-256 hex strings pass through (no semantic content).

## 3. Behavior

- `--sanitized` is stripped from args before command parsing (usable anywhere in the command line, like `--`).
- Startup notice on stderr, once: `note: sanitized output enabled — paths→[path:…], non-allowlisted URLs→[url#…]; file artifacts are NOT transformed`.
- Both `Console.Out` and `Console.Error` are wrapped for the whole process (every write site — Program, Ingest, Planner errors, Push git stderr echoes — without touching each call site).
- A per-line transformation count is unnecessary: the notice + visible markers make redaction self-evident.
- Exit codes are unchanged. Nothing about command semantics changes; only the console channel is filtered.
- Selftest under `--sanitized`: not required (selftest output is fixture names); nothing breaks if used.

## 4. Acceptance criteria

1. **Adversarial unit cases (selftest):** the redactor transforms — a `C:\Users\jdoe\work\…\Directory.Packages.props` path; a UNC `\\fileserver\drop\…`; a unix `/home/jdoe/…`; an internal-host URL; a `git@git.internal.corp:org/repo` scp string; an email; a `UA_REDACT=Contoso` token — each to its marker form with the correct stable hash, while leaving intact: `src/Api/packages.lock.json`, allowlisted URLs, `ua/wave-1/Contoso.Core-1.0.0-to-1.1.0` (no UA_REDACT), pure-hex strings.
2. **Stability:** the same input redacts to the same marker across calls (hash determinism).
3. **E2E:** `Program.Main` invoked with a failing command + `--sanitized` (scan root under a temp path containing a fake username) — the captured stderr contains the `[path:` marker and NOT the username.
3a. **Bare-host e2e:** the GHE origin/base mismatch path prints `[host#…` markers, never the internal hostname — for an FQDN host, a single-label host (`https://ghe/api/v3`), and a private IPv4. **Credential e2e:** a URL with userinfo or a query string redacts even on an allowlisted host. **Email ordering e2e:** `user@company.internal` redacts whole (`[email#…]`, local part gone), not partially.
4. **UA_REDACT e2e:** with `UA_REDACT` set, a redacted stream never contains the listed substring (case-insensitive).
5. Zero behavior change without the flag (existing 94 cases untouched; goldens untouched).

## 5. Out of scope

- Transforming file artifacts (plans/reports/patches) — different feature (a future `--sanitized-report` if sharing plans becomes a need).
- Structured redaction telemetry (counts/auditing) beyond the startup notice.
- Auto-detecting proprietary names (impossible without operator knowledge — `UA_REDACT` is the honest mechanism).
- Redacting inside git subprocess output beyond what the stream wrapper already covers (it IS covered — git's stderr flows through our wrapped Console).

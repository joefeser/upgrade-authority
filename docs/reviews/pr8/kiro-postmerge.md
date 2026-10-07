# Kiro post-merge verification — SPEC-007 (PRs #7 + #8)

**Posted:** 2026-10-03 on PR #8 (Joe-ferried), against `dev @ 8054d11`, .NET SDK 10.0.401.
**Coordinator dispositions below the verbatim log.** All four verification items VERIFIED against the
real scan — the fix is confirmed against reality, not just against our fixtures.

## Verbatim (summary; full text on PR #8)

1. **VERIFIED** — `ua ingest` exit 0, 5 v1 rows with `lockfile`+`tfm`, both Newtonsoft.Json rows
   `via: Contoso.Core`. Kiro additionally proved per-lockfile parenting by stripping `dependencyNames`
   from Worker's Contoso.Core fact only: Worker's row became `via: null`, Api's kept `Contoso.Core`.
2. **VERIFIED** — plan: svc affected via `svc → Contoso.Core → Newtonsoft.Json`; one ready wave, T5;
   findings for Contoso.Core (1.0.0/0.9.0) and Newtonsoft.Json (13.0.1/12.0.3) both with the
   unparsed-groups trailer; no Serilog finding; no net48 rows anywhere.
3. **VERIFIED** — report: summary bullet + Findings section render; Serilog absent.
4. **VERIFIED** — `SELFTEST PASS (42 cases)` incl. `ingest-svc-e2e`.
   Also: PR #6 round-3 B4 confirmed landed (constraint-not-evidenced gaps in `scanCoverage.gaps`).

**Open-question answer (duplicate `(lockfilePath, TFM, package)` in one scan):** impossible from one
tracemap scan — `HasDuplicateJsonProperties` (ordinal set) turns the whole lockfile into a
`packages-lock-parse` gap on any duplicate JSON key; package names are object keys within a TFM group.
Edge cases: case-variant ids (`Serilog`/`serilog`) pass the ordinal check (NuGet never writes them);
realistic duplicate source is the same repo passed to ingest twice (two scans/two commits) — exact
duplicate collapses (exit 0), differing duplicate → typed exit 4; message could hint
"same repo ingested twice?". RID groups (`net8.0/win-x64`) are distinct TFMs, correctly.

**ADVICE for the next spec:** (1) findings should say which side is the delta's `oldVersion`;
(2) root cause invisible — VersionOverride not indexed by tracemap (same slice as PR #6 suggestion:
index `PackageVersion`/`VersionOverride`); (3) net48 gap possibly systemic (long-form
`.NETFramework,Version=v4.8` group key rejected by `SafeLockfileTargetFrameworkPattern`); (4) wave
unit line should say the edit site is not evidenced; (5) T10a trailer duplicated per finding — state
once per repo.

## Coordinator dispositions

| Item | Disposition | Action |
|---|---|---|
| Verdicts 1–4 | **ACCEPT (verified)** | SPEC-007 confirmed against reality. No code change. |
| Case-variant same-key detection | **ACCEPT (hardening, PR #9)** | Ingest + load same-key detection compares `packageId` `OrdinalIgnoreCase` (NuGet ids are case-insensitive); case-variant rows are never "identical in every field" → typed conflict, never silent collapse. |
| Exit-4 message hint | **ACCEPT (hardening, PR #9)** | Message now appends: same repo passed to ingest twice is the usual cause. |
| Advice 1 (oldVersion side) | **QUEUED — SPEC-008 candidate** | Pure function of evidence already in the plan; wording change + golden churn (fidelity-logged). |
| Advice 2 (VersionOverride root cause) | **QUEUED — needs tracemap slice first** | Same as PR #6 suggestion (index `PackageVersion`/`VersionOverride` in tracemap); upgrade-authority can then connect the findings. Joe's call on the tracemap work. |
| Advice 3 (net48 systemic risk) | **RESOLVED — not systemic (real-tool evidence, 2026-10-03)** | Real NuGet lock files key classic-framework groups by the SHORT form (`net48`, `net472`, `net481` — e.g. the Bazel `nuget_lockfile2repos.bzl` gist maps `"net48"`→.NETFramework4.8; WkHtmlToX repos lock net472/net48/net481). Renaming Kiro's synthetic group to `net48` in a scratch copy and running REAL tracemap (`scan --repo`) emits 4 net48 rows, zero `packages-lock-group-unsupported` gaps — `SafeLockfileTargetFrameworkPattern` accepts `net48`. Only the long form `.NETFramework,Version=v4.8` is rejected, and NuGet does not write it into `packages.lock.json`. Kiro's authored fixture used a non-canonical key. Friday sign-off: 10-second glance at one real work lockfile to triple-confirm; no tracemap fix expected. |
| Advice 4 (edit-site not evidenced note) | **QUEUED — SPEC-008 candidate** | Honest-wording improvement on the unit line; pairs with advice 2. |
| Advice 5 (trailer once per repo) | **QUEUED — SPEC-008 candidate** | Golden churn (F9 moves); design choice: repo-level note vs referenced single finding. |

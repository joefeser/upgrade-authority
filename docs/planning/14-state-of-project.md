# State of the project — 2026-10-03

**For the next context: read this + the memory files. The repo is the source of truth.**

## Where we are

V0 is complete and merged to `dev`. Seven PRs through the full three-round review machinery. The tool can:

1. **Read real tracemap scan output** (`ua ingest`) and convert it to planner input format
2. **Build an impact plan** (`ua plan`) from evidence — who's affected, in what order, with uncertainty visible
3. **Render a human-readable report** (`ua report`) with waves, repos, evidence, and gaps
4. **Run the full corpus** (`ua selftest`) — 16 fixtures, 33 test cases, all byte-exact

## What's built (in order)

| PR | What | Status |
|---|---|---|
| #1 | SPEC-000 (V0 definition) + SPEC-001 (evidence model) | Merged |
| #2 | 16-fixture golden corpus + plan.schema.v1 + validator | Merged |
| #3 | SPEC-002 (fusion) + SPEC-003 (wave ordering) + determinism contract | Merged |
| #4 | Planner (.NET 10, zero-dependency) — reproduces all goldens byte-exact | Merged |
| #5 | Markdown report renderer + report goldens | Merged |
| #6 | Tracemap ingest bridge — reads real scan output | Merged |
| #7 | SPEC-007 (multi-TFM lockfile rows + findings) — rounds 1-2 converged | Merged |
| #8 | SPEC-007 implementation — v1 rows, findings, F9 (real svc scan) / F10 / F11, 42-case selftest | Merged |
| #9 | Kiro post-merge hardening — case-insensitive package-id duplicate key + exit-4 hint; 44-case selftest | Merged |
| #10 | Two-repo estate scans (billing+shipping) + SPEC-008 (multi-repo lockfile ingest) — rounds 1-2 | Merged |
| #11 | SPEC-008 implementation — lockfile-rows.v2, exit 2 retired, F12, 50-case selftest — rounds 1-2 | Merged |
| #12 | Rescan all fixture repos with tracemap 0.2.0 (CPM pins + VersionOverride evidence) — zero review findings | Merged |
| #13 | SPEC-009 (ua apply v1 — dry-run edits) — rounds 1-3 | Merged |
| #14 | SPEC-009 implementation — ua apply, F13, 58-case selftest — rounds 1-3 (16 findings fixed) | Merged |
| #15 | SPEC-010 (ua push — apply execution) — rounds 1-2 | Merged |
| #16 | SPEC-010 implementation — ua push, F14/F15 gated fixtures, 71-case selftest — rounds 1-2 (17 findings + correction commit) | Merged |

## The known limitation (next priority)

**RESOLVED 2026-10-03 (PRs #7+#8):** SPEC-007 shipped — multi-TFM lockfile support. The planner accepts a repo whose lockfiles resolve the same package to different versions (`lockfile-rows.v1`, keyed by repo+lockfile+TFM+package); disagreements surface as `uncertainty.findings` (T10/T10a), never errors. The exit-4 refusal is narrowed to genuine same-resolution-group conflicts. Verified end-to-end on Kiro's real committed svc scan (`ua ingest testdata-ingest/tracemap-rich/scans/svc …` → plan → report), and that path is a permanent selftest case (42 cases total; 19 fixtures; zero golden churn).

**Multi-repo lockfiles: SHIPPED 2026-10-03 (PRs #10+#11, SPEC-008).** `ua ingest` accepts any number of lockfile-bearing repos (`lockfile-rows.v2`, repos[] envelope); exit 2 retired; findings stay per-repo internal (cross-repo differences are the waves' job); F12 built from the real billing+shipping scans. **Windows isolated VM acceptance re-run passed 2026-10-03: 50/50 cases byte-identical** (offline, .NET SDK 10.0.302).

**Kiro post-merge verification (2026-10-03, dev @ 8054d11): all four items VERIFIED against the real scan** (incl. a via-scoping probe: stripping Worker's dependencyNames nulls only Worker's via — per-lockfile parenting confirmed). Logged verbatim in `docs/reviews/pr8/kiro-postmerge.md` with dispositions. Two hardening items shipped (PR #9); advice 1/2/4/5 queued in SPEC-QUEUE as next-spec candidates.

**net48 systemic-risk concern RESOLVED (not a real-estate bug):** real NuGet lock files key classic-framework groups short-form (`net48`/`net472`), which tracemap's TFM pattern accepts — verified by running real tracemap on a scratch svc with a `net48` group (4 rows, zero unsupported-group gaps). Only the long form `.NETFramework,Version=v4.8` (which NuGet doesn't write into packages.lock.json) is rejected; Kiro's fixture had used it. Friday sign-off: 10-second glance at one real work lockfile to triple-confirm.

## tracemap 0.2.0 (PR joefeser/tracemap#804, merged 2026-10-03)

Central package management is now indexed: `CentralPackageVersionDeclared` facts (PackageVersion pins, own file+line evidence) + `versionOverride` on PackageReferenced facts; effective-version resolution stays consumer-side. Project-file extractor bumped to 0.2.0; tracemap suite 3235/0. **SPEC-009 (`ua apply`) is unblocked** — edit-site evidence exists; the svc scan shows Contoso.Core 1.0.0 pinned in Directory.Packages.props L2 and Worker's VersionOverride 0.9.0 on its reference fact. Also fixed in passing: semicolon-grouped PackageVersion identities; child-element value spans cover both endpoints.

## Phase 2 started: `ua apply` v1 SHIPPED (2026-10-03, PRs #13+#14)

`ua apply <fixture> [--repo <checkout>] [--out <dir>]`: evidence-gated edit sites (E1 direct pin / E2 CPM central pin / E3 VersionOverride / E4 packages.config), upgrade-only policy (never downgrades), producer/transitive units get honest notes, waves carry their gates, `--repo` verifies every site (package+attribute+version in ONE element, complete span) before emitting git-apply-compatible per-wave patches; stale/ambiguous evidence ⇒ typed exit 6; output is atomic. F13 demonstrates end-to-end on the real svc scan (2 edits incl. the override). Selftest 58 cases.

**Process note (transparent):** one post-merge fix (element spans as ranges, restoring the reviewed ambiguity refusal our own selftest caught) landed directly on dev with the explanation on PR #14 — the merge had raced the failing selftest in the same compound command.

**SPEC-010 ua push SHIPPED (2026-10-03, PRs #15+#16):** branch-per-wave from --base, verification INSIDE a worktree of the base tree, gated commit bodies (wave-status/condition/blocked-on), only-the-patch's-files staging (hooks can't smuggle), rollback strictly pre-commit (committed latch), idempotency-by-refusal, --pr = push + GitHub PR (GITHUB_TOKEN; never merges), push.v1 report (deterministic waves[] goldens + observed[]). Selftest 71 cases; F14/F15 joined the corpus.

**Awaiting Joe (one item):** the one-time real-API validation (SPEC-010 acceptance 6) — needs GITHUB_TOKEN + a throwaway repo; runbook in docs/reviews/pr16/FIDELITY-NOTES.md.

**SPEC-011 --scans-root SHIPPED (PRs #18+#19):** one command ingests a scans tree (depth-1 discovery, name-ordered, broken children warn-not-block); sidecar auto-discovery is single-dir-only (multi-dir runs require explicit sidecars — estate metadata is never a first-match guess); path dedup canonicalizes on-disk casing. Selftest 77 cases. **Windows VM re-run PASSED (offline, 71/71 byte-exact incl. apply+push; PR #17)** — Wednesday sign-off pre-cleared.

**The tool now runs a real estate end-to-end:** `tracemap scan` × N → `ua ingest --scans-root …` → `ua plan` → `ua report` → `ua apply --repo` → `ua push --pr`.

**SPEC-012 findings UX SHIPPED (PRs #20+#21):** delta-package findings mark the from-version; T10a trailer once per repo (subject-first, validator-enforced incl. placement); rule-(c) repos surface coverage gaps (G2). Goldens adjudicated line-by-line pre-capture (docs/reviews/pr21/GOLDEN-CHANGES.md); gaps-sort locale→ordinal fixed (PR#11 known issue closed). Selftest 78 cases.

**SPEC-013 GHE override SHIPPED (PRs #22+#23):** `--github-api`/`GITHUB_API_URL` for `ua push --pr`; credential-safe (non-loopback http refused, userinfo refused — never echoed; URI well-formedness pre-mutation); ssh:// and scp-style origins parse; real-path loopback stub proves the endpoint (offline VM safe). **Windows VM re-run PASSED 82/82 offline.** **Sign-off runbook: `docs/planning/15-signoff-runbook.md` (PR #24).**

**svc-net48 evidence SHIPPED (PR #25):** the net48 short-form outcome made permanent — a real scan whose classic-framework group parses (7 lockfile rows, zero unsupported gaps, `[delta from-version]` on both 12.0.3 resolutions); discovery never silently combines alternate snapshots of one repo (deliberate = explicit dirs). Selftest 83 cases.

**SPEC-014 ua ownership SHIPPED (PRs #26+#27):** the last manual estate blocker is gone — `ua ownership init --scans-root … --self <team> --all-self --out ownership.v0.json` generates the file from the scan set (ingest's shared identity algorithm; alternate snapshots collapse); `update` extends it with manual assignments sacred, mirrors + unknown fields passed through verbatim, absent-but-assigned kept + warned. Teams never invented. Reviews drove 16 fixes incl. planner-exact duplicate semantics (LAST wins), typed errors for uningestable scans, and the CLI flag hardening. Selftest 88.

**The real-estate path is now fully tooled:** `tracemap scan` ×N → `ua ownership init` → `ua ingest --scans-root` → `ua plan` → `ua report` → `ua apply` → `ua push`. Nothing between a scan folder and a scheduled plan requires hand-writing files.

**Windows VM acceptance round 3 PASSED (2026-10-03, offline, 88/88 byte-exact — ownership + GHE + apply/push battery included).** Wednesday sign-off pre-cleared on the exact current build.

**B1(b) SHIPPED END-TO-END (tracemap#818 + PR #29):** tracemap indexes `PackageProduced` (packable projects: PackageId with SDK-only AssemblyName fallback, PackageVersion-outranks-Version per NuGet pack semantics, last-wins MSBuild properties, identity..version evidence spans; extractor 0.3.0); `ua ingest` merges scan-discovered producers into producer-evidence.v0 — sidecar entries preserved verbatim, publicationStatus never invented from a declaration, and multi-dir runs are satisfied by scan facts (no hand-written producer sidecar needed). Real evidence: `scans/corelib`. Selftest 89 (E2E: producer wave 1 → consumer wave 2, no sidecar).

**SPEC-015 gap scope SHIPPED (PRs #30+#31; Joe approved the rule 2026-10-04):** only package-relevant problems are coverage gaps; compile/build health rides `scanCoverage.notes` → `PlanRepo.scanNotes` → report `### Scan notes` (visible, honest, never blocking). **billing/shipping reach `complete`** — `not-affected` is reachable on real estates. One structural classifier (unknown defaults Gap; every committed diagnostic shape covered; every manifest of combined snapshots classified; cycle plans carry notes). Goldens F9/F12/F13 adjudicated pre-capture (`docs/reviews/pr31/GOLDEN-CHANGES.md`; F12's G2 gaps intentionally gone). Selftest 94; **Windows VM 94/94 offline**.

**Public-facing docs SHIPPED (PR #34):** README (front door: why/pipeline/quick-start/commands/honesty-model/license-TBD), docs/FEATURES.md (complete tour), docs/COMPARISONS.md (fair matrix + narratives vs Dependabot/Renovate/Sourcegraph Batch Changes/Maestro-Darc; "when NOT to use us"). Repo description + topics set (private repo). Open-source-safe: sanitization grep-verified (no employer/estate/host specifics); NO license file (Joe's open decision). Two rounds of accuracy review (9 findings) — every claim now matches shipped behavior (per-checkout push, edit-bearing waves, hooks honesty, arcade-services source-availability, air-gapped executors credit).

**SPEC-016 --sanitized SHIPPED (PRs #36+#37; Joe-requested for the Wednesday validation run):** any command's console output can be pasted off a work machine — paths (incl. spaces) → `[path:leaf#hash]`, ssh/http(s) URLs → `[url#…]` (allowlist grants scheme+host+path ONLY; userinfo/query forces redaction), emails, bare hosts in host-bearing contexts (incl. single-label GHE names, IPs, host:port), unquoted repo keys, and `UA_REDACT` operator-listed proprietary names. Canonical stdout (plan/report/apply manifest) bypasses — verified identical. 8 adversarial-review findings fixed (incl. space-paths, uppercase schemes, prose-swallowing, over-broad host contexts). Selftest 96; **Windows VM 96/96 offline**.

**Queue:** real-estate dry run (needs Joe's machine — runbook §4 + §4b if anything fails: same command + `--sanitized`, paste freely); license decision (Joe); **next-major design talk (Joe, ~1 week horizon): DB backend, action queue, CVE flagging, multi-user + roles, NuGet-feed test harness (sample-repos + local feed), schema publishing (HACP=D008; WITS — Joe to define).**

## After that: Phase 2 (PR generation)

`ua apply` generates version-bump edits per wave, creates branches + PRs, human merges.

## Key files

- Specs: `docs/specs/SPEC-000` through `SPEC-005`
- Fixtures: `fixtures/F*` (16 goldens with plan.json + report.md)
- Real tracemap output: `testdata-ingest/tracemap/` (committed scans) + `testdata-ingest/tracemap-rich/` (Kiro's richer scans)
- Review history: `docs/reviews/pr*/` (all rounds, all findings, all dispositions)
- Planning log: `docs/planning/` (research, decisions, fidelity records)
- Tools: `tools/canonicalize-goldens.mjs` (normative serializer), `tools/validate-fixtures.mjs` (structural validator), `tools/templates.mjs` (shared template set)
- Source: `src/UpgradeAuthority/` (Program.cs, Planner.cs, Report.cs, Ingest.cs, Canonical.cs, Models.cs)

## Process

- **Review doctrine:** `docs/specs/REVIEW-DOCTRINE.md` — 2 rounds to converge, 3 to escalate; zero OPEN blockers/majors = done
- **Quorum:** Codex + Qodo (Qodo reviews once per PR); Kiro additive; coordinator patches once after ALL quorum reviews
- **Fidelity rule:** golden changes recorded with reasons in `docs/reviews/*/GOLDEN-CHANGES.md`
- **Lane config:** `.agent-control/lanes/spec-review-loop.yaml` (ACK = agent-control-kit @ ~/src/joefeser/agent-control-kit)

## Open questions for Joe

1. **Gap scope:** should compiler diagnostics make package coverage `gaps`, or only package-relevant gaps (lockfile failures, unsupported manifests)? Today a broken C# file makes the whole repo `gaps`.
2. ~~**IP clearance:** gates anything public~~ **complete** (details in the private archive)
3. **License choice:** Apache-2.0 core + proprietary, or FSL-1.1-MIT

## Stack

.NET 10 console tool (`ua`), zero NuGet packages, offline, Windows-local. Runs on macOS + Windows (verified on an isolated, network-disabled Windows VM).

# Session 12 — Coordinator synthesis: reconciling the ZCode and ChatGPT research streams

**Date:** 2026-10-02
**Author:** ZCode (coordinator)
**Inputs:** ZCode-commissioned research ([04](04-research-landscape.md), [05](05-research-licensing.md)) · ChatGPT research ([07](07-market-research-chatgpt.md), [08](08-dotnet-research-chatgpt.md), [09](09-governance-research-chatgpt.md), [10](10-license-research-chatgpt.md)) · ChatGPT planning record with Joe's steering decisions D001–D020 ([11](11-planning-log-chatgpt.md)) · fresh verification of Joe's public repos (below).
**Status:** Synthesis for review. Items marked **[NEED CONFIRMATION]** await Joe.

## 1. Fresh verifications done by coordinator (2026-10-02)

- **joefeser/tracemap — confirmed public, Apache-2.0, very active** (2,812 commits). It is a deterministic, evidence-based repository indexer (no LLMs) with existing `package-impact` (matches a `package-delta.v1` file against indexed package-reference evidence, reporting sources/SHAs/rules/gaps) and `package-decision` (correlates producer-authored package admission/revocation decisions, exact vs digest-mismatch vs ambiguous) commands. Explicit exclusions: no transitive resolution, no compatibility inference, no build/restore execution, no deployment/release approval. **Implication:** the static-evidence layer of this vision already exists as Joe's OSS project; upgrade-authority's natural role is the layer tracemap deliberately excludes — resolution, sequencing, campaigns, publication gating, evidence-driven execution.
- **joefeser/what-is-the-spec — HTTP 404 at the public URL.** WITS's identity as "what-is-the-spec" is confirmed by Joe (D011), but the repo is private, renamed, or under a different path. **[NEED CONFIRMATION: correct repo pointer before any capability evaluation.]**

## 2. Where the two research streams converge (high confidence)

1. **Sourcegraph Agentic Batch Changes (GA 2026-09-14) is the closest shipped competitor** for cross-repo upgrade campaigns — but with no dependency graph, no build-order waves, no private-feed awareness, .NET unproven (04), and staged rollout whose publication/release-unit awareness is unverified (07).
2. **Don't build a scanner; build the coordination/orchestration layer** that ingests findings from tools customers already pay for.
3. **The single highest-leverage open architecture fact is whether Artifactory Build Info exists and is trustworthy** for the private packages. Both streams independently put this at the top of the question list.
4. **"One team free / pay across the org" must be a product/feature boundary, not a license term.** OSI-open licenses cannot discriminate by org size or field of endeavor (10); FSL-style source-available licenses block only *competing resale*, not internal corporate use (05). Either way, the paywall lives in features/hosting/services.
5. **Leading license paths:** Apache-2.0 (or MIT) core + proprietary enterprise components; FSL-1.1-MIT is the strongest alternative if source-available is acceptable. AGPL has real enterprise friction (both streams), SSPL/BSL/PolyForm-NC rejected (05, 10).
6. ~~**The IP/employment gate is unresolved and gates anything public.**~~ **Cleared** (details in the private archive).
7. **Positioning discipline:** do NOT claim "first cross-repo dependency-upgrade platform" — contradicted by Microsoft's internal systems and Sourcegraph (07 §"What this changes about planning").

## 3. ChatGPT found; my stream missed (adopted into the record)

- **Microsoft Product Construction Service (Maestro) / Darc / Build Asset Registry** (dotnet/arcade-services) — the closest *conceptual* prior art: org-wide .NET dependency flow with explicit provenance (source repo + SHA per dependency), channels/subscriptions, coherent-parent constraints, merge policies, pause/resume. Microsoft runs the four-pillar combination internally for its own estate. **This refines my 04 white-space verdict:** the pillars are combined *inside Microsoft*, but not as a deployable product for private non-Arcade estates. Adds a real "adapt vs build" option (D006) and a reference architecture to learn from (Version.Details.xml-style provenance, channel semantics).
- **Dependabot org/enterprise alert APIs** — org-wide *visibility* exists; the missing piece is org-wide *sequenced remediation*. My 04 said this too, but 07's nuance is sharper: "Dependabot only sees one repo" overstates the limitation.
- **Sonatype Lifecycle** remediation REST API + waiver workflow — a governance/remediation competitor my 04 table lacked.
- **MSBuild 17.8+ `EvaluateItems`/`EvaluateProperties`** — evaluated package identity WITHOUT executing build targets. This is a *mechanism* for producer discovery that beats Joe's current YAML-sniffing heuristic and should be in the v0 discovery design space.
- **NuGet mechanics detail** (08): CPM transitive pinning can't downgrade and may surface pins as explicit deps in packed nuspecs; library lock files don't bind consumers; `dotnet nuget why` for path explanation; packages.config not extracted by Renovate.
- **Corrections:** .NET Patch Tuesday is the *second* Tuesday, not "around the 11th"; Joe's "CVS" almost certainly means CVE or CVSS — confirm before it enters any spec.

## 3a. Maestro/Darc adapt-vs-build assessment (added 2026-10-02, resolves D006)

**Question from Joe:** "with (dotnet/arcade-services) are we wasting our time building our own, and what is the license?"

**License: MIT** (verified from the repo). Code and patterns are legally free to reuse with attribution.

**Verdict: build our own — borrowing Maestro's patterns. Not a waste.** Decisive reasons:

1. **It is an internal engineering system, not a product.** "This repo is home of the services that help us construct .NET." No published self-host/deployment guide exists; the services are tied to Microsoft's internal infrastructure (internal Azure DevOps, dnceng build queues, BAR, VMR pipeline). Adopting it means solo-standing and maintaining a distributed Microsoft-internal service — incompatible with D017 (local MVP on Joe's work machine, 4–8 phases).
2. **Its provenance model requires producer-side Arcade conventions** (Version.Details.xml, eng/ structure, Arcade SDK builds generate the per-dependency source+SHA records Maestro consumes). Joe's estate includes outside-team repos whose conventions he cannot mandate (Session 03 A4) and legacy packages.config shapes (D005). Without conventions on every producer, Maestro has nothing to ingest — the same org-boundary constraint that kills Dependabot-style approaches kills Arcade adoption.
3. **Stack mismatch:** Maestro is built around Azure DevOps + GitHub merge flows + BAR; the estate is GitHub + Harness + Artifactory. The integration surface is rewrite-level.

**What we take from it (the real value):**
- **Validation:** Microsoft runs the four-pillar combination at scale internally — the problem class is real and hard enough that Microsoft built a dedicated system.
- **Reference architecture:** channels/subscriptions, coherent-parent constraints, dependency flow graph, per-dependency provenance records, merge-policy checks, pause/resume campaign semantics — vocabulary and patterns for our campaign model. `DependenciesFlowPlan.md` (dotnet/arcade) is a must-read design doc; the `mstro` CLI (dotnet/arcade-skills) shows the public API shape.
- **MIT-borrowable code** where patterns need heavy lifting.
- **Differentiation sharpens:** our system must work WITHOUT producer-side conventions (evidence-based discovery), across heterogeneous CI/feeds, including legacy formats — precisely what Maestro assumes away.

**Residual risk:** if Microsoft ever productizes PCS, it becomes the strongest possible competitor — but Maestro has existed for years without externalization; Microsoft's external .NET-upgrade energy has gone to Copilot App Modernization (single-repo) instead. Watch, don't fear.

**D006 resolution: build new, borrow patterns (MIT), do not adapt arcade-services.**

## 4. My stream found; ChatGPT missed (complementary, still stands)

- **Licensing depth:** FSL-1.1/Fair Source + Fair Core License options, fair.io movement, n8n precedent, contribution/dual-licensing mechanics, state-law IP backstops, concrete free/paid boundary patterns (SSO tax, seats, per-changeset). ChatGPT's 10 has no FSL coverage — the two license docs together are now the complete picture.
- **Pricing data:** Sourcegraph's outcome-based per-changeset pricing (the most relevant precedent — our unit of value is a merged upgrade wave), GHAS $49/committer, Snyk $25/dev/mo, Mend Renovate ~$250/dev/yr, Aikido flat fees. Directly serves D014 ("Free vs pay us. Show us the money").
- **Vendor negatives:** Endor Labs' own docs limit upgrade analysis to *project level, not across the tenant*; Harness SCS Auto PR is direct-deps-only with metadata gates and no CI evidence (and Harness is already in Joe's CI — highest convenience-collision risk); Microsoft deprecated Upgrade Assistant in favor of Copilot App Modernization (single-solution scope).
- **Agentic newcomers:** Moderne/Moddy (incl. `UpgradeDependencyVersion` recipe for `Directory.Packages.props`), Snyk Evo, Endor Agentic Workflows, Devin .NET migrations. Notably, ChatGPT's own verification backlog asked for exactly this coverage — 04 answers it.

## 5. New project facts absorbed from Session 11 (Joe's steering in the ChatGPT conversation)

These are Joe's own statements there; adopted as candidate canonical facts here.

| Fact | Source | Effect on this workspace |
|---|---|---|
| WITS = what-is-the-spec; necessity undecided | D011, 19:41 | Resolves open Q1a from [06]. Repo 404s — need pointer. |
| hacp.io = manual approved-loop evidence chain; evaluation candidate only, never the executor | Session 09 | Resolves open Q1b from [06]. Park under governance track (D008). |
| artifact-memory + who-decides = Joe's candidate projects for accountability/decision records | D012, D013 | Add to evaluation backlog; capability verification needed. |
| Substantial existing PowerShell implementation, local, many failures; MVP runs on Joe's work machine; 4–8 phases expected | D017 | Major constraint: no cloud control plane assumption; v0 must run locally on Windows. |
| **V0 must be defined and partially built week of 2026-10-05 for a status update** | D018 | **[NEED CONFIRMATION: does this milestone govern THIS repo/agent?]** If yes, v0 definition proposal is my next deliverable. |
| Sol regression incident: demonstrated test cases refused; 2 PRs broke working behavior; ~30 h recovery each | D019 | Adopted as working agreement (see §7). |
| Test direction: mock JFrog + the ugly cases | D020 | Test north star for v0+: contract-faithful feed/build-metadata simulator + real local NuGet/MSBuild fixtures. |
| Scope: modern AND legacy .NET in discovery | D005 | v0 discovery must tolerate packages.config/legacy shapes or explicitly mark them as gaps. |
| Effort preference: bounded core first; full vision preserved, v0 scope discussed separately | D015, D016 | Matches my sequencing; vision lives in the roadmap doc, not v0. |
| VB.NET projectless source-analysis pain is NOT this MVP | 20:01 entry | Keep tracemap-style static analysis distinct from campaign orchestration. |
| Commercial emphasis: "Free vs pay us. Show us the money" | D014 | Pricing boundary work is a first-class planning track, not an afterthought. |

## 6. Corrections to earlier sessions

- **04 (landscape):** verdict nuanced — "nobody combines the four pillars" must read "nobody *ships* the four-pillar combination as a product; Microsoft operates it internally (Maestro/Darc) for its own estate." Big-vendor risk ranking unchanged (GitHub #1).
- **06 (coordinator notes):** open questions 1 (wits/hacp.io) resolved; question 2 (Build Info) reinforced as top question (both streams); new questions added below.
- **02 (brain dump extraction):** "around the 11th" → second Tuesday (Patch Tuesday); "CVS" → CVE/CVSS pending confirmation.

## 7. Working agreements adopted (coordinator, per D016/D019/D020)

1. **Acceptance-fidelity rule:** every demonstrated case becomes an explicit input + expected-behavior + repeatable acceptance check. No substitution with convenient fixtures; any substitution or weakening is recorded with a reason and reviewed. (Directly answers the Sol incident.)
2. **Full vision is preserved** in the planning log; v0 scope is discussed separately and never silently trims the vision.
3. **Mock-JFrog scenario inventory** (Session 11, "Mock private infrastructure") is the test-design north star for v0+.
4. All agent inputs are logged verbatim before synthesis (already standing).

## 8. Updated open questions for Joe

Carryovers still open from [06]: Build Info (Q2), graph bootstrap (Q3), wave unit (Q4), GitHub vs ADO (Q5), evidence plumbing (Q6), two-track (Q7), IP status (Q8 — now also D009/10 §employment), naming (Q10). New:

- **N1 — ANSWERED 2026-10-02: GO.** Joe clarified the ChatGPT deadline's intent: he is "not one prompting this" — he wants working validation of some functionality in **less than a week**, not a big-bang build he only touches at the end. The milestone governs this repo as a *validation loop*: small built slices, touched early, wrongness discovered fast. I proceed to spec → review → build now.
- **N2 — partially answered 2026-10-02:** `what-is-the-spec` exists locally at `~/src/joefeser/what-is-the-spec` (plus `what-is-the-spec-validation`); the public URL 404s because it is local/private. Also found locally: `agent-control-kit` (ACK @ 0.5.5 — the lane system behind tracemap's review loops, now also enforcing our spec-review doctrine), `hacp`, `hacp.io`. Capability evaluation of WITS/artifact-memory/who-decides still pending.
- **N3 — ANSWERED 2026-10-02, Joe approved CONSUME.** upgrade-authority consumes tracemap's evidence outputs (`package-delta.v1`, indexed package evidence) rather than re-implementing static evidence. Version-pin the formats consumed (see R9).
- **N4:** Is the existing PowerShell implementation something I can see (sanitized), or do we treat it as behavior-reference only? Affects reuse vs rewrite for the local runner.
- **N5:** Confirm "CVS" meant CVE/CVSS.

## 9. Updated risk register

| # | Risk | Severity | Change |
|---|---|---|---|
| R1 | Employer IP claim | High | Unchanged; reinforced by Session 10 §employment. Gate for anything public. |
| R2 | Big-vendor collision | High | Refined: Microsoft already runs the four pillars internally (Maestro); the race is productization for private estates. |
| R5 | Graph inference fragility | Medium | **Reduced** — three discovery mechanisms now identified: Artifactory Build Info (if present), MSBuild 17.8 evaluated properties, tracemap's existing indexed evidence. YAML-sniffing is demoted to last resort. |
| R7 (new) | V0 schedule pressure (week of Oct 5) vs planning rigor | Medium | Mitigate with narrow v0 slice (impact plan only) + acceptance-fidelity rule; no scope creep into execution. |
| R8 (new) | Agent-induced regressions (Sol incident, ~60 h lost) | Medium | Mitigated by acceptance-fidelity rule (§7.1); applies to me, Codex, Kiro, and any future coding agent. |
| R9 (new) | Dependency on tracemap's roadmap/ formats (if N3 = consume) | Low-Medium | tracemap is Joe's Apache-2.0 project; version-pin the evidence formats consumed. |

## 10. Proposed next steps (pending N1)

1. Joe confirms N1 (V0 milestone governs here) and N3 (tracemap relationship).
2. I draft **v0-definition proposal** — scope: local Windows CLI that builds a trustworthy impact plan (producer map + affected repos + update order) from sanitized example inputs, with uncertainty preserved and explicit gaps; acceptance cases from the demonstrated set; mock-JFrog scenarios deferred to the execution milestone.
3. Codex/Kiro review the proposal (first use of the reviewer bench) via review brief.
4. On approval: build the v0 slice on `dev` during the week of Oct 5, with the acceptance-fidelity rule enforced from the first commit.
5. Parallel (Joe-only, no agent needed): IP disclosure conversation (R1 — complete, archived); correct WITS repo pointer (N2).

## 11. Go decision & build process (added 2026-10-02)

**GO.** Joe: "I think we are ready to go." Decisions adopted:

1. **Spec-first process (tracemap-style),** per Joe's open question and his answer-in-intent: write specs → PR → "have the agents fight it out" (Codex/Kiro review via Joe-ferried briefs) → merge → build. Guardrails: specs must be reviewable faster than the code they precede; every spec embeds demonstrated-case acceptance checks (fidelity rule, §7.1).
2. **Validation loop over big-bang:** something runnable and touchable within days; v0 proves the impact-plan wedge on sanitized examples; wrongness surfaces early. Not "build everything before I touch it."
3. **4-specs-ahead pipeline** (Joe's model): a spec-writing worker stays ~4 specs ahead of the main builder so the builder never starves. While solo, the coordinator maintains the queue; split into separate workers when volume justifies it.
4. **HACP note (Joe, 2026-10-02):** the evidence contract (tracemap `package-delta.v1` consumption and/or our own graph/evidence schema) "may also want to be published in hacp." Recorded as an open consideration in SPEC-001 and on the governance track (D008); does not block v0.
5. **Branching:** specs land via `spec/*` branches off `dev`, merged only after reviewer verdicts. Repo stays **local-only** — no push to origin until Joe confirms the remote is private and IP posture allows it (R1).
6. Still open: N2 (WITS repo pointer), N4 (PowerShell sanitized access), N5 (CVE/CVSS) — none block v0 spec review.
7. **Macroscope check (2026-10-02, per Joe): INACTIVE — "It got too expensive."** The `Macroscope - Correctness Check` will keep showing SKIPPED on PRs; it is not a gate, ignore it. Do not build workflows to satisfy it.

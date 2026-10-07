# upgrade-authority — planning log

**Status: PLANNING MODE.** No implementation decisions are final. This directory is the durable record of inputs, research, and coordinator notes.

## Ground rules (per Joe, 2026-10-02)

- This is a BIG problem: stay in planning mode. Do not jump to solutions.
- **Team structure (per Joe, 2026-10-02):** Joe = owner/spec authority. **ZCode = coordinator, in charge** — drives decisions and synthesis. **Sol** = peer agent contributing analysis (first input pending). **Codex and Kiro = reviewers**, engaged by the coordinator as needed.
- **Reviewer workflow:** ZCode cannot invoke Codex/Kiro headlessly (no codex binary on PATH; Kiro's CLI only opens the IDE; no MCP server configured in this workspace as of 2026-10-02 — Joe mentions a Codex MCP bridge may exist but untested). **Primary channel today: copy-paste.** When a review is wanted, ZCode writes a focused review brief (file + questions); Joe runs it through the reviewer; the response is pasted back and logged here verbatim as a numbered session, same as Sol's inputs.
- Inputs arrive from multiple agents. Everything gets logged here verbatim before being discussed or synthesized.
- The spec authority for domain pain is Joe's brain dump (Session 02).
- **Data separation rule:** proprietary employer/estate information (real repo names, package names, org dependency data, alert exports) must never be committed to this repository. Logs use anonymized shapes ("repo x publishes packages a b c") only.

## Log index

| Date | Doc | What it is |
|---|---|---|
| 2026-10-02 | [01-handoff-glm-agent.md](01-handoff-glm-agent.md) | Strategic planning context handed off from a prior GLM agent (verbatim) |
| 2026-10-02 | [02-vision-brain-dump.md](02-vision-brain-dump.md) | Joe's vision / domain brain dump (verbatim) |
| 2026-10-02 | [03-sol-qa.md](03-sol-qa.md) | Sol's initial questions and Joe's answers (verbatim) |
| 2026-10-02 | [04-research-landscape.md](04-research-landscape.md) | Competitive landscape research (commissioned by coordinator) |
| 2026-10-02 | [05-research-licensing.md](05-research-licensing.md) | Licensing & IP research (commissioned by coordinator) |
| 2026-10-02 | [06-coordinator-notes.md](06-coordinator-notes.md) | Coordinator opinions, open questions, pending items |
| 2026-10-02 | [07-market-research-chatgpt.md](07-market-research-chatgpt.md) | ChatGPT research: market & prior art incl. Microsoft Maestro/Darc (verbatim) |
| 2026-10-02 | [08-dotnet-research-chatgpt.md](08-dotnet-research-chatgpt.md) | ChatGPT research: .NET evidence, identities, sequencing (verbatim) |
| 2026-10-02 | [09-governance-research-chatgpt.md](09-governance-research-chatgpt.md) | ChatGPT research: governance, campaigns, HACP/WITS (verbatim) |
| 2026-10-02 | [10-license-research-chatgpt.md](10-license-research-chatgpt.md) | ChatGPT research: licensing & commercial boundary (verbatim) |
| 2026-10-02 | [11-planning-log-chatgpt.md](11-planning-log-chatgpt.md) | ChatGPT planning record incl. Joe's decisions D001–D020 (verbatim) |
| 2026-10-02 | [12-coordinator-synthesis.md](12-coordinator-synthesis.md) | **Synthesis: both research streams reconciled; new facts; updated questions/risks; proposed next steps** |
| 2026-10-02 | [13-upstream-feed-tracemap.md](13-upstream-feed-tracemap.md) | Upstream feed: tracemap PR #800 (no SPEC impact; feed policy adopted) |

## Pending

- [ ] **Joe confirms (Session 12 §8):** N1 — does the week-of-2026-10-05 V0 milestone govern this repo? N3 — consume tracemap evidence vs re-implement? Plus N2 (WITS repo pointer), N4 (PowerShell reuse), N5 (CVE/CVSS).
- [ ] Sol's analysis/opinion on the plan — **not yet received.** Will be logged verbatim as Session 13, then synthesized.
- [ ] License decision (blocked on research review + Joe's call + IP clearance).
- [x] IP disclosure/approval — complete (details in the private archive).

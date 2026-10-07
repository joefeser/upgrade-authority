# Comparisons — upgrade-authority and the existing tooling landscape

Fair and factual. Every tool below is good at what it's for; the differences are about *scope* — what unit of work each treats as primary. Also see [FEATURES.md](FEATURES.md) for the full capability tour.

## The one-paragraph version

Existing dependency tooling is either **per-repo** (Dependabot, Renovate — excellent at "bump this repo's manifest", with no cross-repo ordering or estate view) or **batch-execution** (Sourcegraph Batch Changes — run a script over many repos, with no dependency semantics and no opinion about order). `upgrade-authority` is **graph-first**: it models which repos produce and consume which packages, orders upgrades into evidence-backed waves, verifies the exact edits, and opens PRs whose descriptions carry the plan. It trades breadth of ecosystem (NuGet-first today) for depth of correctness on one ecosystem.

## Feature matrix

| Capability | upgrade-authority | Dependabot (native/GitHub) | Renovate | Sourcegraph Batch Changes (incl. Agentic, 2026) | Maestro/Darc (Microsoft, internal) |
|---|---|---|---|---|---|
| Primary unit of work | **The estate** (producer→consumer graph) | One repo | One repo (many registered) | Many repos, one script or agent prompt | .NET-wide dependency flow |
| Cross-repo ordering (producers before consumers) | **Waves, built-in** | — | — (PRs arrive unordered) | — (script-defined at best) | ✔ (queue/coordinator model) |
| Impact plan before any change | **Yes — the core artifact** | — (updates list) | Dashboard (per-repo) | — | Partial (dependency flow graph) |
| Who's affected & why (reasons, evidence) | **Per repo, evidence-cited** | — | Per-repo updates | — | — |
| "Definitely not affected" proof | **Positive evidence required** (lockfile closure + complete coverage) | n/a | n/a | n/a | n/a |
| Exact edits generated & verified against the checkout | **Yes (selector-verified patches)** | Yes (unverified-in-this-sense branch commits) | Yes | Script's job | Yes (version props) |
| Version conflicts inside one repo surfaced | **Findings with per-(lockfile, TFM) resolutions** | — (opens conflicting PRs) | — (pin per repo) | Script's job | — |
| CPM (`Directory.Packages.props` + `VersionOverride`) as first-class edit sites | **Yes, with fan-out and override semantics** | Partial | Yes | Script's job | Yes |
| PR bodies carry the plan (gates, prerequisites, findings) | **Yes** | Release notes | Config-derived | Script-defined | — |
| Ever merges on its own | **Never** | Configurable (auto-merge) | Configurable (automerge) | Script-defined | Coordination model |
| Works fully offline / air-gapped | **Yes (single binary; one optional network flag)** | No (service) | Self-hosted option | Self-hostable incl. air-gapped executors (infrastructure required) | Internal service |
| Evidence traceability (every claim → an input you control) | **Design invariant** | — | — | — | — |
| Ecosystems | **NuGet today** (design is ecosystem-shaped) | Many | Many | Language-agnostic | .NET |
| Maturity | V0, single-user CLI | Very high | Very high | GA 2026-09 | Internal, years in production |

## The tools, fairly

### Dependabot (GitHub native)

The default answer for public repos and simple estates, and very good at it: knows dozens of ecosystems, opens clean per-repo PRs, keeps them updated. What it doesn't attempt: any notion that repo B consumes a package repo A produces, ordering across repos, or proving a repo is safe to skip. Each PR is an island; a wide upgrade is a flood of islands. Auto-merge (when enabled) trades coordination for convenience — `upgrade-authority` deliberately never merges.

### Renovate

The power user's per-repo bot: configurable to a fault, groupable updates, hosted or self-hosted, automerge policies. Its dashboard aggregates *registered repos* but the mental model is still N independent repos — no producer/consumer graph, no waves, no cross-repo "who must move first". If your estate is many unrelated repos, Renovate is excellent; if it's a package graph with internal producers, ordering is on you.

### Sourcegraph Batch Changes

Closest in ambition ("run a change campaign across many repos"). The classic, script-driven engine has coordinated changes for years; **Agentic Batch Changes** (GA September 2026) adds planning and repository-specific handling on top. Both share one model: you define the change — as a script or an agent prompt — and it executes over search-matched repos, collecting PRs. It's a batch *executor*. It has no dependency semantics: no package graph, no affected-ness, no wave ordering, no edit verification against manifests; your script does all of that. Complementary more than competing — Batch Changes is the "any change" hammer; `upgrade-authority` is the dependency-specific instrument that knows what to change and in what order.

### Microsoft Maestro / Darc

Microsoft's internal system for exactly this problem at .NET scale (dependency flows across product repos via coordinators and channels). Publicly described in talks and posts; the associated [arcade-services](https://github.com/dotnet/arcade-services) source (Product Construction Service, formerly Maestro, plus the Darc CLI) is MIT-licensed on GitHub, though the hosted service itself is Microsoft-internal — not self-hostable for others. It validated the architecture — version-flow coordination with producer/consumer ordering — and `upgrade-authority` borrows the pattern (waves, stated-not-verified prerequisites) while differing in key ways: evidence-first planning (three-state classification with proof requirements), offline-first operation, and open single-binary distribution instead of an internal service platform.

### What about vulnerability scanning (Dependabot alerts, Snyk, etc.)?

Deliberately out of scope today. `upgrade-authority` doesn't ingest advisories or score risk — it answers "when we decide to move package X, what exactly happens, in what order, with what proof." Vulnerability intelligence is a planned input (flag packages with advisories, prioritize waves) — it will *feed* the planner, not replace it.

## When *not* to use upgrade-authority

- You want per-repo auto-merging of routine bumps — Dependabot/Renovate do that today; this tool won't (ever, by design).
- You need ecosystems beyond NuGet today — not built yet.
- You want a hosted service — this is a CLI you run where your code lives (which is the point: private estates, air-gapped networks, zero exfiltration).

## Design commitments that show up in every row above

1. **Evidence or silence** — no heuristics, no name-matching, no "probably fine". Absence of evidence is `unknown`.
2. **Stated, not verified live** — availability claims are never presented as checks the tool didn't perform.
3. **Humans merge** — the tool's job ends at verified, well-described PRs.
4. **Byte-deterministic** — same inputs, same bytes, any machine; the corpus proves it continuously.

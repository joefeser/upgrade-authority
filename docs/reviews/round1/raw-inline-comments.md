===== INLINE by qodo-code-review[bot] on docs/specs/SPEC-001-evidence-and-graph-model.md:15 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

1\. Producer maps lack producer evidence <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>SPEC-001</i></b> lists package-reference facts and a package delta as V0 inputs, while <b><i>SPEC-000</i></b> requires
those inputs to establish which repositories produce packages. When F1 or F2 is built from the
declared inputs, references can establish consumption but cannot substantiate the producer claims
needed for the map and wave order.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
The declared V0 inputs do not establish the producer identities required by F1 and F2.
## Fix Focus Areas
- docs/specs/SPEC-001-evidence-and-graph-model.md[12-17]
- docs/specs/SPEC-000-v0-definition.md[16-17]
- docs/specs/SPEC-000-v0-definition.md[31-40]
## Recommended Fix
Pin an actual producer-fact export or another explicitly sourced producer input before promising these outcomes. Add an acceptance check generated from the pinned input that distinguishes a candidate package declaration from verified production.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/f7237b69-ce2d-4d2d-82e5-3fe7a3793f64?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/f7237b69-ce2d-4d2d-82e5-3fe7a3793f64)

===== INLINE by qodo-code-review[bot] on docs/specs/SPEC-001-evidence-and-graph-model.md:57 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

2\. Plans cannot show real transitive paths <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>SPEC-001</i></b> requires F4 to represent distinct direct and transitive paths but supplies only static
package-reference facts and a package delta. Tracemap explicitly excludes transitive resolution, so
the stated inputs cannot establish F4&#x27;s resolved-version mismatch or service-to-package transitive
path.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
F4 requires resolved and transitive information absent from the specified tracemap inputs.
## Fix Focus Areas
- docs/specs/SPEC-001-evidence-and-graph-model.md[12-17]
- docs/specs/SPEC-001-evidence-and-graph-model.md[27-27]
- docs/specs/SPEC-001-evidence-and-graph-model.md[57-57]
- docs/specs/SPEC-000-v0-definition.md[34-34]
## Recommended Fix
Specify a pinned restored-assets or equivalent resolved-graph input, with its provenance and context, and test F4 against it. Otherwise make those paths unknown and move F4's resolved-path requirement out of V0.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/3e50b049-19d6-479d-a67a-62a296bcd3a9?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/3e50b049-19d6-479d-a67a-62a296bcd3a9)

===== INLINE by qodo-code-review[bot] on docs/specs/SPEC-001-evidence-and-graph-model.md:24 =====
<img src="https://img.shields.io/badge/Medium-634FD1?style=flat-square" height="20px" alt="Remediation recommended">

6\. Unknown versions cannot enter the graph <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>PackageIdentity</i></b> requires both an ID and a version, but V0&#x27;s primary producer rung is declared
<b><i>IsPackable</i></b> and package-ID evidence rather than an observed package artifact. When a repository
declares a package ID without an evidenced produced version, the model cannot retain its producer
claim without inventing a version or dropping the claim.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
Source-level producer declarations can identify a package without establishing a produced version.
## Fix Focus Areas
- docs/specs/SPEC-001-evidence-and-graph-model.md[23-27]
- docs/specs/SPEC-001-evidence-and-graph-model.md[36-42]
- docs/specs/SPEC-001-evidence-and-graph-model.md[54-56]
## Recommended Fix
Model an unversioned package ID separately from an observed versioned artifact, or allow an explicitly unknown produced version. Add a fixture with an evidenced package ID but no produced version.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/32b7d6e5-4123-4aa6-a51a-da09e42bdcf3?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/32b7d6e5-4123-4aa6-a51a-da09e42bdcf3)

===== INLINE by qodo-code-review[bot] on docs/specs/SPEC-001-evidence-and-graph-model.md:28 =====
<img src="https://img.shields.io/badge/Medium-634FD1?style=flat-square" height="20px" alt="Remediation recommended">

7\. One repo can receive the wrong wave <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>ReleaseUnit</i></b> groups every package from a repository by default, and F1 requires all eight outputs
to appear as one unit despite release coupling remaining unverified. If those packages publish in
subsets or with independent versions, labeling the assumption does not prevent the plan from
imposing a false shared prerequisite on their consumers.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
Shared compilation does not establish that eight packages publish as one release unit.
## Fix Focus Areas
- docs/specs/SPEC-001-evidence-and-graph-model.md[28-28]
- docs/specs/SPEC-001-evidence-and-graph-model.md[54-54]
- docs/specs/SPEC-000-v0-definition.md[19-19]
- docs/specs/SPEC-000-v0-definition.md[41-41]
## Recommended Fix
Require evidence or an explicit reviewed fixture assumption for a single release unit; otherwise represent the grouping and dependent order as conditional. Test a repository that builds packages together but publishes a subset.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/b4af0b15-b402-4019-9e6e-084621ed9326?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/b4af0b15-b402-4019-9e6e-084621ed9326)

===== INLINE by qodo-code-review[bot] on docs/specs/SPEC-001-evidence-and-graph-model.md:56 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

3\. Conflicted producers can get firm waves <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>SPEC-001</i></b> requires F3 to retain both conflicting producer claims, but it does not require the
impact plan to withhold a definitive producer-dependent order. When the two repositories disagree
over production, an implementation can pass the stated contradiction check while scheduling
consumers behind one unsupported choice.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
Retaining contradictory claims alone does not stop an unsupported producer from driving a definitive wave.
## Fix Focus Areas
- docs/specs/SPEC-001-evidence-and-graph-model.md[34-46]
- docs/specs/SPEC-001-evidence-and-graph-model.md[55-56]
- docs/specs/SPEC-000-v0-definition.md[33-33]
- docs/specs/SPEC-000-v0-definition.md[40-43]
## Recommended Fix
Define how unresolved producer conflicts affect inclusion and wave certainty. Add an F3 assertion that no single conflicted claim silently becomes an unconditional scheduling prerequisite.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/899965c5-6e62-4642-b4df-8216dfd38e79?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/899965c5-6e62-4642-b4df-8216dfd38e79)

===== INLINE by qodo-code-review[bot] on docs/specs/SPEC-000-v0-definition.md:40 =====
<img src="https://img.shields.io/badge/Medium-634FD1?style=flat-square" height="20px" alt="Remediation recommended">

8\. Waves omit package availability checks <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>SPEC-000</i></b> requires an ordered update sequence but has no acceptance check that identifies
publication and retrievability as prerequisites between waves. In F1, a producer change can
therefore yield a plausible consumer wave even when only some of its packages publish; the queued
planning spec calls for publication-gated ordering, but V0 completion does not test it.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
An ordered plan can conceal that a consumer needs an available, retrievable producer artifact before updating.
## Fix Focus Areas
- docs/specs/SPEC-000-v0-definition.md[19-19]
- docs/specs/SPEC-000-v0-definition.md[31-31]
- docs/specs/SPEC-000-v0-definition.md[39-43]
## Recommended Fix
Require the offline report and JSON to state artifact-publication and consumer-restore prerequisites without claiming to verify them live. Add a partial-publication fixture whose next wave is conditional rather than ready.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/b399403e-b4e1-4010-8af5-b0bccb7b104e?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/b399403e-b4e1-4010-8af5-b0bccb7b104e)

===== INLINE by qodo-code-review[bot] on docs/specs/SPEC-000-v0-definition.md:40 =====
<img src="https://img.shields.io/badge/Medium-634FD1?style=flat-square" height="20px" alt="Remediation recommended">

9\. Outside-team work looks executable <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>SPEC-000</i></b> tests only that F2 orders correctly despite non-ownership, without requiring the plan to
distinguish a team-owned change from a request or wait on another team. When the outside producer
must release E1, the same ordered repository action can appear for R4 as for a repository Joe&#x27;s team
may change.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
F2 verifies order but not whether the plan represents an outside team's change as coordination rather than authorized work.
## Fix Focus Areas
- docs/specs/SPEC-000-v0-definition.md[32-32]
- docs/specs/SPEC-000-v0-definition.md[40-40]
- docs/specs/SPEC-001-evidence-and-graph-model.md[23-30]
## Recommended Fix
Require F2's plan to retain the external ownership boundary and show a request, wait, or escalation prerequisite instead of an executable local update. Carry the required owner evidence into SPEC-002.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/be06b864-7ec9-4d6d-9ddc-4a820443330b?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/be06b864-7ec9-4d6d-9ddc-4a820443330b)

===== INLINE by qodo-code-review[bot] on .agent-control/lanes/spec-review-loop.yaml:58 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

4\. A silent reviewer can halt review <code>🐞 Bug</code> <code>☼ Reliability</code>

<pre>
<b><i>spec-review-loop.yaml</i></b> requires two returned Codex/Kiro reviews for quorum and configures both bots
as required, despite the doctrine treating silence after one working day as no-objection. If either
owner-ferried reviewer does not return, the configured quorum cannot be met, so the lane cannot
follow the documented proceed-on-silence path.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
Two mandatory returns conflict with the doctrine's no-objection outcome for a silent reviewer.
## Fix Focus Areas
- .agent-control/lanes/spec-review-loop.yaml[48-58]
- .agent-control/lanes/spec-review-loop.yaml[97-114]
- .agent-control/lanes/spec-review-loop.yaml[136-146]
- docs/specs/REVIEW-DOCTRINE.md[28-31]
## Recommended Fix
Define one consistent silence outcome across quorum, required-bot waits, and non-return policy. If silence remains non-vetoing, test one timely receipt and one absent receipt through the one-working-day boundary.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/d7085942-3363-48e5-b3f1-998c44c7ab37?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/d7085942-3363-48e5-b3f1-998c44c7ab37)

===== INLINE by qodo-code-review[bot] on .agent-control/lanes/spec-review-loop.yaml:89 =====
<img src="https://img.shields.io/badge/Medium-634FD1?style=flat-square" height="20px" alt="Remediation recommended">

10\. Review can continue past round three <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>localReviewFallback</i></b> starts a Claude review when the freshness-cycle ceiling is reached and permits
further attempts and fix cycles. Because the lane maps that ceiling to completion of round three,
this path conflicts with the doctrine&#x27;s absolute stop after round three and its instruction to send
remaining blockers to Joe.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
The configured fallback can initiate review work after the doctrine's final round.
## Fix Focus Areas
- .agent-control/lanes/spec-review-loop.yaml[38-41]
- .agent-control/lanes/spec-review-loop.yaml[83-90]
- docs/specs/REVIEW-DOCTRINE.md[7-12]
## Recommended Fix
Disable post-ceiling review fallback for this lane, or explicitly place any fallback inside the three-round budget. Test that exhausting round three produces the documented human-decision outcome without another review cycle.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/dd1456b3-7a0b-4923-9148-dbeb0922090c?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/dd1456b3-7a0b-4923-9148-dbeb0922090c)

===== INLINE by qodo-code-review[bot] on docs/specs/REVIEW-DOCTRINE.md:16 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

5\. Unfixed majors can end spec review <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
<b><i>REVIEW-DOCTRINE</i></b> ends any round with zero *new* blockers or majors without checking whether earlier
major objections were fixed. If round one finds a major and round two still fails its proposed
acceptance check but discovers nothing new, the stated stop rule ends review without verifying the
required fix.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
The zero-new-issues rule can end a delta review while a round-one major remains unfixed.
## Fix Focus Areas
- docs/specs/REVIEW-DOCTRINE.md[9-20]
- docs/specs/REVIEW-DOCTRINE.md[22-26]
- docs/specs/REVIEW-DOCTRINE.md[37-39]
## Recommended Fix
Require delta review to verify resolution of carried blockers and majors before convergence. Specify the decision path for a failed existing acceptance check, separately from newly discovered issues.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/65caae9c-f416-4b28-91d8-13457fa052be?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/65caae9c-f416-4b28-91d8-13457fa052be)

===== INLINE by chatgpt-codex-connector[bot] on docs/specs/SPEC-001-evidence-and-graph-model.md:39 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Add the primary producer evidence to the input contract**

This declares `IsPackable`/package-id facts to be V0's primary producer rung, but the complete input list only admits `PackageReferenced` and packages.config facts plus a package delta. Those inputs describe consumption/change, not which repository produces a package, while MSBuild evaluation and build-info ingestion are explicitly out of scope. Consequently F1–F3 cannot create evidence-backed `ProducerClaim`s without inventing fixture knowledge; add and version-pin a producer-fact export in the V0 inputs.

Useful? React with 👍 / 👎.

===== INLINE by chatgpt-codex-connector[bot] on docs/specs/SPEC-001-evidence-and-graph-model.md:27 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Require resolved dependency evidence for transitive paths**

The specified tracemap reference facts cannot populate the promised resolved version or direct/transitive path: `docs/planning/12-coordinator-synthesis.md` explicitly records that tracemap excludes transitive resolution, and V0 accepts no restored assets, lock data, package manifests, or resolver execution inputs. In the offline F4 scenario the implementation therefore cannot distinguish or explain the transitive path and pin from the declared references alone; require a recorded restore artifact/precomputed resolution graph or add local resolution to scope.

Useful? React with 👍 / 👎.


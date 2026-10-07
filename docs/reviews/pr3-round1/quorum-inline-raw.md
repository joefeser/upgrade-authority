===== qodo-code-review[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:18 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

1\. A known consumer enters the wrong wave <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
The unit-ripple rule classifies every consumer of a package produced by an affected repo as
<b><i>affected</i></b>, even when the consumer’s transitive path to the delta target is unproved. In F4b, R2
produces I1 and is affected, but R3’s observed I1 reference has no lockfile path to P2; the
normative golden instead classifies R3 <b><i>unknown</i></b> and leaves it unscheduled.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
Unconditional unit ripple schedules F4b's R3, contradicting its normative unknown classification.
## Fix Focus Areas
- docs/specs/SPEC-003-impact-plan-and-wave-ordering.md[18-24]
- docs/specs/SPEC-002-producer-ownership-fusion.md[21-23]
## Recommended Fix
Define when an affected producer's observed consumer edge establishes ripple, and when the missing lockfile instead leaves transitive exposure unknown. Make the distinction reproduce both F1 and F4b.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/a67c483a-dff1-4302-9751-684a7303a958?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/a67c483a-dff1-4302-9751-684a7303a958)

===== qodo-code-review[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:45 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

2\. Four goldens require an absent template <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
T1 always includes <b><i>to the configured feed</i></b>, but the goldens for F4a, F4b, F6 and F7 use a shorter
publication prerequisite without that phrase. Parameter substitution cannot produce those required
strings, so an implementation following the declared templates fails byte-exact comparison.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
T1 cannot render the short publication wording in four normative goldens.
## Fix Focus Areas
- docs/specs/SPEC-003-impact-plan-and-wave-ordering.md[43-54]
## Recommended Fix
Add the exact short form and a deterministic selection rule that distinguishes it from T1.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/cbae69a6-b66f-4d08-9907-6d23135dc65a?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/cbae69a6-b66f-4d08-9907-6d23135dc65a)

===== qodo-code-review[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:41 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

3\. Conditional waves fail golden matching <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
The exact-strings-only rule supplies no templates for several normative conditional and provisional
prerequisites or conditions. F2’s two waves, F3a’s unknown-producer wave, F3b’s
contradiction-resolution wave and F8’s unpublished-package wave all contain strings outside T1–T7,
leaving implementations unable to derive those bytes from the contract.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
The exhaustive template claim excludes strings required by four goldens.
## Fix Focus Areas
- docs/specs/SPEC-003-impact-plan-and-wave-ordering.md[41-54]
## Recommended Fix
Specify the exact additional condition and prerequisite forms, their parameters, and when each is selected. Check every emitted string against all 12 goldens.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/97b3efc8-0d75-4efc-931c-2eca92de22a8?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/97b3efc8-0d75-4efc-931c-2eca92de22a8)

===== qodo-code-review[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:54 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

4\. Direct consumers get extra prerequisites <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
The T2 selection rule says to emit an authenticated-restore prerequisite when a consumer bumps a
known target version, without distinguishing otherwise similar direct consumers. F1’s R2 wave has
T2, while F8’s R2 wave directly references its delta target but has only T1; applying the rule
uniformly adds bytes absent from F8.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
T2 selection does not reproduce the different direct-consumer prerequisite arrays in F1 and F8.
## Fix Focus Areas
- docs/specs/SPEC-003-impact-plan-and-wave-ordering.md[45-54]
## Recommended Fix
Define an input-backed condition for emitting T2, or reconcile the normative goldens through the reviewed fidelity process.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/632e6821-abba-4616-8bd2-2078e1e33135?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/632e6821-abba-4616-8bd2-2078e1e33135)

===== qodo-code-review[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:56 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

5\. Json formatting cannot match every golden <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
The serialization rule specifies two-space indentation and field order but not whether nested
objects and arrays occupy one line or several, or how non-ASCII characters are escaped. F1 uses
inline nested JSON and literal dashes, whereas F3a uses expanded objects and escaped dashes, so the
stated rules permit outputs that are valid yet fail the required byte comparison.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
Two-space indentation and field order cannot reproduce the corpus's different inline and expanded JSON layouts.
## Fix Focus Areas
- docs/specs/SPEC-003-impact-plan-and-wave-ordering.md[55-60]
## Recommended Fix
Adopt one fully specified serializer and escaping policy, then reconcile goldens through the fidelity process, or prescribe a reproducible layout rule covering both existing styles.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/50c80682-bd3d-46a5-b128-9b00ee7e5069?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/50c80682-bd3d-46a5-b128-9b00ee7e5069)

===== qodo-code-review[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:34 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

6\. A public dependency gets conflicting status <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
The <b><i>ready</i></b> definition requires prerequisites to be in-plan producer publishes, while <b><i>conditional</i></b>
covers dependencies outside the plan’s control. F5 marks wave 1 <b><i>ready</i></b> despite its T5 prerequisite
requiring N1 from a public feed with no in-plan producer, so the status rule and normative golden
disagree.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
F5's ready wave has an external public-feed prerequisite excluded by the ready definition.
## Fix Focus Areas
- docs/specs/SPEC-003-impact-plan-and-wave-ordering.md[32-35]
- docs/specs/SPEC-003-impact-plan-and-wave-ordering.md[49-54]
## Recommended Fix
Define explicitly whether a stated public-feed prerequisite permits ready status, while preserving the normative F5 status or reviewing a golden change.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/3e41394c-d040-494e-be42-e1d9617b0b05?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/3e41394c-d040-494e-be42-e1d9617b0b05)

===== qodo-code-review[bot] on docs/specs/SPEC-002-producer-ownership-fusion.md:17 =====
<img src="https://img.shields.io/badge/High-634FD1?style=flat-square" height="20px" alt="Action required">

7\. An unrelated package creates an extra gap <code>🐞 Bug</code> <code>≡ Correctness</code>

<pre>
The external-package rule assigns an unknown-producer gap to every package absent from both
<b><i>producers[]</i></b> and <b><i>externalPackages[]</i></b>, without restricting which packages matter to the plan. F1
includes RU’s direct X9 reference but no X9 producer declaration, while its normative <b><i>gaps[]</i></b>
records E1 only; following the new rule adds an unwanted X9 gap.
</pre>


<details>
<summary><strong>Agent Prompt</strong></summary>

```
## Issue description
The unrestricted unknown-producer rule adds an X9 gap absent from F1's golden.
## Fix Focus Areas
- docs/specs/SPEC-002-producer-ownership-fusion.md[17-27]
## Recommended Fix
Specify which referenced packages warrant plan-level producer gaps and why RU's unrelated X9 does not, then test the resulting gap list against F1.
```

<code>ⓘ Copy this prompt and use it to remediate the issue with your preferred AI generation tools</code>
</details>

[Dismiss ↗](https://app.qodo.ai/findings/72ec0826-70ab-49d2-bb84-e04f3aa2c6e1?action=dismiss&origin=git) | [View ↗](https://app.qodo.ai/findings/72ec0826-70ab-49d2-bb84-e04f3aa2c6e1)

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:18 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Reconcile unit ripple with the no-lockfile golden**

SPEC-003 needs REWORK because the relevant F1 and F4b subgraphs are equivalent: affected R2 produces I1, R3 directly consumes I1, and neither fixture supplies an R3 lockfile. This rule therefore classifies both R3s as affected, while the F4b golden and line 24 require R3 to remain unknown; conversely, removing the rule breaks F1. Rewrite this blanket ripple rule—the kill-one candidate—and either correct one golden or add evidence that distinguishes the cases, plus a paired acceptance check.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:54 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Align prerequisite selection with the goldens**

The selection rules emit prerequisites that several normative goldens omit: T2 is required for direct consumers bumping a known target, but F4a, F6, F7, and F8 have no restore prerequisite; T3 is required for ripple consumers, but F4a wave 3 has only T4. F1 includes T2/T3 for structurally similar work, so an implementation cannot infer the golden distinction. Add an evidence-based discriminator or reconcile the goldens, and acceptance-check each template's presence as well as its text.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:45 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Add the missing byte-exact prerequisite templates**

T1 is not byte-exact for F4a, F4b, F6, or F7 because those goldens omit `to the configured feed`, which parameter substitution cannot remove. The table also has no templates for the additional F2, F3a, F3b, and F8 prerequisite/condition strings. Enumerate every golden variant or normalize the goldens, then add an acceptance check mapping every emitted prerequisite and condition to exactly one template.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:56 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Remove the false trailing-newline requirement**

The byte contract requires a trailing newline, but the normative F-cyc, F3a, F3b, and F7 `plan.json` files do not end in one; they also use expanded formatting while most other goldens compact nested objects onto single lines. A serializer following this sentence cannot match all 12 files byte-for-byte. Either canonicalize and review the corpus or specify fixture-derived formatting without asserting a universal newline.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:26 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Keep evidenced consumers affected under uncertainty**

These `unknown when` bullets overlap the earlier affected rules and contradict two goldens: F3a R11 directly references P7 yet is classified affected despite the unknown producer, and F3b R7 directly references P5 yet is affected despite the contradiction. State that these conditions change actionability/wave status rather than overriding an observed affected edge, and add precedence checks for both fixtures.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:36 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Do not exclude every unknown-action repo from waves**

The normative corpus schedules unknown-action repos: F3a R11 appears in a conditional wave, and all three F3b repos have `actionType: "unknown"` while appearing in provisional waves. Only F2 RM is excluded because its ownership is unknown. Narrow this rule to the actual ownership/actionability condition and acceptance-check all three cases.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:66 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Correct the universal prerequisite wording assertion**

F8 wave 3 contains `R1 publishes P6 (cite: producer-evidence.v0 P6 publicationStatus = unpublished)`, which does not contain `stated, not verified live`. Thus this acceptance criterion fails a merged golden even before implementation. Either change that golden through the fidelity process or narrow the assertion to the prerequisite forms that actually carry the disclaimer.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:55 =====
**<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub>  Specify ordering for every emitted array**

The byte contract orders only a subset of arrays. It leaves prerequisite order, reasons, evidenceKinds, notes, gaps, contradiction claims, downstreamProvisional, and multiple not-affected repos unconstrained, so equally valid implementations—or shuffled input records—can produce different bytes. Define ordering for each array and add an acceptance check that permutes input declaration order and still obtains identical output.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-002-producer-ownership-fusion.md:17 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Scope unknown-producer gaps to plan-relevant packages**

SPEC-002 is FIX THEN SHIP because this unconditional rule would add an `X9 producer` gap in F1: RU has an observed X9 reference, X9 has no producer claim, and it is not declared external, yet the normative golden contains only the E1 producer gap. Define exactly which encountered packages warrant producer-resolution gaps—apparently only plan-relevant exposure—and add an acceptance check that unrelated references do not change `gaps[]`.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:16 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Define how every change in a delta is planned**

The pinned `package-delta.v1` input contains a `changes[]` array and does not restrict it to one item, but these rules consistently refer to a single “delta target.” For a valid input containing two package changes, it is therefore undefined whether classification and ripple closure use one target or both, which version supplies template parameters, and whether one or multiple plans are emitted; an implementation could silently ignore a change while conforming to this text. Explicitly reject multi-change inputs or define how their affected subgraphs and waves are combined, with a multi-change acceptance case.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:34 =====
**<sub><sub>![P1 Badge](https://img.shields.io/badge/P1-orange?style=flat)</sub></sub>  Reconcile ready status with external prerequisites**

The `ready` definition excludes prerequisites that are not in-plan producer publishes, yet the normative F5 golden marks wave 1 `ready` while its only prerequisite is availability of externally declared N1, and acceptance criterion 6 explicitly preserves that T5 prerequisite. F1 also labels waves containing consumer restore prerequisites as ready. An implementation following this status rule must disagree with those goldens, so define `ready` in terms that admit satisfied/stated restore and external-feed prerequisites, or change the normative statuses.

Useful? React with 👍 / 👎.

===== chatgpt-codex-connector[bot] on docs/specs/SPEC-003-impact-plan-and-wave-ordering.md:31 =====
**<sub><sub>![P2 Badge](https://img.shields.io/badge/P2-yellow?style=flat)</sub></sub>  Define deterministic wave partitioning**

Topological producer-before-consumer ordering does not determine which simultaneously eligible repositories share a wave: for example, after R1 an implementation may legally place F6's R8 and RB together in wave 2, as the golden does, or in separate waves 2 and 3. Sorting release units only orders an already chosen wave and the statement that waves are sequential does not supply the grouping algorithm, so near-fixture plans can vary despite the byte-determinism goal. Specify a deterministic layering rule, such as grouping all currently eligible units at the same dependency depth, and acceptance-check that partitioning.

Useful? React with 👍 / 👎.


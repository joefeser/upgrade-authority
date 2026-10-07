# Round 2 — coordinator-side verifier output (verbatim)

**Reviewer:** fresh general-purpose adversarial subagent, standing in for `claude-local` (the lane's bounded fallback reviewer, currently non-functional: `claude -p` returns 401 against its GLM backend — auth fix pending). **Provenance caveat:** this is coordinator-side verification, not an independent external reviewer; Kiro's verdict may arrive later (late issues count only as blockers).
**Date:** 2026-10-02 · **Basis:** revision-2 specs, diff of f8342a5, round-1 raw logs, lane YAML, tracemap checkout, ACK 0.5.5 operating notes.

---

# Round 2 delta review — upgrade-authority brief-001 (2026-10-02)

## 1. Verdicts

- **SPEC-000 = SHIP** (minors logged for inline fix per doctrine §2; no re-review needed)
- **SPEC-001 = SHIP** (one minor provenance residue, log inline)

## 2. Per-finding resolution

| finding | status | resolving section | justification |
|---|---|---|---|
| B1 | RESOLVED | SPEC-001 §2.3, §4; SPEC-000 §2.1–2.2, §5.6 | Real mechanism: `producer-evidence.v0` sidecar with pinned shape `{source: fixture-declared, repo, packageId, producedVersion?, evidenceNote?}` is now the *only* producer rung; rungs 1–3 demoted to "(future, not V0)"; the false "tracemap IsPackable facts" claim replaced with the verified "tracemap emits no producer-side facts today" note; Kiro's exact acceptance check appears verbatim in SPEC-000 §5.6 and SPEC-001 §7.7. Diff confirms substantive rewrite, not a mention. |
| M1 | RESOLVED | SPEC-001 §2.5, §7.4; SPEC-000 §2.3, F4a/F4b | Lockfile rows declared as optional input #5 with `direct`/`transitive`/`unknown` + the 256-char null-`names` caveat pinned in both specs; F4 split into F4a (path reconstructed) / F4b (`Unknown` + gap, "not `not-affected`") with distinct goldens and acceptance checks. One fixture-typo residue (see §3). |
| M2 | RESOLVED | SPEC-000 §2.4, §5.2; SPEC-001 §3 (`ImpactPlan`), §4 | Three-state classification is a normative invariant in the entity model and the plan definition, with F4b/F6 goldens asserting unknown-not-clean in the adjacent missing-evidence cases; "never binary" kills the no-match shortcut. |
| M3 | RESOLVED | SPEC-001 §4 (bold), §5; SPEC-000 F3b, §5.2 | "Precedence affects confidence display only — never which claim survives" is an explicit rule, not prose; corroboration defined as independent sources (distinct repo+commit+rule, same file counts once); §5 + F3b golden force downstream of contradicted producers into provisional waves `blocked-on: <contradiction id>`; asserted in acceptance criteria of both specs. |
| M4 | RESOLVED | SPEC-001 §2.4; SPEC-000 §2.4, F2 | `ownership.v0` input with pinned shape, missing-repo → `Unknown` "never ours"; F2 golden requires `external — request/await`, "no executable step is emitted for R4". |
| M5 | RESOLVED | SPEC-000 §4 preamble, F1, §5.1 | "Input files + golden `plan.json`, committed before implementation… a fixture without a golden file is not a fixture"; substitution requires recorded reason + review; F1 edges fully named; acceptance 1 requires golden match or recorded reviewed diff. The SPEC-005 harness piece is genuinely pulled forward. |
| M6 | RESOLVED | SPEC-000 §4: F5, F6, F7, F8, F-cyc | All four missing shapes plus cycle are present as named fixtures with goldens. |
| m1 | RESOLVED | SPEC-001 §3 (`ReleaseUnit`); SPEC-000 §2.4, F1b, §5.3 | `basis` is a field on the entity (JSON, not prose) and acceptance 3 requires it in the JSON; F1b exercises independent versioning inside one repo. |
| m2 | RESOLVED | SPEC-001 §3 (`PackageId`, `PackageVersion`) | Real split in the entity model; version nullable everywhere; `producedVersion?` optional in the sidecar schema. |
| m3 | RESOLVED | SPEC-001 §3 (`Repository`), §7.8 | Normalization rules spelled out plus a dedicated acceptance check. |
| m4 | RESOLVED | SPEC-000 §5.1 | Network-disabled Windows CI run with Joe's hands-on run retained as final sign-off. |
| m5 | PARTIALLY | SPEC-001 §2.2 | Schema pinned and independently verified against the actual tracemap sample — matches exactly. But Kiro also asked to record the tracemap commit SHA; the spec says "verified round 1" without naming any SHA or commit. Minor provenance residue. |
| m6 | RESOLVED | REVIEW-DOCTRINE §2 + one-line version | Real edit (diff-confirmed): "zero **open** blockers/majors" with explicit definition of "open". |
| Q4 | RESOLVED | lane `reviewQuorum` | Real config change (diff-confirmed): `minimumReturned: 2` → `1` with `preferAllReturned: true`; both-silent types out to `REQUIRED_REVIEW_QUORUM_NOT_MET` → owner decides. |
| Q8 | RESOLVED | SPEC-000 §2.4, F1, F8 | Waves must state publication/restore prerequisites and claim no live verification; F8 golden emits `conditional (awaiting publication)`, "never `ready`". |
| Q10 | PARTIALLY | lane `localReviewFallback` comment | The only change was a comment; the config values are unchanged, and ACK 0.5.5's own operating note states the automatic trigger is "freshness-ceiling-only" and `maxFixCycles` counts post-ceiling fallback receipt→patch heads — the machine can run review cycles past the 3-round budget, contradicting the comment. Mitigations: doctrine outranks the lane, receipts owner-admitted only, spend capped, exhaustion ends in typed owner decisions. Process-risk, not a spec defect. |

## 3. Open blockers/majors

**zero open blockers/majors.**

Non-blocking items (fix inline):

1. **(minor, spec defect)** SPEC-000 §4 F4a: golden demands `R3 → I1 → P2` but cites "`R2`'s checked-in `packages.lock.json`". R2's lockfile cannot evidence R3's transitive path. One-word slip (`R2` → `R3`); unproducible golden.
2. **(minor, provenance)** m5 residue: record the tracemap commit SHA in SPEC-001 §2.2 (Kiro verified against `main @ 32ab4c3`).
3. **(lane config, below major)** Q10: make the lane honest or the mechanism match — either `localReviewFallback.maxFixCycles: 0`, or reword. Also fix the dangling "SPEC-001 §4a" ref and label the "§7.1" fidelity-rule reference.

## 4. Where the author is most likely still fooling themselves

The Q10 "fix" is the tell: the lane comment asserts a doctrine mapping that ACK 0.5.5 demonstrably does not implement — the automatic trigger is ceiling-only and `maxFixCycles: 2` is precisely the post-ceiling patch authority Qodo flagged, unchanged. The author wrote the *narrative* of the mechanism instead of the mechanism, and "doctor-validated" (schema validity, not behavior) plus claude-local's 401 means the lane has never been exercised end-to-end — it is trusted prose. The same pattern shows smaller in F4a naming a golden its declared evidence cannot produce — exactly the class of untraceable-edge claim the golden-fixture discipline exists to catch. The corpus is now well-specified on paper, but every fixture is still unexecuted prose, so the real risk has moved from "specs are vague" to "specs are specific and unverified." The counterweight: this project's own rules (golden files before implementation, recorded-diff-only substitutions, `fixture-declared` honesty caps) are written tightly enough to catch these at build time — which is why both specs ship.

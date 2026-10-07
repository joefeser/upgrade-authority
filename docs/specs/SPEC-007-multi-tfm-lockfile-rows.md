# SPEC-007 — Multi-TFM lockfile rows and version-disagreement findings

**Status:** draft — round 1 review (brief-007)
**Author:** ZCode (coordinator)
**Date:** 2026-10-03
**Depends on:** SPEC-001 (input schemas) · SPEC-003 (plan + determinism contract) · SPEC-005 (ingest) · committed real scan `testdata-ingest/tracemap-rich/scans/svc/` (Kiro, tracemap `main @ 32ab4c3`)

## 1. Purpose

Let the planner accept a repo whose lockfiles resolve the same package to different versions — across projects (one `packages.lock.json` per project) and across TFMs (one TFM group per lockfile) — and surface those disagreements as **findings** in the plan instead of refusing with a typed error. This is the normal shape of a real multi-project estate; refusing it blocks the ingest bridge on exactly the repos the tool exists to help (Kiro, PR #6 round 3, M1: "a multi-project repo whose lockfiles disagree is the normal case. That disagreement is exactly the 'one project lags on the vulnerable version' situation the tool exists to find").

## 2. Problem (with committed evidence)

Today `lockfile-rows.v0` carries one row per package per repo, and `ua ingest` enforces it: the first row for a package wins the slot, and a second row with a different version is a typed error (exit 4). The committed svc scan demonstrates the real shape:

| Row (from `scans/svc/facts.ndjson`) | lockfile | TFM | package | relation | resolved |
|---|---|---|---|---|---|
| fact-dd1a57… | `src/Api/packages.lock.json` | net8.0 | Contoso.Core | direct | **1.0.0** |
| fact-2f104b… | `src/Api/packages.lock.json` | net8.0 | Newtonsoft.Json | transitive | **13.0.1** |
| fact-f4661f… | `src/Api/packages.lock.json` | net8.0 | Serilog | direct | 3.1.1 |
| fact-1019bf… | `src/Worker/packages.lock.json` | net8.0 | Contoso.Core | direct | **0.9.0** |
| fact-c9cb42… | `src/Worker/packages.lock.json` | net8.0 | Newtonsoft.Json | transitive | **12.0.3** |

- Contoso.Core disagrees across lockfiles (1.0.0 vs 0.9.0 — explained upstream by Worker's `VersionOverride="0.9.0"` under CPM).
- Newtonsoft.Json disagrees across lockfiles (13.0.1 vs 12.0.3 — each Contoso.Core version drags its own transitive closure).
- Worker's `net48` lockfile group is `packages-lock-group-unsupported` in the scan: **no rows exist for it**. It is already honestly handled as a coverage gap (PR #6 B1); this spec must not invent rows for it.

Current behavior on this input: `error: package Contoso.Core in repo svc resolves to multiple versions (1.0.0 and 0.9.0)`, exit 4. No plan is produced.

## 3. Input schema: `lockfile-rows.v1` (v0 frozen)

A row's identity changes from `packageId` to **(repo, lockfile, tfm, packageId)**. Because that is a semantic change to a pinned contract (SPEC-001 §2.5 / `docs/schemas/input-schemas.md`), it ships as a new schemaVersion; `v0` is frozen, not reinterpreted.

```json
{
  "schemaVersion": "lockfile-rows.v1",
  "repo": "svc",
  "rows": [
    { "packageId": "Contoso.Core", "type": "direct", "version": "1.0.0", "via": null, "names": "Newtonsoft.Json", "lockfile": "src/Api/packages.lock.json", "tfm": "net8.0" },
    { "packageId": "Newtonsoft.Json", "type": "transitive", "version": "13.0.1", "via": "Contoso.Core", "names": null, "lockfile": "src/Api/packages.lock.json", "tfm": "net8.0" }
  ]
}
```

- Same single-repo envelope as v0 (`schemaVersion`, `repo`, `rows`). Multi-**repo** lockfiles remain out of scope (§10).
- `lockfile` (string, repo-relative path of the packages.lock.json) and `tfm` (string, possibly empty — copied verbatim from tracemap) are **required keys on every v1 row**. They are the disagreement-detection key; a row without them is malformed input (typed error).
- Multiple rows per `packageId` are legal. Under one `(lockfile, tfm, packageId)` there is exactly one resolution in a well-formed lockfile, so two rows sharing that key must be **identical in every field** (`version`, `type`, `via`, `names`) to collapse to one row (idempotent re-ingest); any differing field is **conflicting evidence for one resolution group** — a typed error (§7), never a silent first-wins.
- `via`, `names`, `version` keep their v0 meanings (`names: null` ⇒ unknown child detail, never evidence of absence).

**Version acceptance (planner):** `v0` files keep today's semantics exactly — trusted corpus inputs, one row per package, duplicates-with-different-versions rejected at load. `ua ingest` emits **v1 always** (one emitted shape; v0 is never written again). The 16-fixture corpus stays on v0 and its goldens stay byte-identical (§9.1).

## 4. Ingest mapping changes (`ua ingest`)

| Tracemap fact property | v1 row field | Rule |
|---|---|---|
| `lockfilePath` | `rows[].lockfile` | Direct copy (required — facts carry it) |
| `targetFramework` | `rows[].tfm` | Direct copy (empty string preserved) |
| `packageName` | `rows[].packageId` | Unchanged |
| `dependencyRelation` | `rows[].type` | Unchanged |
| `resolvedVersion` | `rows[].version` | Unchanged |
| (derived) | `rows[].via` | **Scoped to the same `(lockfile, tfm)` group**: a transitive row's parent is a direct row *in the same lockfile and TFM* whose `dependencyNames` contains the child. Cross-lockfile parenting is never inferred (Api's Newtonsoft.Json resolves via Api's Contoso.Core, not Worker's). |

- The exit-4 check is **narrowed** to its honest core: same `(lockfile, tfm, packageId)` arriving twice with any differing field (`version`, `type`, `via`, `names`) — impossible facts for one resolution group. Exact duplicates collapse. Cross-lockfile or cross-TFM disagreement no longer errors — all rows are emitted.
- `packages-lock-group-unsupported` groups stay coverage gaps (unchanged, PR #6 B1); ingest never fabricates rows for them.
- Exit 2 (lockfile facts for >1 repo) is unchanged (§10).

## 5. Planner semantics with multiple rows per package

Classification, closure, waves, and prerequisites are unchanged — multiple rows only add evidence and findings:

1. **Closure rule (c)** fires if ANY row of the target is `transitive` (any lockfile, any TFM). Rule (b)/(d) consumer detection (`Type == "direct"`) similarly matches any row. Existing single-row fixtures behave identically.
2. **Dependency edges** from direct rows deduplicate on `(producer, packageId)` — the same package referenced by two lockfiles yields one edge, not two.
3. **Reason wording picks one row, deterministically:** the c-reason uses the first **`transitive`** target row in canonical row order (§8) — rule (c) is established by a transitive row, so the reason must narrate that row's evidence, never a direct row's absent `via` (mixed-relation acceptance: F11, §9.4). Closure enumerations likewise enumerate transitive rows for rule (c). The full multi-resolution picture is carried by findings (§6), never by stacking variant reasons. For v0 inputs the canonical order is `packageId`-only, so every existing golden's chosen row — and its bytes — are unchanged.
4. **`not-affected` closure enumeration** stays `{packageId} {type}` — versions do not change path-existence semantics, and disagreement is a finding, not a reason variant.
5. **Scheduling is unit-level, unchanged.** A repo with disagreeing lockfiles appears once, in one wave; the plan does not schedule per lockfile (§10).
6. **Evidence-kind provenance:** the `lockfile-rows.*` evidence kind cites the input's actual schemaVersion — `lockfile-rows.v1` for v1 inputs, `lockfile-rows.v0` for v0 inputs — occupying the same slot in SPEC-003 §4.2's canonical `evidenceKinds[]` order. A plan built from ingested v1 scans never attributes its evidence to a v0 file (F9/F10/F11 assert this).

## 6. Findings (`uncertainty.findings[]`)

A **finding** is an evidenced disagreement inside one repo — the mirror of a gap (gaps = missing evidence; findings = evidence that conflicts). It never gates classification, waves, or statuses (unlike contradictions, which block provisional waves): svc with two disagreeing lockfiles is still one `ready` wave.

- One finding per `(repo, packageId)` whose rows carry **more than one distinct non-null, non-empty `version`** — target or not (Contoso.Core gets a finding; it is why Newtonsoft.Json disagrees). Same version across lockfiles/TFMs = agreement = no finding. A row with a null/empty `version` is an **unevidenced** resolution: it never participates in the distinct-version count and never appears in the enumeration — missing evidence cannot disagree (F10 carries a mixed known/unknown case). It must not vanish silently: `ua ingest` records a per-repo coverage gap (`resolved version not evidenced: {pkg}`) for every null-version row it emits — the same honesty treatment as constraint-not-evidenced facts (PR #6 B1) — so an unknown resolution always surfaces as uncertainty even when the finding rules say nothing.
- Shape: `{ "subject": "{repo} version disagreement: {pkg}", "detail": <T10 text> }` — same shape as gaps; sorted with them by subject (tie: detail).
- Emitted **only when non-empty** (like `notes`/`stop`), so every existing golden's bytes are unchanged.

**Templates** (added to SPEC-003 §4.1's exhaustive table; the validator's one-template mapping extends to findings):

| id | kind | template |
|---|---|---|
| T10 | finding | `{repo} resolves {pkg} to {n} versions across its lockfile/TFM resolution groups: {resolutions} — evidenced disagreement, not an error; V0 schedules {repo} once at unit level and picks no winner (convergence is deliberately out of scope, §10)` |
| T10a | finding trailer | `; {repo} also has lockfile groups the scanner could not parse and contributed no rows — those resolutions stay unevidenced (coverage gap)` |

- `{resolutions}` = `;`-joined `"{version} in {lockfile} ({tfm})"`, sorted by (lockfile, tfm) ordinal.
- T10a is appended to a repo's findings when that repo produced ≥1 finding AND its scan coverage carries an unsupported-lockfile-group gap — the honesty trailer for the net48 case: the finding lists evidenced rows only and says so, instead of implying the enumeration is complete. Unsupported-group gaps remain coverage gaps; T10a references, never replaces, them.

## 7. Typed errors (updated table)

| Condition | Where | Exit |
|---|---|---|
| Same `(lockfile, tfm, packageId)`, any differing field (`version`, `type`, `via`, `names`) — impossible within one resolution group | ingest | 4 (narrowed) |
| v0 file with duplicate `packageId`, different `version` | plan load | 3 (UaException) |
| v1 row missing `lockfile` or `tfm` key | plan load | 3 (UaException) |
| v1 duplicate `(lockfile, tfm, packageId)` with any differing field | plan load | 3 (UaException) |
| Lockfile facts for >1 repo in one ingest run | ingest | 2 (unchanged, §10) |

## 8. Determinism contract additions (SPEC-003 §4)

1. **Canonical row order** (engine-side enumeration; ingest also writes rows in this order): `(packageId, lockfile, tfm, version)` — ordinal compares. v0 files (no lockfile/tfm) sort by `packageId` alone, byte-identical to today.
2. **Findings array**: subject ordinal, tie-break detail ordinal (same rule as gaps).
3. **Resolutions inside a finding detail**: (lockfile, tfm) ordinal.
4. **Permutation acceptance** extends to v1: reversing any input array (rows included) must produce identical output bytes (F9/F10/F11 selftest cases).
5. **Canonical serialization**: `findings` written only when non-empty, after `gaps`, in the `uncertainty` object; same formatter rules (2-space indent, LF, trailing newline, minimal escaping).

## 9. Acceptance criteria

1. **Zero golden churn:** all 16 existing fixtures produce byte-identical `plan.json` and `report.md`. No GOLDEN-CHANGES entries. (The contract change is purely additive; if implementation surfaces any diff, it is a bug or a fidelity-log event — never silently regenerated.)
2. **New fixture `F9-multi-lockfile-disagreement`** — hand-authored golden-first, inputs mirroring the committed svc scan exactly (rows per §2, CPM consumer facts with `version: ""`, ownership svc=team-a self, producer-evidence externalPackages=[Newtonsoft.Json] + producers=[corelib/Contoso.Core 1.0.0], delta Newtonsoft.Json 12.0.3→13.0.3): svc `affected` via rule (c); wave 1 `ready` with T5; findings for Contoso.Core (1.0.0/0.9.0) and Newtonsoft.Json (13.0.1/12.0.3), each enumerating both resolutions; **no** finding for Serilog; golden validates against `plan.schema.v1.json` (extended with optional `findings`).
3. **New fixture `F10-multi-lockfile-agreement`** — v1 rows, same versions across lockfiles/TFMs: zero findings; `not-affected` positive-evidence closure enumerates the multi-row lockfile. Proves disagreement detection does not over-fire and not-affected works on v1. *(Amended during implementation, recorded reason: the original draft placed the null-`version` row here, but its `resolved version not evidenced` coverage gap forces coverage status `gaps`, which contradicts not-affected's complete-coverage requirement on the same repo — the null-version case moved to F11.)*
4. **New fixture `F11-mixed-relation`** — the delta target `direct` in one lockfile, `transitive` (via a parent) in another, different versions, plus a null-`version` row (mixed known/unknown): rule (c) fires on the transitive row and the c-reason narrates **that** row's path (never the direct row's absent `via`); a finding covers the version disagreement; the null row never counts toward it and never renders (its `resolved version not evidenced: {pkg}` gap is declared in the fixture's `scanCoverage`, mirroring what ingest emits); relation difference itself gets no separate finding (§10).
5. **Typed rejections as selftest cases:** v0-duplicate and v1-same-key conflicts (§7 rows 1–3) yield their typed errors — never a partial plan.
6. **Ingest of the committed svc scan** (`testdata-ingest/tracemap-rich/scans/svc`) exits 0 and emits `lockfile-rows.v1` with 5 rows, each carrying `lockfile` + `tfm`, `via` derived per-lockfile (both Newtonsoft.Json rows via Contoso.Core).
7. **End-to-end, machine-checked forever:** a permanent selftest case runs ingest → `ua plan` → `ua report` on the committed svc scan and asserts: plan validates; svc affected via rule (c); both findings present; `lockfile-rows.v1` cited as the evidence kind; report renders the Findings section and the summary bullet. The real-tool fidelity of PR #6 (Kiro's lesson) becomes a repeatable check, not a one-time demo.
8. **Report rendering:** `### Findings` subsection after `### Gaps`; summary bullet `- Version disagreements: {n} — see Uncertainty` emitted only when findings exist (existing fixtures' reports unchanged); finding text passes through `Text()`.
9. **Validator:** every finding detail maps to exactly one template (T10, optionally ending with T10a) — same one-template enforcement as prerequisites/conditions; F9/F10/F11 assert `lockfile-rows.v1` evidence-kind provenance.

## 10. Out of scope (typed, honest, logged)

- **Multi-repo lockfiles** (ingest exit 2 stays): removing it needs planner changes (per-repo lockfile sets) *and* a real multi-lockfile-repo estate scan to validate against — building it from source-reading alone repeats PR #6's original input-model mistake. Queued for the next slice once such a scan is committed.
- **Winner-picking or convergence recommendations** — V0 reports the disagreement; deciding whether svc should converge on 13.0.1 is a human/Phase-2 (`ua apply`) decision.
- **Per-lockfile wave scheduling** — scheduling stays unit-level (SPEC-003 §3); a finding never splits a unit.
- **Delta ↔ lockfile reconciliation** (e.g. flagging that the delta's oldVersion 12.0.3 matches only Worker's resolution) — future spec.
- **Relation disagreements as such** (same package `direct` in one lockfile, `transitive` in another): the relation difference alone emits **no** separate finding and no template wording — no committed real-world scan evidences the shape yet (F11 pins the *mechanics* synthetically: rule (c) on the transitive row, the c-reason narrating it; wording beyond that must not be invented without a real fixture).
- **Rule-(c) coverage-gap surfacing:** today `{repo} coverage` gaps reach `plan.gaps` only for direct-fact (rule-b) repos; a lockfile-evidenced (rule-c) repo's coverage gaps stay in inputs. T10a covers the unsupported-group case adjacent to this feature; the general fix is logged as a known issue, not smuggled into this slice.

## 11. Implementation notes

- Spec PR (this document): spec + `docs/schemas/input-schemas.md` v1 section + SPEC-QUEUE row. Implementation PR: models/ingest/planner/canonical/report changes, `plan.schema.v1.json` + validator, F9/F10/F11 (golden-first per the fidelity rule), selftest cases, `ua ingest` scan output for F9's input regeneration note. Expected GOLDEN-CHANGES: none.
- Engine detail worth pinning: `via` parenting must consider only rows sharing `(lockfile, tfm)`; the existing `FirstOrDefault` parent choice within a group stays (deterministic under canonical row order).
- `tfm: ""` is legal data (copy verbatim); it keys rows and renders as `()` — no inference, no invention.

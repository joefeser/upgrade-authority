# SPEC-024 round 1 — triage (2026-10-11)

**Quorum record:** one adversarial stand-in with repo access. Verdict: **REWORK** — "the architecture is sound; a security report whose verdicts and headline depend on unstated choices needs those choices in the spec." All 13 findings folded into v2.

| # | Finding | Severity | Disposition → v2 |
|---|---|---|---|
| 1 | "null-version rows count" was FALSE parity — drift skips them (Drift.cs:199, SPEC-018 law); the parenthetical was lifted from the REGISTRY's consumed-set rule; and audit never defined what it DOES with such a row | **Blocker** | Accepted — drift's rule adopted VERBATIM (skip + the same stderr note; a version join cannot classify an invisible version); the registry's packageId-alone rule stays where it belongs |
| 2 | Boundary rule mishandled the produced∩declared-external anomaly (external must be AND-NOT-produced); audit had NO anomaly channel — a silent label on a security report | Major | Accepted — registry semantics exactly; the anomaly package is internal WITH `"anomaly": "produced-and-declared-external"` named on the row |
| 3 | Missing --advisories file exit unstated (1 vs 5) | Major | Accepted — exit 1 naming the path (the drift missing-feed precedent); absent flag ⇒ usage exit 1 |
| 4 | Every summary field's computation undefined (packagesObserved meaning; dedup arithmetic under multi-advisory; advisoriesWithFindings numerator; advisoryCount scope) | Major | Accepted — every count pinned; installation arithmetic is per-package DEDUPED with worst-status precedence (affected > unclassified > not-affected); advisoryCount vs consideredCount separated |
| 5 | Exact-list equality undefined (string vs padding-aware); exact-list vs prerelease⇒unclassified precedence undefined; acceptance 3 overclaimed comparer parity | Major | Accepted — CompareCore equality (padding-aware); exact-list WINS over unclassified (the list is exact evidence); parity scoped to the fixedIn path |
| 6 | "Mirrors OSV/GHSA" oversold; multi-window/last_affected advisories collapse unsoundly to one fixedIn (BOTH failure directions silent) | Major | Accepted — §2's EXPORTER CONTRACT: multi-window ⇒ split entries, last_affected ⇒ exact list; collapsing is an unsound export; native shapes later |
| 7 | Multi-package advisories unrepresentable (one OSV id, many affected[] packages); validation made faithful exports a hard error | Major | Accepted — dedup key = (advisoryId, packageId) |
| 8 | Ecosystem comparison undefined; OSV spells "NuGet" | Minor | Accepted — OrdinalIgnoreCase; skip precedes validation |
| 9 | Misattributed ecosystem-skip precedent (SPEC-020 has none) | Minor | Accepted — the drift folder-feed warn+skip precedent cited |
| 10 | "Usable" undefined; both-unusable unspecified | Minor | Accepted — usable = ≥1 parseable entry or parseable fixedIn; neither ⇒ exit 5; unparseable entries within a usable list skip with a note |
| 11 | Reason templates unpinned (goldens are byte-exact) | Minor | Accepted — A1/A2/A3 pinned verbatim (the D1-D3 adaptations) |
| 12 | Estate-spelling rule ambiguous (drift 3-level vs registry 6-level) | Minor | Accepted — drift's 3-level, stated |
| 13 | All-clean invisibility vs "visible absence, never silence" | Minor | Accepted — packages[] admits ANY advisory interaction; notAffectedInstallations makes checked-and-clean visible |

## Fresh round 1 on v2 (same day) — verdict: FIX THEN SHIP (3 majors + 4 minors, all folded into v3)

| # | Finding | Severity | Disposition → v3 |
|---|---|---|---|
| 1 | §2's list-entry clauses were mutually contradictory (CompareCore equality can't match a prerelease even verbatim; "skip unparseable entries" vs "the list wins") | Major | Two-tier entry matching: numeric entries by CompareCore equality; non-numeric by ordinal verbatim; nothing skipped; usability = any entry or parseable fixedIn |
| 2 | The v2 arithmetic had zero acceptance coverage (worst-status, advisoriesWithAffected < count, the (advisoryId, packageId) key) | Major | F-audit grows explicit rows: dual-advisory install, all-clean advisory, one-id-two-packages, export-gap |
| 3 | Open-ended windows (introduced, no fixed) still unrepresentable — silent-drop fabricates clean | Major | Exporter MUST refuse unsound windows: exported as `severity: "export-gap"`; ua renders them unclassified with the gap marker (visible absence); last_affected needs FULL enumeration |
| 4 | Dedup parenthetical misstated drift's key ((repo,version,evidence) vs drift's (repo,version)) | Minor | Drift's exact key stated; evidence resolved by precedence |
| 5 | Worst-status honesty rested on unstated per-advisory row preservation | Minor | findings[].installations[] = PER-ADVISORY classification; reason rides unclassified rows only — stated |
| 6 | title had no slot; no-fixedIn-no-match undefined | Minor | title dropped (severity echoed on findings); no-fixedIn-no-match ⇒ not-affected (the list is exhaustive) |
| 7 | Anomaly spelling divergence (kebab vs registry's camel) | Minor | registry's own spelling `producedAndDeclaredExternal` adopted |

## Delta round (round 2) on v3 — verdict FIX THEN SHIP; both blockers folded into v4

- **Export-gap boundary incoherence**: the export-gap entry collided with the neither-usable⇒exit-5 rule and the dropped-title marker. v4: explicit exemption (severity `export-gap` IS the marker) + the always-unclassified finding with its pinned reason.
- **Stale kebab anomaly spelling** in §3 contradicted §4 and the registry's actual field. v4: `producedAndDeclaredExternal` everywhere.

## v4 close-out: both delta-round blockers were two-sentence local fixes that moved no architecture (the reviewer's own assessment); with the exemption + marker pinned and the spelling unified, the spec has zero open blockers/majors — **SHIP** per doctrine §2. Proceeds to implementation.

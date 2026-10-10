# SPEC-020 round 1 — triage (2026-10-09)

**Quorum record:** stand-ins (same as prior spec rounds — external quorum rides the implementation PR).

| # | Finding | Severity | Disposition | Fix applied |
|---|---|---|---|---|
| 20-1 | Provenance unrecoverable from post-fusion input (fusion deletes the loser; the committed sidecar already declares corelib ⇒ acceptance contradicted the corpus bytes) | **Blocker** | Accepted | Provenance recorded ONCE at fusion (`ProducerEntry.provenance`, versioned backward-compatible addition; absent ⇒ sidecar⇒operator else project); registry reads the field; acceptance re-reconciled to the b1b sidecar shape |
| 20-2 | produced∩external silently resolved opposite to the planner (which keeps both signals); scan-estate's bootstrap template creates exactly this conflict | Major | Accepted | `anomalies.producedAndDeclaredExternal: [ids]` — surfaced, never silently resolved; planner behavior unchanged (logged follow-up) |
| 20-3 | Package identity case-sensitivity unpinned at the boundary joins; "consumed" undefined (null-version rows?); unconsumed external declarations | Major | Accepted | OrdinalIgnoreCase + Drift's spelling rule; consumed = any consumer fact or lockfile-class row (null-version rows count); unconsumed externals echoed; lists ordinal-sorted |
| 20-4 | "Exit 0 always" contradicted the typed-errors sentence | Minor | Accepted | "exit 0 for any successfully loaded input" |
| 20-5 | Triple-intersection disjointness was vacuous | Minor | Accepted | pairwise disjointness + case-variant check in the validator |
| 20-6 | Boundary list ordering unstated | Minor | Accepted | ordinal (byte-golden safe) |
| 20-7 | ci-defined consuming contract would need a second fix when tracemap's slice lands | Question → accepted | Contract pinned now: the fact's provenance property maps to the entry's provenance at fusion |

# PR #21 golden changes (SPEC-012 implementation)

Per the acceptance-fidelity rule (Session 12 §7.1): every change below was derived from the SPEC-012
rules FIRST (each diff adjudicated line-by-line against §2 before any golden was written), reviewed
against the spec text in the PR, and is machine-checked by the new selftest case + validator rules.

| Fixture | Change | Rule |
|---|---|---|
| F9 | Newtonsoft.Json finding: `12.0.3 … [delta from-version]`; trailer removed from this finding (stays on the subject-first Contoso.Core finding); new gap `svc coverage (lockfile-evidenced)` | §2.1 (delta-scoped marker), §2.2 (once/repo), §2.3 (G2) |
| F11 | `1.0.0 … [delta from-version]`; new gap `R11 coverage (lockfile-evidenced)` (first gap: resolved version not evidenced: Bogus.Null) | §2.1, §2.3 |
| F12 | New gaps `billing coverage (lockfile-evidenced)` + `shipping coverage (lockfile-evidenced)`; findings unchanged (cross-repo difference, §SPEC-008 §8) | §2.3 |
| F13 | Contoso.Core finding: `1.0.0 … [delta from-version]` (delta oldVersion); Newtonsoft.Json finding UNMARKED (not the delta package); report golden re-derived; push dry-run golden re-captured (PR bodies inherit the finding text) | §2.1 |
| F10, F14–F23 | Untouched (byte-identical) | — |

## Incidental fix (recorded)

The PR #11-known locale-vs-ordinal gaps[] sort divergence became load-bearing (F12 mixes
`billing…` with `Newtonsoft.Json producer` subjects). `canonicalize-goldens.mjs` now sorts gaps by
code-unit order, matching `Engine.BuildGaps`'s `CompareOrdinal` — same fix findings received on
PR #8. No golden bytes changed as a result (all current subjects agree under both orders).

# SPEC-017 golden changes — F-scope addition

Reviewed per the fidelity rule: every golden delta adjudicated against the spec BEFORE capture.

| Fixture | Change | Spec rule |
|---|---|---|
| F-scope | **NEW fixture** (F2's inputs verbatim + `input/scope.v0.json` `{"exclude":["archived","spikes"]}`); golden = F2's golden + the `scope` block and one report line | SPEC-017 §5 (scope echo; key order `mode`/`includedCount?`/`outOfScope?`; report line directly after Repositories) |
| All 23 existing fixtures | **byte-identical** — machine-checked by the selftest corpus run (117 cases green) and `validate-fixtures.mjs` (24 goldens) | §3/§5: no scope input ⇒ no scope field, no report line (zero churn by construction: `LoadEngine` loads `scope.v0.json` only when present; `Canonical`/`Report`/validator emit scope only when non-null) |

The zero-churn claim is not just observed here — the corpus IS the check: any fixture gaining or
losing a byte fails `selftest`/`validate-fixtures.mjs` in CI.

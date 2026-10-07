# PR #5 round 1 triage — V0 report (2026-10-02)

**Quorum batch:** Codex 3 (1 P1, 2 P2) · Qodo 7 (5 correctness, 1 reliability, 1 observability) · Sourcery 3. Raw: [quorum-inline-raw.json](quorum-inline-raw.json). **All 13 accepted; none rejected.** Strong convergence: all three reviewers independently caught the GFM table escaping hole and the notes/publication order swap; Codex P1 + Qodo 7 converge on dropped plan facts.

| Finding(s) | Disposition |
|---|---|
| Codex P1 + Qodo 7: dropped plan facts (basis, evidenceKinds, confidence, delta.origin, claim packageIds) | **Accepted.** Unit bullet gains ` · {basis}`; repos table gains Evidence + Confidence columns; delta line appends origin when present; contradiction claims render `{repo} ({pkg})` |
| Qodo 1 (+Sourcery/Codex notes-order): prereqs/conditions nest under the last unit in GFM | **Accepted.** Wave-level `Prerequisites:` / `Condition:` blocks rendered BEFORE the unit list (own bullets, items nested); SPEC-004 §3.3 updated |
| Sourcery 1 + Codex P2: publication before notes (spec: notes first) | **Accepted.** Order swapped; F8/F-gate goldens regenerate |
| Sourcery 2 + Codex P2 + Qodo 5: `|`/newline break GFM cells | **Accepted.** Shared `Cell()` encoder: `\`→`\\`, `|`→`\|`, CR/LF→space — applied to every cell; SPEC-004 §3.5 documents it |
| Qodo 2: input text forges headings/bullets | **Accepted.** Markdown-context encoding: headings/labels only from renderer constants; dynamic text in cells/bullets is line-collapsed by Cell()/Text() so no forged structure possible |
| Sourcery 3: CR in plan text → non-LF report | **Accepted.** Final `.Replace("\r\n","\n").Replace('\r','\n')` normalization at Render exit |
| Qodo 3: missing report.md still passes | **Accepted.** report.md REQUIRED for every fixture in the corpus; missing = typed failure |
| Qodo 4: string compare vs BOM | **Accepted.** Byte compare: UTF8(no BOM) bytes vs File.ReadAllBytes |
| Qodo 6: report-only failure shows plan diff | **Accepted.** Render once from the built plan; DumpDiff on the report pair when plans match |
| New acceptance case | **F-pipe:** a reason/path containing `|` and a line break — proves Cell() (golden hand-derived from rule) |

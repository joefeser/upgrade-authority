# SPEC-010 acceptance 6 — one-time real GitHub API validation

**Run:** 2026-10-03, coordinator (ZCode), against a private throwaway repo created for this purpose.
**Token:** fine-grained PAT, 7-day expiry, permissions Contents:RW + Pull requests:RW (+ auto Metadata:R),
no Administration (repo creation via API correctly refused 403 — least-privilege confirmed). Joe killed
the token after the run.

## Steps (exactly as run)

1. `git clone` throwaway `joefeser/upgrade-authority-glm-test`; commit the svc fixture sources on `main`; push.
2. `GITHUB_TOKEN=<pat> ua push fixtures/F13-apply-cpm-override --repo <clone> --base main --out <dir> --pr`
3. Exit 0. `push.v1.json` observed[]: commit `13003fb63dc16b4576700fa113fc3f1f1ed4ec5b`, PR url below.

## Results

- **Branch:** `ua/wave-1/Contoso.Core-1.0.0-to-1.1.0` (P1) pushed to origin.
- **PR #1:** https://github.com/joefeser/upgrade-authority-glm-test/pull/1
  - title = P2 verbatim: `ua: Contoso.Core 1.0.0 -> 1.1.0 (wave 1)`
  - head→base: `ua/wave-1/…` → `main`
  - body = P7–P12 verbatim: status, prerequisite (stated-not-verified language preserved), both edits with
    kinds + evidence kinds, BOTH svc findings, "Human merges." footer.
- **Commit message:** subject P2; body carries `wave-status: ready`, the prerequisite, evidence trailer,
  generated-by trailer (P3a/P3/P4/P5).
- **Diff:** exactly the two evidenced files (Directory.Packages.props, src/Worker/Worker.csproj).
- Sourcery auto-reviewed the throwaway PR (additive bot) — no action.

**Verdict: VERIFIED.** The API path behaves exactly as specified; the selftest stays offline (no network
in tests, by design). SPEC-010 acceptance 6 is satisfied; the throwaway repo + PR remain for Joe's
inspection and can be deleted at will.

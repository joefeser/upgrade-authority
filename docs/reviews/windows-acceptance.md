# Windows network-disabled acceptance run (SPEC-000 §5.1) — 2026-10-02

**Environment:** an isolated Windows 11 VM (network-disabled) on the coordinator machine. Windows 11 (build 10.0.26200.8873), .NET SDK 10.0.302. **Network isolation verified:** ping 8.8.8.8 — no reply; curl https://example.com — no connection. Zero NuGet packages (build had no restore traffic).

**Results:**

| Check | Result |
|---|---|
| `dotnet build` | 0 warnings, 0 errors |
| `ua selftest` (all fixtures) | **PASS — 33/33** (16 goldens + 16 permutations + rejection) |
| `ua plan F1` vs golden plan.json | **byte-identical** (`fc /b`: no differences) |
| `ua report F1` vs golden report.md | **byte-identical** (`fc /b`: no differences — em-dashes intact, LF-only output, no CRLF corruption) |

**Verdict: the Windows network-disabled acceptance criterion is met.** The tool runs correctly on Windows with no internet and produces byte-identical output to the canonical goldens. Joe's hands-on sign-off on the work machine remains the final human gate.

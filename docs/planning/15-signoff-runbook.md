# Wednesday sign-off runbook — 2026-10-07 (or whenever you sit down at the work machine)

Plain-language walkthrough of the hands-on acceptance. Everything here already passed on the
isolated offline Windows VM (82/82, byte-exact) — this run is you confirming it on YOUR machine,
with YOUR eyes, on real data if you bring it. Budget: ~30 minutes without the real-estate part.

## 0. Preflight (60 seconds — catch environment surprises before they cost you)

```
dotnet --version      # expect 10.x (any 10; the tool builds in ~5s)
git --version         # any modern git; required by ua push
git config user.email # must be set (ua push commits as YOU — your identity, never invented)
```

If any of those fail, fix them first — everything else will work regardless of network state
(the tool is offline; the only network step in this runbook is `git clone`/`dotnet build` fetches).

## 0b. One-time setup (5 min)

```
git clone https://github.com/joefeser/upgrade-authority   # trunk: main
cd upgrade-authority
dotnet build src/UpgradeAuthority    # ~5s, zero packages, no network needed after clone
function ua { dotnet run --project src/UpgradeAuthority -- @args }   # optional convenience (PowerShell; bash is ua() { ... "$@"; })
```

**No-build option:** a self-contained single-file binary also works (verified; the suite is now 98 cases) —
`dotnet publish src/UpgradeAuthority -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`,
then run `ua.exe` directly. Note `selftest` needs the repo checkout beside it (it reads
`fixtures/` and `testdata-ingest/`); plan/report/apply/push/ingest only need their own arguments.

## 1. The demo estate (10 min) — "does it do the thing"

The committed real scans (Kiro's tracemap output, refreshed with tracemap 0.2.0):

```
ua ingest --scans-root testdata-ingest/tracemap-rich/scans --out C:\ua-demo `
  --producer testdata-ingest/tracemap-rich/sidecars/producer-evidence.v0.json `
  --ownership testdata-ingest/tracemap-rich/sidecars/ownership.v0.json `
  --delta testdata-ingest/tracemap-rich/sidecars/delta.json

ua plan C:\ua-demo | Out-File plan.json -Encoding utf8    # open in an editor
ua report C:\ua-demo | Out-File report.md -Encoding utf8  # open this one — it's the human view
# (Windows PowerShell 5.1: run `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8` once per
#  window FIRST — plain `>` decodes the tool's UTF-8 with the legacy codepage and mangles the → and —)
```

What you should see in report.md (the things worth eyeballing):
- Newtonsoft.Json 12.0.3 → 13.0.3 plan; svc affected; ONE ready wave with the "available from the
  public feed (stated, not verified live)" prerequisite — the tool never claims it checked a feed.
- **Version disagreements section**: Contoso.Core (1.0.0 vs 0.9.0) and Newtonsoft.Json (13.0.1 vs
  12.0.3), with `[delta from-version]` on Worker's 12.0.3 — the "who's actually on the old version"
  call-out.
- The "scanner could not parse some lockfile groups" honesty note — once, not repeated.
- A **"### Scan notes"** section (added since this runbook was first written — your SPEC-015 decision):
  compile/build health per repo (compiler diagnostics, buildStatus, analysisLevel) as visible
  INFORMATION, never blocking. Expected, not an error. Ingesting just `billing`+`shipping` is the
  quick demo: both now classify `complete` (compile noise is no longer a coverage gap).

## 2. From plan to actual edits (10 min) — "does it write the change"

```
git clone <any copy of the svc sources> C:\svc        # or copy testdata-ingest\tracemap-rich\sources\svc
ua apply fixtures/F13-apply-cpm-override --repo C:\svc --out C:\apply-out
type C:\apply-out\wave-1.patch                        # a real, git-apply-able patch:
                                                      #   Directory.Packages.props  1.0.0 -> 1.1.0 (central pin)
                                                      #   src\Worker\Worker.csproj   0.9.0 -> 1.1.0 (override)
```

Then the safety checks (each should REFUSE, never guess):
```
:: edit C:\svc\Directory.Packages.props so the pin says 9.9.9, then:
ua apply ... --repo C:\svc --out C:\apply-out2        # -> error: stale evidence. It refused. (exit code 6)
:: restore the 1.0.0 pin. Then the same drill for push:
cd C:\svc ; git init . ; git add -A ; git commit -m base ; git branch -m main
ua push ..\..\fixtures\F13-apply-cpm-override --repo . --base main --out C:\push-out
git log --oneline main -1                              # STILL "base" — your branch never moved
git log --format="%h %s" ua/wave-1/Contoso.Core-1.0.0-to-1.1.0 -1   # the gated commit on the new branch
ua push .. (again)                                     # -> error: branch exists. It refused. (exit code 7)
```

## 3. The full battery (2 min)

```
dotnet test isn't used — the tool self-checks:
ua selftest        # expect: SELFTEST PASS (98 cases)
node tools/validate-fixtures.mjs   # expect: ALL GOLDENS PASS (23)  [needs node; skip if absent]
```

## 4. Real estate (optional, the exciting one)

**Coming from a folder of repo checkouts? ua never reads repos directly — tracemap is the scanner.**
The step between "my repos" and `ua ingest` is a tracemap scan per repo (each repo must be a git
repo with at least one commit — tracemap records the SHA; real checkouts already are):

```powershell
mkdir C:\ua-estate\scans

# build tracemap ONCE — `dotnet run` re-checks/rebuilds per invocation (minutes each at estate scale).
# The assembly is named tracemap.dll (project folder is TraceMap.Cli). If the $cli path 404s:
#   dir C:\path\to\tracemap\src\dotnet\TraceMap.Cli\bin\Release -Recurse -Filter tracemap.dll
dotnet build C:\path\to\tracemap\src\dotnet\TraceMap.Cli -c Release
$cli  = "C:\path\to\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll"
$root = "C:\path\to\repos"

# scan every repo under $root — resume-safe: repos with an existing facts.ndjson are skipped,
# so Ctrl+C any time and re-run the block to continue where it left off.
# Noisy folders (SQL migrations, packages/) can be trimmed per repo with repeatable globs:
#   dotnet $cli scan ... --exclude "SqlMigrations/**" --exclude "packages/**"
# (also the cure for tracemap's SourceSnapshotChangedDuringScan on repos where something
#  touches a watched file mid-scan — the check re-hashes file CONTENT, git-clean is not enough)
$repos = Get-ChildItem $root -Directory
foreach ($r in $repos) {
  $out = "C:\ua-estate\scans\$($r.Name)"
  if (Test-Path "$out\facts.ndjson") { Write-Host "skip $($r.Name) (already scanned)"; continue }
  Write-Host "scan  $($r.Name)"
  dotnet $cli scan --repo $r.FullName --out $out
}
```

Start with 2–3 repos, not the whole estate. Then the three sidecar files ingest requires
(multi-repo runs refuse implicit sidecars on purpose — one repo's stray file must not speak for all):

- **ownership** — generate it, don't hand-write:
  `ua ownership init --scans-root C:\ua-estate\scans --self <team> --all-self --out C:\ua-estate\ownership.v0.json`
  (then hand-edit any repos that are actually another team's; `--unassigned` gives a checklist instead)
- **producer** — minimal file is fine: producers INSIDE the estate are discovered from the scans
  themselves; only packages from outside it (public-feed packages) get listed:
  `'{"schemaVersion":"producer-evidence.v0","externalPackages":[{"packageId":"Newtonsoft.Json"}]}' | Out-File C:\ua-estate\producer-evidence.v0.json -Encoding utf8`
- **delta** — the one genuinely hand-written file: which package, old version → new version:
  `{"version":"package-delta.v1","sourceRepo":"https://example.invalid/x.git","sourceCommitSha":"0000000000000000000000000000000000000000","changes":[{"id":"nj","packageName":"Newtonsoft.Json","ecosystem":"nuget","changeType":"updated","oldVersion":"12.0.3","newVersion":"13.0.3"}]}`

With scan output and sidecars in place:

```
ua ingest --scans-root C:\ua-estate\scans --out C:\estate --producer C:\ua-estate\producer-evidence.v0.json --ownership C:\ua-estate\ownership.v0.json --delta C:\ua-estate\delta.json
ua plan C:\estate | Out-File estate-plan.json -Encoding utf8
ua report C:\estate | Out-File estate-report.md -Encoding utf8
```

Note: sidecars must be explicit for multi-repo runs (one repo's stray sidecar file can't silently
speak for the estate — it will refuse and tell you which flag is missing). If you don't have an
ownership.v0 at full-estate scale yet, that's expected — that's the next work item, not a blocker today.

## 4b. If anything fails — sanitized feedback (built for this)

Re-run the same command with `--sanitized` and paste the output straight into GitHub/chat/email:

```
ua <same command> --sanitized
```

Paths become `[path:file#hash]`, non-public hosts/URLs become `[url#…]`/`[host#…]`, emails `[email#…]`
— nothing identifying your machine, directory structure, or infrastructure leaves it. Your repo's
internal NAMES are the one thing the tool can't guess: set `UA_REDACT` to a comma list of
proprietary words (`UA_REDACT=OurCompany,InternalPackage`) and they become `[tok#…]` too.
Hashes are stable, so we can correlate lines across a paste without seeing the originals.
Canonical stdout (plan/report) is never transformed — only the diagnostics channel.

## 5. What "sign-off" means

You confirm: the commands above ran on your work machine, the outputs made sense to you, and the
refusals refused. Anything that surprises you goes to ZCode as review findings, not blockers —
the corpus already pins today's behavior byte-exactly.

## Known gaps (honest list, all logged)

- Going from a repos root to a report is manual today (scan loop + sidecar bootstrap above);
  a one-command `scan-estate` wrapper is the queued convenience.
- Producer evidence inside the estate is scan-discovered (PackageProduced); the sidecar remains
  for external/public-feed packages and hand-declared producers.
- No ownership.v0 at full-estate scale exists yet — `ua ownership init` generates the skeleton;
  the actual team assignments at estate scale are a human work item.
- `ua push --pr` against real GitHub Enterprise is untested against a live GHE (github.com verified
  via the throwaway-repo run; the override is stub-tested offline).

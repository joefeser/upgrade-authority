# Estate Runbook

The operating loop for scanning a real estate, distilled from the 2026-10
validation cycles. Every command here is the tested shape; PowerShell 5.1-safe
(assume arrays where you expect values, and wrap method calls in per-item
iteration — see the traps at the bottom).

## The loop (RUN ORDER)

```powershell
$ua = "src/UpgradeAuthority/bin/Release/net10.0/ua.dll"   # build first: dotnet build src/UpgradeAuthority -c Release

# 0) tracemap — pull dev, rebuild, PROVE the dll is fresh (a stale dll re-scans nothing and lies)
cd C:\tracemap
git checkout dev; git pull
dotnet build src/dotnet/TraceMap.sln -c Release
Get-Item src/dotnet/TraceMap.Cli/bin/Release/net10.0/tracemap.dll | Select-Object LastWriteTime

# 1) full estate scan — worktree mode; capture ALL streams (transient failures leave receipts)
dotnet $ua scan-estate --repos-root <repos-root> `
  --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
  --scope <repos-root>\repos.txt `
  --out C:\ua-estate --parallel 8 --worktree *> C:\ua-estate\scan-run-log.txt
# notes: a ci-hints.v1.json sidecar in the OUT DIR enables the ci producer lane by itself
#        (loud notes both ways); a non-zero tracemap exit retries 3x with backoff, and the
#        skip line carries tracemap's stderr tail — silence is never the answer shape;
#        the first run needs --self <teamId> ONCE (ownership bootstrap), or an existing ownership.v0.json
# "--archived <file>" excludes archived repos from waves (evidence retained)

# 2) vintage census before ingest — the whole estate must be ONE vintage (count of stale = 0)
$dll = (Get-FileHash C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll -Algorithm SHA256).Hash
Get-ChildItem C:\ua-estate\scans -Directory | ForEach-Object {
  $m = Get-Content "$($_.FullName)\scan-estate.json" -Raw | ConvertFrom-Json
  if ($m.tracemapSha256 -ne $dll) { $_.Name }
} | Measure-Object | ForEach-Object Count

# 3) ingest + registry
dotnet $ua ingest --scans-root C:\ua-estate\scans `
  --producer C:\ua-estate\producer-evidence.v0.json `
  --ownership C:\ua-estate\ownership.v0.json --out C:\ua-estate\fixture
dotnet $ua registry C:\ua-estate\fixture --out C:\ua-estate\registry.json
(Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).provenanceSummary
(Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).boundary.internalPackages.Count
(Get-Content C:\ua-estate\registry.json -Raw | ConvertFrom-Json).boundary.unknownPackages.Count
```

## One-repo refresh (the fast path)

Refresh exactly one repo into an existing estate (the rest of the scans stay on
disk; ingest reads the whole scans tree; run the vintage census afterwards):

```powershell
Set-Content C:\ua-estate\one-repo.txt "<that-repo>"
dotnet $ua scan-estate --repos-root <repos-root> `
  --tracemap C:\tracemap\src\dotnet\TraceMap.Cli\bin\Release\net10.0\tracemap.dll `
  --scope C:\ua-estate\one-repo.txt `
  --out C:\ua-estate --parallel 1 --worktree
# then step 3 (ingest + registry)
```

## The ci-hints sidecar (census-derived — its only sanctioned birth)

`ci-hints.v1.json` names the shared CI workflows that mean "this repo
publishes". NEVER hand-type it — derive it from the estate's own yml files so
it cannot drift from reality (a hint that matches nothing is a silent no-op;
two spellings in the wild are the DEFAULT failure shape):

```powershell
$uses = Get-ChildItem <repos-root> -Directory | ForEach-Object {
  $f = Join-Path $_.FullName '.github\workflows\release.yml'
  if (Test-Path $f) { (Select-String -Path $f -Pattern 'uses:\s*(\S+)' -AllMatches).Matches | ForEach-Object { $_.Groups[1].Value.Trim() } }
}
$shared = @($uses | Where-Object { $_ -like '*.github/workflows/*' } | Sort-Object -Unique)
"$($shared.Count) shared workflow spellings:"; $shared
$entries = @(); foreach ($u in $shared) { $entries += @{ uses = [string]$u } }
@{ schemaVersion = 'ci-hints.v1'; packWorkflows = $entries } | ConvertTo-Json -Depth 5 | Set-Content C:\ua-estate\ci-hints.v1.json -Encoding ascii
# ASSERT the shape before trusting it (single-element collapse is a real trap):
$j = Get-Content C:\ua-estate\ci-hints.v1.json -Raw | ConvertFrom-Json
"type: $($j.packWorkflows.GetType().Name)  count: $($j.packWorkflows.Count)"   # must be Object[] + N
```

The file enables the ci producer lane automatically when present in the scan
out dir; `--index-ci-producers` forces it on with no sidecar.

## Failure reading

- `skip <repo> (tracemap exited N after M attempt(s): <stderr tail>)` — the
  receipt. Transient classes (contention, AV races, api blips) die on retry;
  a persistent skip names its cause in the tail.
- `scan-run-log.txt` — the always-captured full stream; the skip context grep:
  `Select-String C:\ua-estate\scan-run-log.txt -Pattern 'skip ' -Context 2,0`
- A MISSING scan folder for a repo = the repo never completed a scan this
  vintage (check the manifest status + the log).
- An absent producer in the registry with its consumers in `unknownPackages`:
  find the producing repo, read its gap lines in
  `scans\<repo>\facts.ndjson` (gap facts name the PROJECT, not the package).

## PowerShell 5.1 traps (all paid for live)

- `(no-match).Line` is `$null` — a collected array of results contains nulls
  that explode on method calls. Guard: `if ($m) { $m.Line }`.
- An array's `.Length` is its ELEMENT COUNT, not characters —
  `@('a','b').Substring(0, 2)` truncates to two chars. Iterate per line.
- `-ceq` on arrays FILTERS (returns matches, prints blank), it does not
  compare. Wrap `@(...)` and check `.Count`.
- `ConvertTo-Json` collapses a single-element pipeline into an object, not an
  array. Build entries explicitly (`$entries = @(); foreach ... +=`).
- `Set-Content` writes UTF-16/BOM by default — use `-Encoding ascii` (or
  utf8) for machine-read files; strict readers reject the BOM.

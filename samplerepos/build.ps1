# samplerepos/build.ps1 — SPEC-022 (D020 v1): the deployable-proof harness.
# Packs the fictional Cassowary Air estate into local folder feeds, restores real consumers
# (real packages.lock.json files), scans with real tracemap, and runs the full ua chain.
# PowerShell 5.1-compatible: no && / ternary / ?? / $IsWindows; explicit $LASTEXITCODE checks;
# conditional args via array splatting; $HOME instead of ~; every ua artifact goes through
# `ua ... --out` (never > / Out-File — BOM/UTF-16 on 5.1).
param(
    [ValidateSet('pack', 'restore', 'scan', 'plan', 'all')]
    [string]$Stage = 'all',
    [Parameter(Mandatory = $true)]
    [string]$TracemapCli,          # absolute path to the tracemap CLI dll (build the sibling checkout; see README)
    [string]$UaCli = '',           # optional: path to a built ua dll. Default: dotnet-runs this repo's project.
    [switch]$LockedMode            # demonstration only: honor a pre-existing lockfile with --locked-mode
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repoRoot = Split-Path -Parent $root
$srcRoot = Join-Path $root 'src'
$feed = Join-Path $root 'feed'
$feedHistory = Join-Path $root 'feed-history'
$scratch = Join-Path $root '.scratch'
$scans = Join-Path $root 'scans'
$out = Join-Path $root 'out'
$sidecars = Join-Path $root 'sidecars'

# Pinned scratch-commit metadata (explicit +00:00 — bare dates parse in LOCAL time and would
# churn the commit epoch across machines). Identity rides `git -c` per invocation (no session leak).
$gitDate = '2026-01-01T00:00:00+00:00'

function Invoke-Checked {
    param([string]$File, [string[]]$ArgList, [string]$What)
    & $File @ArgList
    if ($LASTEXITCODE -ne 0) { throw "$What failed (exit $LASTEXITCODE)" }
}

function Invoke-Ua {
    param([string[]]$ArgList, [string]$What)
    if ($UaCli -ne '') {
        Invoke-Checked 'dotnet' (@($UaCli) + $ArgList) $What
    }
    else {
        $proj = Join-Path $repoRoot 'src/UpgradeAuthority/UpgradeAuthority.csproj'
        Invoke-Checked 'dotnet' (@('run', "--project:$proj", '-c', 'Release', '--') + $ArgList) $What
    }
}

function Invoke-StagePack {
    foreach ($dir in @($feed, $feedHistory)) {
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }   # CLEAN first: a stale nupkg beside a new one is drift's exit-5 trap
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    foreach ($proj in @('Cassowary.Core/Cassowary.Core.csproj', 'Cassowary.GroundOps/Cassowary.GroundOps.csproj')) {
        Invoke-Checked 'dotnet' (@('pack', (Join-Path $srcRoot $proj), '-c', 'Release', '-o', $feedHistory,
                                   '-p:ContinuousIntegrationBuild=true') ) "pack $proj -> feed-history"
    }
    # the latest-only view drift reads + the historical 1.1.0 the behind-consumer restores
    Copy-Item (Join-Path $feedHistory '*.nupkg') $feed
    Invoke-Checked 'dotnet' (@('pack', (Join-Path $srcRoot 'Cassowary.Core/Cassowary.Core.csproj'), '-c', 'Release',
                                   '-o', $feedHistory, '-p:Version=1.1.0', '-p:ContinuousIntegrationBuild=true') ) 'pack historical Cassowary.Core 1.1.0'
}

function Invoke-StageRestore {
    foreach ($proj in @('Cassowary.Booker/Cassowary.Booker.csproj', 'Cassowary.CrewWeb/Cassowary.CrewWeb.csproj')) {
        $args = @('restore', (Join-Path $srcRoot $proj))
        if ($LockedMode) { $args += '--locked-mode' }
        Invoke-Checked 'dotnet' $args "restore $proj"
    }
}

function Invoke-StageScan {
    if (Test-Path $scratch) { Remove-Item -Recurse -Force $scratch }
    New-Item -ItemType Directory -Force -Path $scratch | Out-Null
    $oldAuthor = $env:GIT_AUTHOR_DATE; $oldCommitter = $env:GIT_COMMITTER_DATE
    try {
        $env:GIT_AUTHOR_DATE = $gitDate; $env:GIT_COMMITTER_DATE = $gitDate
        foreach ($repo in @('Cassowary.Core', 'Cassowary.GroundOps', 'Cassowary.Booker', 'Cassowary.CrewWeb')) {
            $dest = Join-Path $scratch $repo
            Copy-Item -Recurse (Join-Path $srcRoot $repo) $dest
            # the tier's guard: no lockfile in the copied consumer tree = the proof degrades to pins-only
            $csprojs = Get-ChildItem $dest -Filter '*.csproj'
            foreach ($p in $csprojs) {
                $pkgDir = Join-Path $p.DirectoryName 'packages.lock.json'
                $isConsumer = (Select-String -Path $p.FullName -Pattern 'PackageReference' -SimpleMatch -Quiet)
                if ($isConsumer -and -not (Test-Path $pkgDir)) {
                    throw "no packages.lock.json beside $($p.Name) — run -Stage restore first (a pins-only scan would silently degrade the harness)"
                }
            }
            Set-Content -Encoding Ascii (Join-Path $dest '.gitignore') "bin/`nobj/`n"   # explicit encoding: 5.1 Set-Content defaults to UTF-16LE, which git cannot parse (PR8 Codex r1)
            Invoke-Checked 'git' (@('-C', $dest, 'init', '-q') ) "git init $repo"
            Invoke-Checked 'git' (@('-C', $dest, 'add', '-A') ) "git add $repo"
            Invoke-Checked 'git' (@('-C', $dest, '-c', 'user.name=cassowary-harness', '-c', 'user.email=harness@example.invalid',
                                       'commit', '-qm', 'estate snapshot') ) "git commit $repo"
            $scanOut = Join-Path $scans $repo
            if (Test-Path $scanOut) { Remove-Item -Recurse -Force $scanOut }
            New-Item -ItemType Directory -Force -Path $scanOut | Out-Null
            $extra = @()
            if ($repo -eq 'Cassowary.Core') { $extra += '--index-ci-producers' }   # the workflow lives only on the producer
            Invoke-Checked 'dotnet' (@($TracemapCli, 'scan', '--repo', $dest, '--out', $scanOut) + $extra) "tracemap scan $repo"
            # commit ONLY facts + manifest; delete everything else the scanner generated (no gitignore negation games)
            Get-ChildItem $scanOut -File | Where-Object { $_.Name -ne 'facts.ndjson' -and $_.Name -ne 'scan-manifest.json' } | Remove-Item -Force
            Get-ChildItem $scanOut -Directory | Remove-Item -Recurse -Force
        }
    }
    finally {
        if ($null -eq $oldAuthor) { Remove-Item Env:GIT_AUTHOR_DATE -ErrorAction SilentlyContinue } else { $env:GIT_AUTHOR_DATE = $oldAuthor }
        if ($null -eq $oldCommitter) { Remove-Item Env:GIT_COMMITTER_DATE -ErrorAction SilentlyContinue } else { $env:GIT_COMMITTER_DATE = $oldCommitter }
    }
}

function Invoke-StagePlan {
    if (-not (Test-Path (Join-Path $scans 'Cassowary.Core'))) { throw 'no committed scans — run -Stage scan first' }
    New-Item -ItemType Directory -Force -Path $out | Out-Null

    # enumeration sanity only (gitignored target); the committed cross-team sidecar is hand-authored
    Invoke-Ua (@('ownership', 'init', '--scans-root', $scans, '--self', 'platform', '--unassigned',
                 '--out', (Join-Path $out 'ownership-sanity.json') )) 'ownership init sanity'

    # ---- run 1: the Core delta (consumers, ci-defined producer, drift statuses) ----
    $fx1 = Join-Path $out 'fixture-core'
    Invoke-Ua (@('ingest', '--scans-root', $scans,
                 '--producer', (Join-Path $sidecars 'producer-evidence.v0.json'),
                 '--ownership', (Join-Path $sidecars 'ownership.v0.json'),
                 '--delta', (Join-Path $sidecars 'delta.json'),
                 '--out', $fx1) ) 'ingest (Core delta)'
    Invoke-Ua (@('registry', $fx1, '--out', (Join-Path $out 'registry-core.v1.json') )) 'registry (Core)'
    Invoke-Ua (@('drift', $fx1, '--folder-feed', $feed, '--out', (Join-Path $out 'drift-core.v1.json') )) 'drift (Core)'
    Invoke-Ua (@('plan', $fx1, '--out', (Join-Path $out 'plan-core.json') )) 'plan (Core)'
    Invoke-Ua (@('report', $fx1, '--out', (Join-Path $out 'report-core.md') )) 'report (Core)'

    # ---- run 2: the Diesel delta (declared-external target: T5/T6/T6a + external-request-await) ----
    $fx2 = Join-Path $out 'fixture-diesel'
    Invoke-Ua (@('ingest', '--scans-root', $scans,
                 '--producer', (Join-Path $sidecars 'producer-evidence.v0.json'),
                 '--ownership', (Join-Path $sidecars 'ownership.v0.json'),
                 '--delta', (Join-Path $sidecars 'delta-external.json'),
                 '--out', $fx2) ) 'ingest (Diesel delta)'
    Invoke-Ua (@('registry', $fx2, '--out', (Join-Path $out 'registry-diesel.v1.json') )) 'registry (Diesel)'
    Invoke-Ua (@('plan', $fx2, '--out', (Join-Path $out 'plan-diesel.json') )) 'plan (Diesel)'
    Invoke-Ua (@('report', $fx2, '--out', (Join-Path $out 'report-diesel.md') )) 'report (Diesel)'

    # ---- stage-4 assertions (the acceptance checks, machine-read from the artifacts) ----
    $reg = Get-Content (Join-Path $out 'registry-core.v1.json') -Raw | ConvertFrom-Json
    $coreEntry = $reg.producers | Where-Object { $_.packageId -eq 'Cassowary.Core' }
    if (-not $coreEntry -or $coreEntry.provenance -ne 'ci-defined') { throw 'registry: Cassowary.Core must be ci-defined (the workflow claim wins)' }
    if (($reg.boundary.anomalies.producedAndDeclaredExternal) -notcontains 'Cassowary.GroundOps.Diesel') { throw 'registry: the Diesel produced-and-declared-external anomaly must be surfaced' }

    $drift = Get-Content (Join-Path $out 'drift-core.v1.json') -Raw | ConvertFrom-Json
    $core = $drift.packages | Where-Object { $_.packageId -eq 'Cassowary.Core' }
    $behind = @($core.installations | Where-Object { $_.repo -eq 'Cassowary.CrewWeb' -and $_.status -eq 'behind' })      # repo-BOUND (PR8 Codex r1): a swapped regression must fail
    $current = @($core.installations | Where-Object { $_.repo -eq 'Cassowary.Booker' -and $_.status -eq 'current' })
    if ($behind.Count -ne 1) { throw 'drift: Cassowary.CrewWeb must be the one behind row (1.1.0 vs feed 1.2.0)' }
    if ($current.Count -ne 1) { throw 'drift: Cassowary.Booker must be the one current row (1.2.0)' }
    foreach ($row in ($behind + $current)) {
        if ($row.evidence -ne 'lockfile') { throw "drift: $($row.repo) must carry evidence 'lockfile' (the whole point of this tier), got '$($row.evidence)'" }
    }
    $diesel = $drift.packages | Where-Object { $_.packageId -eq 'Cassowary.GroundOps.Diesel' }
    $dieselRow = @($diesel.installations | Where-Object { $_.repo -eq 'Cassowary.Booker' -and $_.status -eq 'current' -and $_.evidence -eq 'lockfile' })
    if ($dieselRow.Count -ne 1) { throw 'drift: Diesel must be current via lockfile evidence on Booker' }

    $plan1 = Get-Content (Join-Path $out 'plan-core.json') -Raw | ConvertFrom-Json
    foreach ($must in @('Cassowary.Booker', 'Cassowary.CrewWeb')) {
        $r = $plan1.repos | Where-Object { $_.repo -eq $must }
        if (-not $r -or $r.classification -ne 'affected') { throw "plan (Core): $must must be affected" }
    }

    $plan2 = Get-Content (Join-Path $out 'plan-diesel.json') -Raw | ConvertFrom-Json
    $prereqs = @($plan2.waves | ForEach-Object { $_.prerequisites } | ForEach-Object { $_ })
    if (($prereqs | Where-Object { $_ -like '*available from the public feed*' }).Count -lt 1) { throw 'plan (Diesel): T5 must fire' }
    $conditions = @($plan2.waves | ForEach-Object { $_.condition } | Where-Object { $_ })
    if (($conditions | Where-Object { $_ -like '*external ground-crew releases*' }).Count -lt 1) { throw 'plan (Diesel): T6 (external team coordination) must fire' }
    $ground = $plan2.repos | Where-Object { $_.repo -eq 'Cassowary.GroundOps' }
    if (-not $ground -or $ground.actionType -ne 'external-request-await') { throw 'plan (Diesel): the GroundOps producer must be external-request-await (coordination, not executable work)' }
    $booker = $plan2.repos | Where-Object { $_.repo -eq 'Cassowary.Booker' }
    if (-not $booker -or $booker.classification -ne 'affected') { throw 'plan (Diesel): Booker must be affected through the external target' }

    Write-Host 'stage plan: all assertions green (registry ci-defined + anomaly; drift lockfile-evidence behind/current; Core plan affected x2; Diesel plan T5 + Booker affected)'
}

switch ($Stage) {
    'pack'    { Invoke-StagePack }
    'restore' { Invoke-StageRestore }
    'scan'    { Invoke-StageScan }
    'plan'    { Invoke-StagePlan }
    'all'     { Invoke-StagePack; Invoke-StageRestore; Invoke-StageScan; Invoke-StagePlan }
}

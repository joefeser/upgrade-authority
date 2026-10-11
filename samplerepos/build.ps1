# samplerepos/build.ps1 — SPEC-022 (D020 v1): the deployable-proof harness.
# Packs the fictional Cassowary Air estate into local folder feeds, restores real consumers
# (real packages.lock.json files), scans with real tracemap, and runs the full ua chain.
# PowerShell 5.1-compatible: no && / ternary / ?? / $IsWindows; explicit $LASTEXITCODE checks;
# conditional args via array splatting; $HOME instead of ~; every ua artifact goes through
# `ua ... --out` (never > / Out-File — BOM/UTF-16 on 5.1).
param(
    [ValidateSet('pack', 'restore', 'scan', 'plan', 'campaign', 'all')]
    [string]$Stage = 'all',
    [string]$TracemapCli = '',     # absolute path to the tracemap CLI dll (REQUIRED for scan/all; pack/restore/plan run without it)
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
    $rc = Invoke-UaRaw $ArgList
    if ($rc -ne 0) { throw "$What failed (exit $rc)" }
}

# raw runner: returns the exit code WITHOUT throwing (campaign expects refusals, e.g. push's 7).
# Output goes to the HOST (never the return pipeline — dotnet-run build chatter would pollute $rc).
function Invoke-UaRaw {
    param([string[]]$ArgList)
    if ($UaCli -ne '') {
        $cmdArgs = @($UaCli) + $ArgList            # build the array FIRST (spaced + is not concatenation)
        & dotnet @cmdArgs | Out-Host
    }
    else {
        $proj = Join-Path $repoRoot 'src/UpgradeAuthority/UpgradeAuthority.csproj'
        $cmdArgs = @('run', "--project:$proj", '-c', 'Release', '--') + $ArgList
        & dotnet @cmdArgs | Out-Host
    }
    return $LASTEXITCODE
}

function Invoke-StagePack {
    # STAGE-then-PROMOTE (PR8 Baz r3): both feeds are built beside their targets and swapped in only
    # after EVERY pack succeeds — a mid-pack failure never destroys the last usable feed
    $stageFeed = "$feed.staging"; $stageHistory = "$feedHistory.staging"
    # staging dirs are gitignored (.staging suffix) and swept on FAILURE too (PR8 Codex r4)
    if (Test-Path $stageFeed) { Remove-Item -Recurse -Force $stageFeed }
    if (Test-Path $stageHistory) { Remove-Item -Recurse -Force $stageHistory }
    foreach ($dir in @($stageFeed, $stageHistory)) {
        if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }
    foreach ($proj in @('Cassowary.Core/Cassowary.Core.csproj', 'Cassowary.GroundOps/Cassowary.GroundOps.csproj')) {
        Invoke-Checked 'dotnet' (@('pack', (Join-Path $srcRoot $proj), '-c', 'Release', '-o', $stageHistory,
                                   '-p:ContinuousIntegrationBuild=true') ) "pack $proj -> feed-history"
    }
    # the latest-only view drift reads + the historical 1.1.0 the behind-consumer restores
    Copy-Item (Join-Path $stageHistory '*.nupkg') $stageFeed
    Invoke-Checked 'dotnet' (@('pack', (Join-Path $srcRoot 'Cassowary.Core/Cassowary.Core.csproj'), '-c', 'Release',
                                   '-o', $stageHistory, '-p:Version=1.1.0', '-p:ContinuousIntegrationBuild=true') ) 'pack historical Cassowary.Core 1.1.0'
    foreach ($pair in @(@($stageFeed, $feed), @($stageHistory, $feedHistory))) {
        if (Test-Path $pair[1]) { Remove-Item -Recurse -Force $pair[1] }   # promote: clean swap (a stale nupkg beside a new one is drift's exit-5 trap)
        Move-Item $pair[0] $pair[1]
    }
}

function Invoke-StageRestore {
    foreach ($proj in @('Cassowary.Booker/Cassowary.Booker.csproj', 'Cassowary.CrewWeb/Cassowary.CrewWeb.csproj')) {
        $args = @('restore', (Join-Path $srcRoot $proj))
        if ($LockedMode) { $args += '--locked-mode' }
        Invoke-Checked 'dotnet' $args "restore $proj"
    }
}

function Invoke-StageScan {
    if ($TracemapCli -eq '') { throw '-TracemapCli is required for -Stage scan (and -Stage all): absolute path to the tracemap CLI dll — see samplerepos/README.md' }
    if (-not (Test-Path $TracemapCli)) { throw "TracemapCli not found: $TracemapCli" }
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

    # exact feed inventory BEFORE drift reads it (PR8 Baz r3): a partial feed must never drive artifacts
    $expectedFeed = @('Cassowary.Core.1.2.0.nupkg', 'Cassowary.GroundOps.Diesel.1.0.0.nupkg')
    $actualFeed = @((Get-ChildItem $feed -Filter '*.nupkg' | Sort-Object Name | ForEach-Object { $_.Name }))
    if (($actualFeed -join '|') -ne ($expectedFeed -join '|')) { throw "feed/ inventory wrong (expected exactly $($expectedFeed -join ', ')): $($actualFeed -join ', ')" }

    $drift = Get-Content (Join-Path $out 'drift-core.v1.json') -Raw | ConvertFrom-Json
    $core = $drift.packages | Where-Object { $_.packageId -eq 'Cassowary.Core' }
    if (@($core.installations).Count -ne 2) { throw "drift: Cassowary.Core must carry EXACTLY the two consumer installations, got $(@($core.installations).Count)" }
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
    $expectedRepos = @{
        'Cassowary.Core' = 'affected'; 'Cassowary.Booker' = 'affected'; 'Cassowary.CrewWeb' = 'affected'; 'Cassowary.GroundOps' = 'unknown'   # no lockfile of its own — never not-affected without positive evidence (the strict set caught my wrong first guess)
    }
    foreach ($entry in $expectedRepos.GetEnumerator()) {
        $rows = @($plan1.repos | Where-Object { $_.repo -eq $entry.Key })
        if ($rows.Count -ne 1) { throw "plan (Core): $($entry.Key) must appear EXACTLY once, got $($rows.Count)" }
        if ($rows[0].classification -ne $entry.Value) { throw "plan (Core): $($entry.Key) must be $($entry.Value), got $($rows[0].classification)" }
    }
    if ($plan1.repos.Count -ne $expectedRepos.Count) { throw "plan (Core): expected exactly $($expectedRepos.Count) repos (no extras, no duplicates), got $($plan1.repos.Count)" }

    $plan2 = Get-Content (Join-Path $out 'plan-diesel.json') -Raw | ConvertFrom-Json
    $prereqs = @($plan2.waves | ForEach-Object { $_.prerequisites } | ForEach-Object { $_ })
    if (($prereqs | Where-Object { $_ -like '*available from the public feed*' }).Count -lt 1) { throw 'plan (Diesel): T5 must fire' }
    $conditions = @($plan2.waves | ForEach-Object { $_.condition } | Where-Object { $_ })
    if (($conditions | Where-Object { $_ -like '*external ground-crew releases*' }).Count -lt 1) { throw 'plan (Diesel): T6 (external team coordination) must fire' }
    if (($prereqs | Where-Object { $_ -like '*available and restorable from the consumer environment*' }).Count -lt 1) { throw 'plan (Diesel): T6a (consumer-environment restorability) must fire' }   # PR8 Codex r2
    $ground = $plan2.repos | Where-Object { $_.repo -eq 'Cassowary.GroundOps' }
    if (-not $ground -or $ground.actionType -ne 'external-request-await') { throw 'plan (Diesel): the GroundOps producer must be external-request-await (coordination, not executable work)' }
    $booker = $plan2.repos | Where-Object { $_.repo -eq 'Cassowary.Booker' }
    if (-not $booker -or $booker.classification -ne 'affected') { throw 'plan (Diesel): Booker must be affected through the external target' }

    # SPEC-023: the HACP seam — export each run's waves as task packets and assert the contract shape
    Invoke-Ua (@('export-packet', $fx1, '--wave', '1', '--out', (Join-Path $out 'packet-core.json') )) 'export-packet (Core wave)'
    Invoke-Ua (@('export-packet', $fx2, '--wave', '1', '--out', (Join-Path $out 'packet-diesel.json') )) 'export-packet (Diesel wave)'
    $pkt = Get-Content (Join-Path $out 'packet-diesel.json') -Raw | ConvertFrom-Json
    if ($pkt.recordKind -ne 'hacp.v0_3_candidate.task_packet') { throw 'packet: recordKind const wrong' }
    if ($pkt.profileRefs -notcontains 'ua-hacp-profile-v0.1') { throw 'packet: profile ref missing' }
    if ($pkt.digest.value -notmatch '^[0-9a-f]{64}$') { throw 'packet: digest must be 64 lowercase hex' }
    $gated = @($pkt.stopConditions | Where-Object { $_ -like '*available from the public feed*' })
    if ($gated.Count -lt 1) { throw 'packet: the Diesel wave must carry the T5 gate' }

    Write-Host 'stage plan: all assertions green (registry ci-defined + anomaly; drift lockfile-evidence behind/current; Core plan affected x2; Diesel plan T5 + Booker affected; HACP packets exported)'
}

function Invoke-StageCampaign {
    # receipts carry an explicit terminal status: a mid-campaign failure writes
    # status "failed" BEFORE the throw (a partial receipt must never masquerade as complete)
    try { Invoke-StageCampaignCore }
    catch {
        $partialPath = Join-Path $out 'campaign' 'campaign.v1.json'
        if (Test-Path $partialPath) {
            $partial = Get-Content $partialPath -Raw | ConvertFrom-Json
            $partial | Add-Member -NotePropertyName status -NotePropertyValue 'failed' -Force
            $partial | Add-Member -NotePropertyName failure -NotePropertyValue "$_" -Force
            $partial | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 $partialPath
        }
        throw
    }
}

function Invoke-StageCampaignCore {
    # the campaign demo (post-talk board item 1): walk the plan's waves in ua's WAVE ORDER
    # (ascending index = the dependency order ua derived: producers publish before consumers upgrade),
    # claim each wave's task packet (export), execute the executable ones (apply + push on the REAL
    # scratch git repos), and record WorkReceipt-SHAPED evidence (command/exitCode/counts/artifacts).
    # artifact-memory WorkReceipts themselves are VAULT-RESIDENT by their own contract — never
    # committed to any repo — so this emits the evidence shape into gitignored out/; minting real
    # receipts against an artifact-memory vault is a future slice.
    if (-not (Test-Path (Join-Path $out 'fixture-core'))) { throw 'no plan outputs — run -Stage plan first' }
    $campDir = Join-Path $out 'campaign'
    if (Test-Path $campDir) { Remove-Item -Recurse -Force $campDir }
    New-Item -ItemType Directory -Force -Path $campDir | Out-Null

    $plan1 = Get-Content (Join-Path $out 'plan-core.json') -Raw | ConvertFrom-Json
    $plan2 = Get-Content (Join-Path $out 'plan-diesel.json') -Raw | ConvertFrom-Json
    $waves = @()

    foreach ($run in @(@('core', $plan1), @('diesel', $plan2))) {
        $name = $run[0]; $pl = $run[1]
        foreach ($w in $pl.waves) {
            # the wave's REAL task packet (SPEC-023): exported here so the receipt cites the
            # claimable id, never a synthetic label
            $packetPath = Join-Path $campDir "packet-$name-wave-$($w.index).json"
            Invoke-Ua (@('export-packet', (Join-Path $out "fixture-$name"), '--wave', "$($w.index)", '--out', $packetPath) ) "export-packet $name wave $($w.index)"
            $packetId = (Get-Content $packetPath -Raw | ConvertFrom-Json).packetId
            $entry = [ordered]@{
                run = $name; wave = $w.index; status = $w.status
                packetId = $packetId
                units = @()
            }
            foreach ($u in $w.releaseUnits) {
                $unitEntry = [ordered]@{ repo = $u.repo; action = 'coordination-only'; evidence = @() }
                $scratchRepo = Join-Path $scratch $u.repo
                if (-not (Test-Path $scratchRepo)) { throw "scratch repo missing: $scratchRepo (run -Stage scan)" }
                # per-repo scoped fixture copies (apply/push execute ONE checkout per run — the refusal's own rule)
                $scoped = Join-Path $campDir "fixture-$name-$($u.repo)"
                Copy-Item -Recurse (Join-Path $out "fixture-$name") $scoped
                $pxPath = Join-Path $scoped 'input' 'package-evidence.v0.json'
                $px = Get-Content $pxPath -Raw | ConvertFrom-Json
                $px.facts = @($px.facts | Where-Object { $_.repo -eq $u.repo })
                $px.scanCoverage = @($px.scanCoverage | Where-Object { $_.repo -eq $u.repo })
                $px | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 $pxPath
                # repo-local git identity for the push commit (portable; no global config assumed)
                Invoke-Checked 'git' (@('-C', $scratchRepo, 'config', 'user.email', 'campaign@harness.invalid') ) "git identity $($u.repo)"
                Invoke-Checked 'git' (@('-C', $scratchRepo, 'config', 'user.name', 'cassowary-campaign') ) "git identity name $($u.repo)"
                $base = (git -C $scratchRepo branch --show-current).Trim()
                $applyOut = Join-Path $campDir "apply-$name-$($u.repo)"
                $rcApply = Invoke-UaRaw @('apply', $scoped, '--repo', $scratchRepo, '--out', $applyOut)
                if ($rcApply -ne 0) { throw "apply $($u.repo) failed (exit $rcApply)" }
                $unitEntry.action = 'applied'
                $unitEntry.evidence += @{ command = "ua apply --repo $($u.repo)"; exitCode = $rcApply; counts = @{ }; artifacts = @() }
                $pushOut = Join-Path $campDir "push-$name-$($u.repo)"
                $rcPush = Invoke-UaRaw @('push', $scoped, '--repo', $scratchRepo, '--base', $base, '--out', $pushOut)
                if ($rcPush -eq 7) {
                    # exit 7 is the branch-exists refusal ONLY if the branch exists — any other
                    # precondition refusal with exit 7 semantics is a failure, not a claim
                    $expectedBranch = "ua/wave-$($w.index)/$($pl.delta.packageName)-$($pl.delta.oldVersion)-to-$($pl.delta.newVersion)"
                    git -C $scratchRepo rev-parse --verify --quiet $expectedBranch *> $null
                    if ($LASTEXITCODE -ne 0) { throw "push $($u.repo) refused (exit 7) but branch '$expectedBranch' does not exist — an unexpected precondition refusal, not already-claimed" }
                    $unitEntry.action = 'already-claimed'
                    $unitEntry.evidence += @{ command = "ua push --repo $($u.repo)"; exitCode = 7; counts = @{ }; artifacts = @() }
                }
                elseif ($rcPush -ne 0) { throw "push $($u.repo) failed (exit $rcPush)" }
                else {
                $pushReport = Get-Content (Join-Path $pushOut 'push.v1.json') -Raw | ConvertFrom-Json
                $editTotal = ($pushReport.waves | Measure-Object -Property edits -Sum).Sum
                if ($editTotal -eq 0) {
                    # zero-edit waves (republish-required / external coordination) push NOTHING — no branch,
                    # no claim; the report itself is the coordination artifact (honest taxonomy)
                    $unitEntry.action = 'reported-only'
                    $unitEntry.evidence += @{ command = "ua push --repo $($u.repo)"; exitCode = $rcPush; counts = @{ edits = 0 }; artifacts = @() }
                }
                else {
                    $branch = "ua/wave-$($w.index)/$($pl.delta.packageName)-$($pl.delta.oldVersion)-to-$($pl.delta.newVersion)"
                    $unitEntry.action = 'pushed'
                    $unitEntry.branch = $branch
                    $unitEntry.evidence += @{ command = "ua push --repo $($u.repo)"; exitCode = $rcPush; counts = @{ edits = $editTotal }; artifacts = @() }
                }
                }
                $entry.units += $unitEntry
                # persist after EVERY unit: a later failure never orphans an earlier push's receipt
                $receipt = [ordered]@{ schemaVersion = 'campaign.v1'; status = 'in-progress'; note = 'WorkReceipt-shaped evidence (artifact-memory receipts are vault-resident by their contract — never committed); minting real receipts is a future slice'; waves = $waves + $entry }
                $receipt | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 (Join-Path $campDir 'campaign.v1.json')
            }
            $waves += $entry
        }
    }

    $receipt = [ordered]@{ schemaVersion = 'campaign.v1'; status = 'complete'; note = 'WorkReceipt-shaped evidence (artifact-memory receipts are vault-resident by their contract — never committed); minting real receipts is a future slice'; waves = $waves }
    $receipt | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 (Join-Path $campDir 'campaign.v1.json')

    # assertions: executable waves really landed (or already-claimed on re-run); coordination waves recorded
    $c = Get-Content (Join-Path $campDir 'campaign.v1.json') -Raw | ConvertFrom-Json
    $pushedCore = @($c.waves | Where-Object { $_.run -eq 'core' -and $_.wave -eq 2 } | ForEach-Object { $_.units } | Where-Object { $_.action -in @('pushed', 'already-claimed') })
    if ($pushedCore.Count -lt 2) { throw "campaign: both Core consumers must push (or be already-claimed), got $($pushedCore.Count)" }
    $producerWaves = @($c.waves | ForEach-Object { $_.units } | Where-Object { $_.repo -in @('Cassowary.Core', 'Cassowary.GroundOps') })
    if ($producerWaves.Count -gt 0 -and $producerWaves.Where({ $_.action -ne 'reported-only' }).Count -gt 0) { throw 'campaign: zero-edit producer/external waves must be reported-only (no branch, no claim)' }
    if ($producerWaves.Where({ $_.action -eq 'reported-only' }).Count -lt 2) { throw 'campaign: both producer/external waves must be recorded as reported-only' }
    Write-Host 'stage campaign: waves walked claim->apply->push (or coordination/already-claimed); receipts at out/campaign/campaign.v1.json'
}

switch ($Stage) {
    'pack'      { Invoke-StagePack }
    'restore'   { Invoke-StageRestore }
    'scan'      { Invoke-StageScan }
    'plan'      { Invoke-StagePlan }
    'campaign'  { Invoke-StageCampaign }
    'all'       { if ($TracemapCli -eq '') { throw '-TracemapCli is required for -Stage all' }; Invoke-StagePack; Invoke-StageRestore; Invoke-StageScan; Invoke-StagePlan; Invoke-StageCampaign }
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// SPEC-017: ua scan-estate — one command from a repos folder to a report. Absorbs runbook §4
// Steps 1–3: freshness precondition (north-star step 1) → SHA-cached tracemap scan loop (the
// scans folder IS the cache; no database) → sidecar bootstrap (generated, never hand-written)
// → ingest → plan → report. Every skip is visible with a reason and recorded in
// scan-estate.v1.json — silence is not safety; a skip never fails the run (--allow-stale waives
// freshness, F1/F2 excepted). The scanner invocation is an internal seam so the selftest can
// drive the whole wrapper offline and deterministically.
public static class ScanEstate
{
    public sealed class ScanRequest
    {
        public string RepoPath = "";
        public string ScanOutDir = "";
        public string HeadSha = "";
        public string[] Excludes = Array.Empty<string>();
        public string RepoName = ""; // SPEC-019 §3.2: the CANONICAL estate name — in worktree mode RepoPath is the satellite (its folder leaf is a ua hex name, never an identity)
        public bool IndexDepsJson; // pass-through flag for the real runner (stubs read RepoName/HeadSha)
    }

    public sealed class CacheMeta
    {
        [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "scan-estate-cache.v2"; // SPEC-019 §6: v1/unknown ⇒ rescan
        [JsonPropertyName("exclude")] public List<string> Exclude { get; set; } = new();
        [JsonPropertyName("tracemapSha256")] public string? TracemapSha256 { get; set; }
        [JsonPropertyName("staleScan")] public bool StaleScan { get; set; }
        [JsonPropertyName("depsFingerprint")] public string? DepsFingerprint { get; set; } // sorted (path,size,mtime) hash of bin/**/*.deps.json — make-style skip key
        [JsonPropertyName("indexDepsJson")] public bool IndexDepsJson { get; set; }
        [JsonPropertyName("buildFreshness")] public string? BuildFreshness { get; set; } // fresh|stale|none|fresh-by-build
        [JsonPropertyName("scanMode")] public string ScanMode { get; set; } = "in-place"; // in-place|worktree
        [JsonPropertyName("trunk")] public string? Trunk { get; set; } // RESOLVED, e.g. origin/main
    }

    public sealed class ManifestEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("status")] public string Status { get; set; } = "";
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("commitSha")] public string? CommitSha { get; set; }
        [JsonPropertyName("buildFreshness")] public string? BuildFreshness { get; set; } // SPEC-019 §5.2
    }

    // argv AFTER the assembly (the default runner prefixes the dll): scan --repo <abs> --out <dir>
    // + one --exclude <glob> per flag, in flag order. Pure — pinned by selftest.
    internal static string[] ComposeScanArgs(ScanRequest r)
    {
        var args = new List<string> { "scan", "--repo", r.RepoPath, "--out", r.ScanOutDir };
        foreach (var g in r.Excludes) { args.Add("--exclude"); args.Add(g); }
        return args.ToArray();
    }

    // The assembly is invoked directly (dotnet <dll>) — `dotnet run` recompiles per repo (runbook §4).
    // Output is captured and drained (not inherited): a child writing straight to the console would
    // bypass the --sanitized redactor. Progress comes from our own per-repo lines; failures carry rc.
    // BOTH streams must drain CONCURRENTLY: reading one to EOF before the other deadlocks a verbose
    // child once the undrained pipe fills (PR #2 Baz round 1) — the estate loop would hang mid-repo.
    static int DefaultRunner(ScanRequest r, string tracemapDll)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        psi.ArgumentList.Add(tracemapDll);
        foreach (var a in ComposeScanArgs(r)) psi.ArgumentList.Add(a);
        if (r.IndexDepsJson) psi.ArgumentList.Add("--index-deps-json");
        using var p = System.Diagnostics.Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        try { outTask.Wait(2000); errTask.Wait(2000); } catch { /* observation only — exit code is the contract */ }
        return p.ExitCode;
    }

    public static int Run(string reposRoot, string outDir, string? tracemapDll, string? scopePath,
        string? selfTeam, bool allowStale, string[] tracemapExcludes,
        string? deltaPackage, string? deltaOld, string? deltaNew, Func<ScanRequest, int>? scanner = null,
        bool updateCheckouts = false, bool worktreeMode = false, string? worktreeRoot = null, string? trunkOverride = null,
        int parallel = 2, bool build = false, bool indexDepsJson = false)
    {
        if (!Directory.Exists(reposRoot)) { Console.Error.WriteLine($"error: --repos-root directory not found: {reposRoot}"); return 1; }
        if (updateCheckouts && worktreeMode)
        { Console.Error.WriteLine("error: --update-checkouts and --worktree are mutually exclusive refresh modes — pick in-place refresh or isolated worktree scans"); return 1; }
        if (parallel < 1 || parallel > 64)
        { Console.Error.WriteLine("error: --parallel requires a value in 1..64"); return 1; }
        if (Push.Git(reposRoot, "--version", out _, out _) != 0)
        { Console.Error.WriteLine("error: git not found on PATH (required for freshness checks)"); return 1; }
        // scope is read ONCE, here: the parsed form filters this run and the exact bytes are
        // snapshotted for ingest (§4.4) — a file replaced mid-run can never split the evidence
        string? scopeText = null;
        EstateScope? scope = null;
        if (scopePath is not null)
        {
            if (!File.Exists(scopePath)) { Console.Error.WriteLine($"error: --scope file not found: {scopePath}"); return 1; }
            scopeText = File.ReadAllText(scopePath);
            var (parsedScope, scopeErr) = Scope.ParseAndValidate(scopeText, Path.GetFileName(scopePath));
            if (scopeErr is not null) { Console.Error.WriteLine($"error: {scopeErr}"); return 5; }
            scope = parsedScope;
        }
        if (tracemapDll is not null && !File.Exists(tracemapDll))
        { Console.Error.WriteLine($"error: --tracemap dll not found: {tracemapDll} (build tracemap once, pass bin/Release/net10.0/tracemap.dll)"); return 1; }
        string? dllHash = null;
        if (tracemapDll is not null)
            dllHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tracemapDll))).ToLowerInvariant();

        var scansDir = Path.Combine(Path.GetFullPath(outDir), "scans");
        Directory.CreateDirectory(scansDir);
        // ResolvedTrunk is per-repo in principle; the cache records the run's resolution basis per repo
        // via the value computed in pass 1 — captured here as the default (overridden per-repo below when
        // --trunk is set, since the override IS the resolution for every repo that accepts it).

        // ---- pass 1: eligibility, freshness, cache evaluation (fetches are the only side effect) ----
        var entries = new List<ManifestEntry>();
        var plannedScans = new List<(ManifestEntry Entry, string RepoPath, string HeadSha, bool StaleWaived, string? WorktreePath, string Freshness, string? Fingerprint, string Trunk)>();
        var freshDirs = new List<string>();
        var seenOrigins = new Dictionary<string, string>(StringComparer.Ordinal); // normalized origin -> first checkout name
        var rescuedTo = new Dictionary<string, string>(StringComparer.Ordinal); // Baz r2: rescue provenance rides the single canonical entry
        foreach (var repoPath in Directory.GetDirectories(Path.GetFullPath(reposRoot)).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal))
        {
            var name = Path.GetFileName(repoPath);
            ManifestEntry Skip(string reason)
            {
                Console.Error.WriteLine($"skip   {name} ({reason})");
                return new ManifestEntry { Name = name, Status = "skipped", Reason = reason };
            }
            if (name.StartsWith('.')) { entries.Add(new ManifestEntry { Name = name, Status = "ignored", Reason = "dot-directory" }); continue; }
            // symlinked children are visible skips, never scanned: a link redirects git/scanner work
            // outside the operator's repos root (PR #2 Baz round 2) — point scan-estate at the real
            // checkout's parent, or ingest the scan dir explicitly
            if (new DirectoryInfo(repoPath).LinkTarget is not null)
            { entries.Add(Skip("symlinked directory — pass the real checkout's parent (or the scan dir to ingest explicitly)")); continue; }
            if (!Scope.IsInScope(name, scope)) { Console.Error.WriteLine($"scope  {name} (out of scope by config)"); entries.Add(new ManifestEntry { Name = name, Status = "out-of-scope" }); continue; }
            if (Push.Git(repoPath, "rev-parse HEAD", out var headOut, out _) != 0)
            { entries.Add(Skip("not a git repository or no commits")); continue; }
            var headSha = headOut.Trim();
            if (Push.Git(repoPath, "remote", out var remotesOut, out _) != 0
                || !remotesOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Contains("origin"))
            { entries.Add(Skip("no origin remote")); continue; } // never bypassable (SPEC-017 §4.1 F2)
            // alternate-checkout dedupe: name-ordinal first checkout of an origin wins (ingest's
            // deliberate-combination rule, enforced before a scan is wasted). The URL comes from
            // config --get (the raw truth): `remote get-url` ECHOES THE REMOTE NAME when the URL is
            // unset/empty, which would silently dedupe every URL-less repo onto the key "origin"
            // (PR #2 Baz round 1 — proven by the selftest's empty-URL case).
            if (Push.Git(repoPath, "config --get remote.origin.url", out var urlOut, out _) != 0
                || Ingest.NormalizeRepoBasic(urlOut.Trim()).Length == 0)
            { entries.Add(Skip("origin remote has no usable URL (alternate-checkout identity unavailable — check git remote set-url origin")); continue; }
            var normOrigin = Ingest.NormalizeRepoBasic(urlOut.Trim());
            if (seenOrigins.TryGetValue(normOrigin, out var firstName))
            { entries.Add(Skip($"alternate checkout of {firstName} (same origin) — one scan per repo; pass scan dirs to ingest explicitly to combine deliberately")); continue; }
            seenOrigins[normOrigin] = name;

            // ---- refresh mode + freshness (SPEC-019 §3) ----
            string? fail = null;
            var trunk = "";
            string? worktreePath = null;
            var scanMode = worktreeMode ? "worktree" : "in-place";
            if (Push.Git(repoPath, "fetch origin", out _, out var fetchErr) != 0)
            { entries.Add(Skip($"fetch failed: {TrimReason(fetchErr)}")); continue; }
            // trunk resolution: --trunk override (validated), else origin/main -> origin/dev; recorded RESOLVED
            if (trunkOverride is not null)
            {
                if (Push.Git(repoPath, $"rev-parse --verify origin/{trunkOverride}", out _, out _) != 0)
                { entries.Add(Skip($"--trunk origin/{trunkOverride} does not exist on this repo")); continue; }
                trunk = $"origin/{trunkOverride}";
            }
            else if (Push.Git(repoPath, "rev-parse --verify origin/main", out _, out _) == 0) trunk = "origin/main";
            else if (Push.Git(repoPath, "rev-parse --verify origin/dev", out _, out _) == 0) trunk = "origin/dev";
            else { entries.Add(Skip("no origin/main or origin/dev branch (pass --trunk <branch> to override)")); continue; }

            if (worktreeMode)
            {
                // isolated scan: a satellite at the trunk tip — the operator's checkout is never touched.
                // All worktrees live under <worktreeRoot>/ua/ (the shared-root namespace; SPEC-019 §3.2).
                var wtRoot = Path.Combine(Path.GetFullPath(worktreeRoot ?? Path.Combine(outDir, ".worktrees")), "ua");
                SweepWorktreeLeftovers(repoPath, wtRoot, name);
                // The satellite is created INSIDE the bounded worker (Codex P1): materializing N worktrees
                // in the sequential pass would defeat --parallel's disk bound. The sha needs no satellite:
                Push.Git(repoPath, $"rev-parse {trunk}", out var tipWt, out _);
                headSha = tipWt.Trim(); // the scanned sha is the trunk tip, not the checkout HEAD
                worktreePath = Path.Combine(wtRoot, $"{name}-PENDING"); // real path minted by the worker
                // F5/F6 do not apply: the satellite is pristine by construction; staleScan stays false
            }
            else if (updateCheckouts)
            {
                // Joe's script: dirty => rescue branch + commit (never silent, never dropped); then trunk + ff-only.
                if (Push.Git(repoPath, "status --porcelain", out var dirtyOut, out _) != 0 || dirtyOut.Trim().Length > 0)
                {
                    if (dirtyOut.Trim().Length > 0)
                    {
                        var rescueBranch = $"ua/rescue-{headSha[..8]}"; // 8 chars — one more than display Sha7, uniqueness at a glance
                        var rcB = Push.Git(repoPath, $"checkout -b {rescueBranch}", out _, out _);
                        var rcA = rcB == 0 ? Push.Git(repoPath, "add -A", out _, out _) : 1;
                        var commitErr = ""; var rcC = rcA == 0 ? Push.Git(repoPath, "commit -m \"ua scan-estate rescue: preserve dirty working tree before refresh\"", out _, out commitErr) : 1;
                        if (rcB != 0 || rcA != 0 || rcC != 0)
                        { entries.Add(Skip($"dirty-tree rescue failed ({TrimReason(commitErr ?? "git error")} — identity configured? ua never invents one)")); continue; }
                        Console.Error.WriteLine($"rescue {name}: stray work committed to {rescueBranch} (find it there — never dropped)");
                        rescuedTo[name] = rescueBranch; // ONE canonical entry per repo (Baz r2): the pending entry below carries the rescue provenance
                    }
                }
                var localTrunk = trunk["origin/".Length..];
                if (Push.Git(repoPath, $"rev-parse --verify {localTrunk}", out _, out _) != 0)
                {
                    if (Push.Git(repoPath, $"checkout -b {localTrunk} --track {trunk}", out _, out var coErr) != 0)
                    { entries.Add(Skip($"checkout trunk failed: {TrimReason(coErr)}")); continue; }
                }
                else if (Push.Git(repoPath, $"checkout {localTrunk}", out _, out var coErr) != 0)
                { entries.Add(Skip($"checkout trunk failed: {TrimReason(coErr)}")); continue; }
                Push.Git(repoPath, $"rev-list --count {trunk}..{localTrunk}", out var aheadOut, out _);
                if (int.TryParse(aheadOut.Trim(), out var ahead) && ahead > 0)
                { entries.Add(Skip("local commits ahead of the trunk tip — refresh refused rather than merge (rebase deliberately and re-run)")); continue; }
                if (Push.Git(repoPath, $"merge --ff-only {trunk}", out _, out var ffErr) != 0)
                { entries.Add(Skip($"fast-forward failed: {TrimReason(ffErr)}")); continue; }
                Push.Git(repoPath, "rev-parse HEAD", out var newHead, out _);
                headSha = newHead.Trim();
            }
            else
            {
                // F5/F6, always evaluated: the outcome feeds the staleScan cache rule under --allow-stale
                Push.Git(repoPath, $"rev-parse {trunk}", out var tipOut, out _);
                var tip = tipOut.Trim();
                if (tip != headSha)
                    fail = $"HEAD {Sha7(headSha)} is not at {trunk} tip {Sha7(tip)}";
                else if (Push.Git(repoPath, "status --porcelain", out var statusOut, out _) != 0 || statusOut.Trim().Length > 0)
                    fail = "working tree not clean (uncommitted or untracked changes; --allow-stale to scan anyway)";
            }
            var staleWaived = fail is not null;
            if (fail is not null)
            {
                if (!allowStale) { entries.Add(Skip(fail)); continue; }
                Console.Error.WriteLine($"stale  {name} ({fail}; --allow-stale)");
            }

            // SHA + metadata cache (SPEC-017 §4.2)
            var scanDir = Path.Combine(scansDir, name);
            var manifestPath = Path.Combine(scanDir, "scan-manifest.json");
            var factsPath = Path.Combine(scanDir, "facts.ndjson");
            var hasManifest = File.Exists(manifestPath);
            var hasFacts = File.Exists(factsPath);
            if (hasFacts && !hasManifest)
            {
                Console.Error.WriteLine($"warning: {name}: cached scan has facts.ndjson but no scan-manifest.json — remove or complete it (skipped)");
                entries.Add(Skip("cached scan has facts.ndjson but no scan-manifest.json — remove or complete it"));
                continue;
            }
            // build freshness + fingerprint (in-place: computed here; worktree: none now, recorded after build/scan)
            var freshnessNow = worktreeMode ? (build && indexDepsJson ? "fresh-by-build" : "none") : ComputeBuildFreshness(repoPath, headSha, indexDepsJson);
            var fingerprintNow = worktreeMode ? null : DepsFingerprint(repoPath); // recorded from the worktree at scan time instead
            CacheMeta? meta = null;
            if (hasManifest && hasFacts && File.Exists(Path.Combine(scanDir, "scan-estate.json")))
            {
                try
                {
                    var candidate = JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(Path.Combine(scanDir, "scan-estate.json")), JsonOpts);
                    // hand-edited/foreign metadata must invalidate, never crash or silently reuse
                    // (PR #2 Baz round 2): wrong schemaVersion or a null exclude list ⇒ rescan
                    if (candidate is { SchemaVersion: "scan-estate-cache.v2", Exclude: not null })
                        meta = candidate;
                }
                catch (JsonException) { meta = null; } // broken metadata ⇒ rescan (safe default)
            }
            var reuse = hasManifest && hasFacts && meta is not null
                && CachedCommitSha(manifestPath) == headSha
                && meta.Exclude.SequenceEqual(tracemapExcludes)
                && (tracemapDll is null || meta.TracemapSha256 == dllHash) // hash compared only when --tracemap given
                && meta.StaleScan == staleWaived // a dirty/behind scan never speaks for a clean run at the same SHA
                && meta.IndexDepsJson == indexDepsJson
                && meta.ScanMode == (worktreeMode ? "worktree" : "in-place")
                && meta.Trunk == trunk
                && Ord(meta.BuildFreshness) == Ord(freshnessNow)
                && (worktreeMode || Ord(meta.DepsFingerprint) == Ord(fingerprintNow)) // in-place: a rebuild with same HEAD changes the fingerprint ⇒ rescan (SPEC-019 §6)
                && !build; // Codex P2: --build is an explicit freshness-seeking action — it always executes (a pre-build cache match must not skip the requested build)
            if (reuse)
            {
                Console.Error.WriteLine($"reuse  {name} (HEAD unchanged: {Sha7(headSha)})");
                entries.Add(new ManifestEntry { Name = name, Status = "reused", CommitSha = headSha, BuildFreshness = meta!.BuildFreshness });
                freshDirs.Add(scanDir);
                continue;
            }
            var pending = new ManifestEntry { Name = name, Status = "scanned", CommitSha = headSha, BuildFreshness = freshnessNow }; // confirmed (or skipped) in pass 2
            if (rescuedTo.TryGetValue(name, out var rb)) pending.Reason = $"dirty tree rescued to {rb} before refresh";
            entries.Add(pending);
            plannedScans.Add((pending, repoPath, headSha, staleWaived, worktreePath, freshnessNow, fingerprintNow, trunk));
        }

        // ---- scan precondition: a needed scan with neither dll nor seam is a typed error ----
        if (plannedScans.Count > 0 && tracemapDll is null && scanner is null)
        {
            Console.Error.WriteLine($"error: {plannedScans.Count} repo(s) need a scan — pass --tracemap <path-to-tracemap.dll>");
            return 1;
        }

        // ---- pass 2: execute scans in parallel (SPEC-019 §2: decisions were all made sequentially
        //      above in name-ordinal order; ONLY the work parallelizes, so outputs are N-independent) ----
        var gate = new object();
        var workers = plannedScans.Select(unit => (Action)(() =>
        {
            var (entry, repoPath, headSha, staleWaived, worktreePath, freshness, fingerprint, resolvedTrunk) = unit;
            var scanDir = Path.Combine(scansDir, entry.Name);
            var wtRoot = Path.GetDirectoryName(worktreePath ?? ""); // namespace dir when in worktree mode
            var satellite = worktreePath is not null ? Path.Combine(wtRoot ?? "", $"{entry.Name}-{Guid.NewGuid().ToString("N")[..8]}") : null;
            try
            {
                if (worktreePath is not null)
                {
                    if (Push.Git(repoPath, $"worktree add --detach \"{satellite}\" {resolvedTrunk}", out _, out var wtErr) != 0)
                    {
                        lock (gate)
                        {
                            entry.Status = "skipped";
                            entry.Reason = $"worktree add failed: {TrimReason(wtErr)}";
                            Console.Error.WriteLine($"skip   {entry.Name} ({entry.Reason})");
                        }
                        return;
                    }
                }
                var scanTarget = satellite ?? repoPath; // the REAL satellite — worktreePath is only the PENDING marker from planning (Codex P1 r2)
                if (satellite is not null && build)
                {
                    {
                        // provably-fresh deps.json: compile the satellite at the scanned commit (§3.2)
                        var psiB = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                        psiB.ArgumentList.Add("build");
                        psiB.ArgumentList.Add(satellite);
                        psiB.ArgumentList.Add("--nologo");
                        using var pB = System.Diagnostics.Process.Start(psiB)!;
                        _ = pB.StandardOutput.ReadToEndAsync(); _ = pB.StandardError.ReadToEndAsync();
                        pB.WaitForExit();
                        if (pB.ExitCode != 0)
                        {
                            lock (gate)
                            {
                                entry.Status = "skipped";
                                entry.Reason = "build failed (--build refuses to scan without fresh evidence)";
                                Console.Error.WriteLine($"skip   {entry.Name} (build failed; worktree discarded)");
                            }
                            return;
                        }
                        fingerprint = DepsFingerprint(satellite); // recorded from the tree actually scanned (§6)
                        freshness = indexDepsJson ? "fresh-by-build" : freshness;
                    }
                }
                else if (build)
                {
                    // in-place build: only repos that passed freshness/rescue reach here
                    var psiB = new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    psiB.ArgumentList.Add("build");
                    psiB.ArgumentList.Add(repoPath);
                    psiB.ArgumentList.Add("--nologo");
                    using var pB = System.Diagnostics.Process.Start(psiB)!;
                    _ = pB.StandardOutput.ReadToEndAsync(); _ = pB.StandardError.ReadToEndAsync();
                    pB.WaitForExit();
                    if (pB.ExitCode != 0)
                    {
                        lock (gate)
                        {
                            entry.Status = "skipped";
                            entry.Reason = "build failed (--build refuses to scan without fresh evidence)";
                            Console.Error.WriteLine($"skip   {entry.Name} (build failed)");
                        }
                        return;
                    }
                    fingerprint = DepsFingerprint(repoPath);
                    freshness = indexDepsJson ? ComputeBuildFreshness(repoPath, headSha, true) : freshness;
                }
                ClearLeftovers(scansDir, entry.Name);
                var tmp = Path.Combine(scansDir, "." + entry.Name + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
                var request = new ScanRequest { RepoPath = scanTarget, ScanOutDir = tmp, HeadSha = headSha, Excludes = tracemapExcludes, RepoName = entry.Name, IndexDepsJson = indexDepsJson };
                var rc = scanner is not null ? scanner(request) : DefaultRunner(request, tracemapDll!);
                if (satellite is not null)
                {
                    // satellite lifecycle: remove what we created, only what we created (§3.2 safety rules)
                    Push.Git(repoPath, $"worktree remove --force \"{satellite}\"", out _, out _);
                    Push.Git(repoPath, "worktree prune", out _, out _);
                }
                if (rc != 0 || !File.Exists(Path.Combine(tmp, "facts.ndjson")) || !File.Exists(Path.Combine(tmp, "scan-manifest.json")))
                {
                    TryDeleteDir(tmp);
                    lock (gate)
                    {
                        entry.Status = "skipped";
                        entry.Reason = rc == 0 ? "scanner exited 0 but produced no facts.ndjson/scan-manifest.json" : $"tracemap exited {rc}";
                        Console.Error.WriteLine($"skip   {entry.Name} ({entry.Reason})");
                    }
                    return;
                }
                var meta = new CacheMeta
                {
                    Exclude = tracemapExcludes.ToList(), TracemapSha256 = dllHash, StaleScan = staleWaived,
                    DepsFingerprint = fingerprint, IndexDepsJson = indexDepsJson, BuildFreshness = freshness,
                    ScanMode = worktreePath is not null ? "worktree" : "in-place",
                    Trunk = resolvedTrunk, // recorded RESOLVED so --trunk main == auto-detect (§6)
                };
                File.WriteAllText(Path.Combine(tmp, "scan-estate.json"),
                    JsonSerializer.Serialize(meta, JsonOpts).ReplaceLineEndings("\n") + "\n");
                var old = Path.Combine(scansDir, "." + entry.Name + ".old-" + Guid.NewGuid().ToString("N")[..8]);
                try
                {
                    if (Directory.Exists(scanDir)) Directory.Move(scanDir, old);
                    try { Directory.Move(tmp, scanDir); }
                    catch { if (Directory.Exists(old)) Directory.Move(old, scanDir); throw; }
                    if (Directory.Exists(old)) TryDeleteDir(old);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"warning: {entry.Name}: scan swap failed ({ex.Message}) — cached scan left as-is");
                    TryDeleteDir(tmp);
                    lock (gate)
                    {
                        entry.Status = "skipped";
                        entry.Reason = $"scan swap failed: {TrimReason(ex.Message)}";
                    }
                    return;
                }
                lock (gate)
                {
                    entry.BuildFreshness = freshness;
                    Console.Error.WriteLine($"scan   {entry.Name} ({Sha7(headSha)})");
                    freshDirs.Add(scanDir);
                }
            }
            catch (Exception ex)
            {
                lock (gate)
                {
                    entry.Status = "skipped";
                    entry.Reason = $"scan worker failed: {TrimReason(ex.Message)}";
                    Console.Error.WriteLine($"skip   {entry.Name} ({entry.Reason})");
                }
            }
            finally
            {
                if (satellite is not null && Directory.Exists(satellite))
                {
                    Push.Git(repoPath, $"worktree remove --force \"{satellite}\"", out _, out _); // every exit path cleans its own satellite (Codex P1)
                    Push.Git(repoPath, "worktree prune", out _, out _);
                }
            }
        })).ToArray();
        if (workers.Length > 0 && parallel > 1)
            System.Threading.Tasks.Parallel.Invoke(new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = parallel }, workers);
        else
            foreach (var w in workers) w();

        freshDirs.Sort(StringComparer.Ordinal); // SPEC-019 §2: downstream inputs are name-ordinal regardless of worker completion order

        // Baz r3: rescue provenance rides EVERY terminal state (reused entries never reached the
        // pending assignment; failure paths overwrote it) — apply once, just before the durable write
        foreach (var e2 in entries)
            if (rescuedTo.TryGetValue(e2.Name, out var rb2) && (e2.Reason is null || !e2.Reason.Contains(rb2)))
                e2.Reason = e2.Reason is null ? $"dirty tree rescued to {rb2} before refresh" : $"{e2.Reason}; dirty tree rescued to {rb2} before refresh";

        // ---- run manifest: written before anything downstream can fail (§4.4) ----
        var manifestFile = Path.Combine(Path.GetFullPath(outDir), "scan-estate.v1.json");
        File.WriteAllText(manifestFile, JsonSerializer.Serialize(
            new { schemaVersion = "scan-estate.v1", repos = entries }, WriteOpts).ReplaceLineEndings("\n") + "\n");

        // ---- empty fresh set: typed error naming the composition, no sidecars ----
        if (freshDirs.Count == 0)
        {
            var nSkipped = entries.Count(e => e.Status == "skipped");
            var nOos = entries.Count(e => e.Status == "out-of-scope");
            var nIgn = entries.Count(e => e.Status == "ignored");
            Console.Error.WriteLine($"error: no repos to ingest (0 fresh: {nSkipped} skipped, {nOos} out-of-scope, {nIgn} ignored — see {manifestFile})");
            return 1;
        }

        // ---- sidecar bootstrap (§4.3): existing files are operator data, never overwritten ----
        var deltaPath = Path.Combine(Path.GetFullPath(outDir), "delta.json");
        if (File.Exists(deltaPath))
        {
            var ignoredDeltaFlags = new[] { ("--delta-package", deltaPackage), ("--delta-old", deltaOld), ("--delta-new", deltaNew) }
                .Where(t => t.Item2 is not null).Select(t => t.Item1).ToList();
            if (ignoredDeltaFlags.Count > 0) // SPEC-017 §4.3: EVERY --delta-* flag warns and loses, never silently
                Console.Error.WriteLine($"warning: existing delta.json wins ({string.Join(" ", ignoredDeltaFlags)} ignored) — the real delta IS the real use case");
        }
        else
        {
            var missing = new[] { ("--delta-package", deltaPackage), ("--delta-old", deltaOld), ("--delta-new", deltaNew) }
                .Where(t => t.Item2 is null).Select(t => t.Item1).ToList();
            if (missing.Count > 0)
            { Console.Error.WriteLine($"error: missing {string.Join(" ", missing)} — required to template delta.json (or place your own delta.json in the out dir)"); return 1; }
            File.WriteAllText(deltaPath, JsonSerializer.Serialize(new DeltaFile
            {
                Version = "package-delta.v1",
                SourceRepo = "https://example.invalid/x.git",
                SourceCommitSha = new string('0', 40),
                Changes = new List<DeltaChange> { new() { Id = "bootstrap", PackageName = deltaPackage!, Ecosystem = "nuget", ChangeType = "updated", OldVersion = deltaOld!, NewVersion = deltaNew! } },
            }, WriteOpts).ReplaceLineEndings("\n") + "\n");
            Console.Error.WriteLine($"note: templated delta.json ({deltaPackage} {deltaOld} → {deltaNew}) — swap in your real delta when ready");
        }
        DeltaFile delta;
        try { delta = JsonSerializer.Deserialize<DeltaFile>(File.ReadAllText(deltaPath), JsonOpts) ?? throw new JsonException(); }
        catch (JsonException ex) { Console.Error.WriteLine($"error: malformed delta file {deltaPath}: {ex.Message}"); return 1; }
        if (delta.Changes is null)
        { Console.Error.WriteLine($"error: malformed delta file {deltaPath}: changes is null — a delta with no changes must not exist at all"); return 1; } // valid JSON, null list (PR #2 Baz round 2): typed error, never an uncaught NRE
        if (delta.Changes.Count != 1)
        { Console.Error.WriteLine($"error: delta.json must contain exactly one change (found {delta.Changes.Count}); single-change planning only in V0 (SPEC-003 §1a)"); return 1; }
        var deltaPkgName = delta.Changes[0].PackageName;
        if (deltaPackage is not null && deltaPackage != deltaPkgName)
        { Console.Error.WriteLine($"error: --delta-package {deltaPackage} differs from the existing delta.json target {deltaPkgName}"); return 1; }

        var producerPath = Path.Combine(Path.GetFullPath(outDir), "producer-evidence.v0.json");
        if (!File.Exists(producerPath))
        {
            File.WriteAllText(producerPath, JsonSerializer.Serialize(new ProducerEvidence
            {
                SchemaVersion = "producer-evidence.v0",
                ExternalPackages = new List<ExternalPackage> { new() { PackageId = deltaPkgName } },
            }, WriteOpts).ReplaceLineEndings("\n") + "\n");
            Console.Error.WriteLine($"note: templated producer-evidence.v0 (externalPackages lists the delta target {deltaPkgName}) — extend it for other public-feed packages");
        }
        else
            Console.Error.WriteLine("note: existing producer-evidence.v0 reused — edit externalPackages if the delta target changed");

        var ownershipPath = Path.Combine(Path.GetFullPath(outDir), "ownership.v0.json");
        if (File.Exists(ownershipPath))
        {
            OwnershipFile existing;
            try { existing = JsonSerializer.Deserialize<OwnershipFile>(File.ReadAllText(ownershipPath), JsonOpts) ?? throw new JsonException(); }
            catch (JsonException ex) { Console.Error.WriteLine($"error: malformed ownership file {ownershipPath}: {ex.Message}"); return 1; }
            if (existing.SchemaVersion != "ownership.v0")
            { Console.Error.WriteLine($"error: unsupported schemaVersion '{existing.SchemaVersion}' (expected ownership.v0)"); return 1; }
            if (selfTeam is not null && selfTeam != existing.SelfTeamId)
            { Console.Error.WriteLine($"error: --self {selfTeam} differs from the existing ownership selfTeamId {existing.SelfTeamId}"); return 1; }
            // update refuses --self by design; scan-estate did the comparison itself (SPEC-017 §4.3)
            var rcU = Ownership.Update(freshDirs.ToArray(), null, ownershipPath, "new-self", null, "", ownershipPath);
            if (rcU != 0) return rcU;
        }
        else
        {
            if (selfTeam is null)
            { Console.Error.WriteLine("error: --self <teamId> is required to bootstrap ownership.v0.json (teams are never invented)"); return 1; }
            var rcI = Ownership.Init(freshDirs.ToArray(), null, selfTeam, "all-self", null, ownershipPath);
            if (rcI != 0) return rcI;
        }

        // ---- chain: ingest (explicit dirs, all sidecars explicit) → plan → report (§4.4) ----
        // Ingest consumes the SNAPSHOT of the scope bytes this run actually filtered with (read
        // once at the top): a file replaced mid-run cannot split the run's evidence between two
        // scope versions (PR #2 Baz round 2). The snapshot stays in the out dir as provenance.
        var scopePathForIngest = scopePath;
        if (scopePath is not null && scopeText is not null)
        {
            scopePathForIngest = Path.Combine(Path.GetFullPath(outDir), "scope.snapshot.json");
            File.WriteAllText(scopePathForIngest, scopeText);
        }
        var fixtureDir = Path.Combine(Path.GetFullPath(outDir), "fixture");
        var rcIngest = Ingest.Run(freshDirs.ToArray(), fixtureDir, producerPath, ownershipPath, deltaPath, null, scopePathForIngest);
        if (rcIngest != 0) return rcIngest;
        var plan = Program.LoadEngine(fixtureDir).BuildPlan();
        Program.WriteArtifactFile(Path.Combine(Path.GetFullPath(outDir), "plan.json"), Canonical.Write(plan));
        Program.WriteArtifactFile(Path.Combine(Path.GetFullPath(outDir), "report.md"), Report.Render(plan));

        var nScan = entries.Count(e => e.Status == "scanned");
        var nReuse = entries.Count(e => e.Status == "reused");
        var nSkip = entries.Count(e => e.Status == "skipped");
        var nOos2 = entries.Count(e => e.Status == "out-of-scope");
        var nIgn2 = entries.Count(e => e.Status == "ignored");
        Console.Error.WriteLine($"estate: {nScan} scanned, {nReuse} reused, {nSkip} skipped, {nOos2} out-of-scope, {nIgn2} ignored → {Path.Combine(Path.GetFullPath(outDir), "report.md")}");
        if (nSkip > 0) Console.Error.WriteLine($"note: skipped repos are listed in {manifestFile}");
        return 0;
    }

    static string Sha7(string sha) => sha.Length <= 7 ? sha : sha[..7];
    static string Ord(string? s) => s ?? "";

    // SPEC-019 §5.2: OLDEST bin/**/*.deps.json mtime (min — conservative) vs the HEAD COMMITTER date, boundary >=.
    static string ComputeBuildFreshness(string repoPath, string headSha, bool indexDepsJson)
    {
        if (!indexDepsJson) return "none";
        var (files, truncated) = DepsManifests(repoPath);
        if (files.Count == 0 && !truncated) return "none";
        if (truncated) return "stale"; // fail closed: a discarded suffix could hold the OLDEST manifest (Codex P1 r3)
        if (Push.Git(repoPath, "log -1 --format=%ct HEAD", out var ctOut, out _) != 0) return "stale"; // no committer date ⇒ cannot certify fresh
        if (!long.TryParse(ctOut.Trim(), out var commitTime)) return "stale";
        var oldest = files.Min(f => new FileInfo(f).LastWriteTimeUtc.Ticks);
        return new DateTimeOffset(oldest, TimeSpan.Zero).ToUnixTimeSeconds() >= commitTime ? "fresh" : "stale";
    }

    // Bounded manual walk (Codex P2 r3): Take() bounds matches, NOT the traversal — the walk itself
    // carries the budget. Truncation FAILS CLOSED everywhere it flows (freshness => stale; fingerprint
    // => "!truncated" sentinel that never equals a real hash).
    const int MaxDepsManifests = 2048;
    const int MaxWalkedDirs = 50_000;
    static (List<string> Files, bool Truncated) DepsManifests(string root)
    {
        if (!Directory.Exists(root)) return (new List<string>(), false);
        var found = new List<string>();
        var truncated = false;
        var stack = new Stack<(string Dir, bool InBin)>();
        stack.Push((root, false));
        var walked = 0;
        while (stack.Count > 0)
        {
            if (++walked > MaxWalkedDirs) { truncated = true; break; }
            var (dir, inBin) = stack.Pop();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception) when (ex581(dir)) { continue; }
            foreach (var e in entries)
            {
                var name = Path.GetFileName(e);
                if (Directory.Exists(e))
                {
                    if (name is ".git" or "node_modules" or ".vs" or "packages") continue; // never productive
                    stack.Push((e, inBin || name.Equals("bin", StringComparison.OrdinalIgnoreCase)));
                }
                else if (inBin && name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(e);
                    if (found.Count > MaxDepsManifests) { truncated = true; break; }
                }
            }
            if (truncated) break;
        }
        if (truncated)
            Console.Error.WriteLine($"warning: deps.json discovery under {root} exceeded its budget ({MaxDepsManifests} manifests / {MaxWalkedDirs} dirs) — evidence treated conservatively (stale; fingerprint sentinel)");
        return (found.OrderBy(f => f, StringComparer.Ordinal).ToList(), truncated);
        static bool ex581(string d) => true; // unreadable directory: skip, never abort the estate
    }

    // make-style skip key: metadata only (path, size, mtime), never content reads (SPEC-019 §6)
    static string? DepsFingerprint(string root)
    {
        var (files, truncated) = DepsManifests(root);
        if (truncated) return "!truncated"; // never equals a real hash ⇒ no false cache reuse (Codex P1 r3)
        if (files.Count == 0) return null;
        using var sha = System.Security.Cryptography.SHA256.Create();
        var parts = files.Select(f => { var fi = new FileInfo(f); return $"{fi.FullName}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}"; });
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", parts));
        var hash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        return hash;
    }

    // crash-leftover sweep: exact-pattern children for THIS repo under <root>/ua/ only — never beyond the namespace (§3.2)
    static void SweepWorktreeLeftovers(string repoPath, string uaRoot, string name)
    {
        if (!Directory.Exists(uaRoot)) return;
        // OWNERSHIP-CHECKED sweep (Baz r2): a pattern-matching directory is ours only if git itself
        // lists it as this repo's worktree — a foreign actor creating <name>-<8hex> inside ua/ survives.
        Push.Git(repoPath, "worktree list --porcelain", out var wl, out _);
        var owned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in wl.Split('\n'))
            if (line.StartsWith("worktree ", StringComparison.Ordinal))
                owned.Add(Path.GetFullPath(line["worktree ".Length..].Trim()));
        foreach (var d in Directory.GetDirectories(uaRoot).Where(d =>
            Path.GetFileName(d).StartsWith(name + "-", StringComparison.Ordinal)
            && Path.GetFileName(d).Length == name.Length + 1 + 8
            && Path.GetFileName(d)[(name.Length + 1)..].All(Uri.IsHexDigit)
            && owned.Contains(Path.GetFullPath(d))).ToList())
        {
            Push.Git(repoPath, "worktree remove --force \"" + d + "\"", out _, out _);
            Push.Git(repoPath, "worktree prune", out _, out _);
            if (Directory.Exists(d)) TryDeleteDir(d); // dir survived an admin-less removal
        }
    }

    // Reason strings reach BOTH the console and scan-estate.v1.json — a file artifact --sanitized
    // never transforms by design — so credential-bearing URL userinfo is scrubbed HERE, at the
    // source, unconditionally (PR #2 Baz round 1 + Codex P1 round 2: git fetch stderr happily
    // echoes the remote URL, and PATs ride as USERNAME-ONLY userinfo — https://PAT@host — no colon).
    internal static string TrimReason(string text)
    {
        var firstLine = (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        var scrubbed = System.Text.RegularExpressions.Regex.Replace(firstLine,
            @"([a-zA-Z][a-zA-Z0-9+.\-]*://)[^\s/@]+@", "$1***@"); // scheme://ANY-userinfo@ (token or user:pass)
        scrubbed = System.Text.RegularExpressions.Regex.Replace(scrubbed,
            @"\b[^\s/@]+@(?=[a-zA-Z0-9._\-]+[.:])", "***@"); // scheme-less userinfo@host — dotted OR scp-style `host:`; `_` included (Redact.cs accepts it in hosts — PR #2 Codex r4)
        // A masked SINGLE-LABEL host (`***@ghe:`) would still name the internal alias, and it rides
        // both the manifest and sanitized stderr (Redact.UrlRe doesn't parse the masked form — PR #2
        // Baz r4); single-label hosts are exactly what the redactor treats as sensitive, so mask them
        // too — in scp form (terminated by :) AND scheme form (terminated by / ? # or end — PR #2
        // Codex r5). Dotted hosts keep their diagnostic value (the credential was the userinfo).
        scrubbed = System.Text.RegularExpressions.Regex.Replace(scrubbed,
            @"(\*\*\*@)(?![a-zA-Z0-9_\-]*\.)([a-zA-Z0-9_\-]+)(?=[:/?#]|$)", "$1***");
        return scrubbed.Length <= 200 ? scrubbed : scrubbed[..200];
        return scrubbed.Length <= 200 ? scrubbed : scrubbed[..200];
    }

    static string? CachedCommitSha(string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.TryGetProperty("commitSha", out var sha) ? sha.GetString() : null;
        }
        catch { return null; }
    }

    // stale .tmp/.old swap siblings for one repo — an interrupted run must not strand discoverable garbage
    static void ClearLeftovers(string scansDir, string name)
    {
        foreach (var prefix in new[] { "." + name + ".tmp-", "." + name + ".old-" })
            foreach (var d in Directory.GetDirectories(scansDir).Where(d => Path.GetFileName(d).StartsWith(prefix, StringComparison.Ordinal)))
                TryDeleteDir(d);
    }

    static void TryDeleteDir(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* racing cleanup */ }
            Directory.Delete(dir, true);
        }
        catch (Exception ex) { Console.Error.WriteLine($"warning: could not clean {dir} ({ex.Message}) — remove it manually"); }
    }

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}

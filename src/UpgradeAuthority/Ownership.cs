using System.Text.Json;
using System.Text.Json.Nodes;
namespace UpgradeAuthority;

// SPEC-014: ua ownership — estate-scale ownership.v0 generation. init creates the file from the
// scan set; update extends an existing file with manual assignments sacred. Teams are never
// invented: they come from flags (a human decision) or the existing file. Identity is ingest's
// own enumeration (one algorithm); mirror mappings resolve "already present" as the planner does.
public static class Ownership
{
    public enum Mode { AllSelf, Team, Unassigned }
    public enum NewMode { NewSelf, NewTeam, NewUnassigned }

    public static int Init(string[] scanDirs, string? scansRoot, string selfTeam, string modeFlag, string? teamArg, string outFile, string? scopePath = null)
    {
        var (mode, err) = ParseMode(modeFlag, teamArg, selfTeam);
        if (err is not null) { Console.Error.WriteLine($"error: {err}"); return 1; }
        var (scope, scopeRc) = Scope.LoadForCli(scopePath);
        if (scopeRc != 0) return scopeRc;
        WarnExplicitOutOfScope(scanDirs, scope);
        var dirs = ResolveDirs(scanDirs, scansRoot, scope);
        if (dirs is null) return 1;
        var (repos, enumErr) = Ingest.EnumerateRepoKeysChecked(dirs);
        if (enumErr is not null) { Console.Error.WriteLine($"error: {enumErr}"); return 1; } // uningestable scans never yield ownership files
        if (mode == Mode.Unassigned)
        {
            foreach (var r in repos) Console.Error.WriteLine($"unassigned: {r}"); // the fill-in checklist
            Console.Error.WriteLine($"note: {repos.Count} repos discovered; none assigned (--unassigned) — planner treats absent repos as ownership-unknown");
        }
        var ownerships = mode switch
        {
            Mode.AllSelf => repos.Select(r => new OwnershipEntry { Repo = r, Team = selfTeam }).ToList(),
            Mode.Team => repos.Select(r => new OwnershipEntry { Repo = r, Team = teamArg! }).ToList(),
            _ => new List<OwnershipEntry>(),
        };
        Write(outFile, selfTeam, ownerships);
        Console.Error.WriteLine($"ownership: {repos.Count} repos enumerated, {ownerships.Count} assigned -> {outFile}");
        return 0;
    }

    public static int Update(string[] scanDirs, string? scansRoot, string existingFile, string newModeFlag, string? newTeamArg, string selfTeamOverride, string outFile, string? scopePath = null)
    {
        if (!File.Exists(existingFile)) { Console.Error.WriteLine($"error: --existing file not found: {existingFile}"); return 1; }
        OwnershipFile existing;
        try { existing = JsonSerializer.Deserialize<OwnershipFile>(File.ReadAllText(existingFile), JsonOpts) ?? throw new JsonException(); }
        catch (JsonException ex) { Console.Error.WriteLine($"error: malformed ownership file {existingFile}: {ex.Message}"); return 1; }
        if (existing.SchemaVersion != "ownership.v0") { Console.Error.WriteLine($"error: unsupported schemaVersion '{existing.SchemaVersion}' (expected ownership.v0)"); return 1; }

        var selfTeam = selfTeamOverride.Length > 0 ? selfTeamOverride : existing.SelfTeamId;
        var (mode, err) = ParseNewMode(newModeFlag, newTeamArg, selfTeam);
        if (err is not null) { Console.Error.WriteLine($"error: {err}"); return 1; }

        var (scope, scopeRc) = Scope.LoadForCli(scopePath);
        if (scopeRc != 0) return scopeRc;
        WarnExplicitOutOfScope(scanDirs, scope);
        var dirs = ResolveDirs(scanDirs, scansRoot, scope);
        if (dirs is null) return 1;
        var (discovered, enumErr2) = Ingest.EnumerateRepoKeysChecked(dirs);
        if (enumErr2 is not null) { Console.Error.WriteLine($"error: {enumErr2}"); return 1; }

        // "already present" resolves through the file's mirrors exactly as the planner does (SPEC-001 §3):
        // alias-keyed entries and canonical discovered forms are the SAME repo.
        var mirrorToCanonical = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in existing.Mirrors)
        {
            var canonical = Ingest.NormalizeRepoBasic(m.Canonical);
            mirrorToCanonical[Ingest.NormalizeRepoBasic(m.Canonical)] = canonical;
            foreach (var a in m.Aliases) mirrorToCanonTryAdd(mirrorToCanonical, Ingest.NormalizeRepoBasic(a), canonical);
        }
        static void mirrorToCanonTryAdd(Dictionary<string, string> d, string alias, string canonical)
        { if (!d.ContainsKey(alias)) d[alias] = canonical; }
        string Resolve(string repo)
        {
            var basic = Ingest.NormalizeRepoBasic(repo); // EXACTLY the planner's identity (host lowercased, path case preserved) — no extra case folding: a differently-cased path is a DIFFERENT repo to the planner, and suppressing it here would silently strand it ownership-unknown
            if (mirrorToCanonical.TryGetValue(basic, out var c)) return c;
            foreach (var k in mirrorToCanonical.Keys)
                if (!string.Equals(k, basic, StringComparison.Ordinal) && string.Equals(k, basic, StringComparison.OrdinalIgnoreCase))
                    Console.Error.WriteLine($"warning: '{repo}' differs from '{k}' only by case — treated as a different repo (planner identity is case-sensitive beyond the host); merge deliberately via mirrors if they are one");
            return basic;
        }

        var assigned = new Dictionary<string, OwnershipEntry>(StringComparer.Ordinal);
        foreach (var o in existing.Ownerships)
        {
            var key = Resolve(o.Repo);
            if (assigned.TryGetValue(key, out var dup))
                Console.Error.WriteLine($"warning: duplicate ownership for {key} ('{dup.Repo}'={dup.Team} then '{o.Repo}'={o.Team}) — one repo; LAST assignment kept, matching the planner's application order");
            assigned[key] = o; // last wins == planner semantics; manual assignments sacred (original spellings preserved)
        }

        string? AssignedKeyFor(string repoKey) => assigned.ContainsKey(repoKey) ? repoKey : null; // exact planner identity only
        var added = 0;
        foreach (var repo in discovered)
        {
            if (AssignedKeyFor(Resolve(repo)) is not null) continue;
            if (mode == NewMode.NewUnassigned) { Console.Error.WriteLine($"unassigned: {repo}"); continue; }
            assigned[repo] = new OwnershipEntry { Repo = repo, Team = mode == NewMode.NewSelf ? selfTeam : newTeamArg! };
            added++;
        }

        // absent-but-assigned repos are KEPT and warned — a scan gap must not silently erase assignments
        var discoveredSet = new HashSet<string>(discovered.Select(Resolve), StringComparer.Ordinal);
        foreach (var key in assigned.Keys.Where(k => !discoveredSet.Contains(k)).OrderBy(k => k, StringComparer.Ordinal))
            Console.Error.WriteLine($"warning: {key} is assigned but absent from this scan set — assignment kept (check the scan coverage)");

        var ownerships = assigned.Values.OrderBy(o => Resolve(o.Repo), StringComparer.Ordinal).ThenBy(o => o.Repo, StringComparer.Ordinal).ToList();
        // whole file preserved except ownerships: mirrors AND any unknown/future top-level fields pass
        // through verbatim (raw-JSON merge — typed models would silently drop what they don't model)
        using var existingDoc = JsonDocument.Parse(File.ReadAllText(existingFile));
        var rootObj = new Dictionary<string, object?> { ["schemaVersion"] = existing.SchemaVersion, ["selfTeamId"] = selfTeam };
        foreach (var prop in existingDoc.RootElement.EnumerateObject())
        {
            if (prop.Name is "schemaVersion" or "selfTeamId" or "ownerships") continue;
            rootObj[prop.Name] = JsonNode.Parse(prop.Value.GetRawText())!; // mirrors + future fields, byte-preserved
        }
        rootObj["ownerships"] = JsonSerializer.SerializeToNode(ownerships, JsonOpts);
        File.WriteAllText(outFile, JsonSerializer.Serialize(rootObj, new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n");
        Console.Error.WriteLine($"ownership: {discovered.Count} repos scanned, {added} new assigned, {ownerships.Count} total -> {outFile}");
        return 0;
    }

    static (Mode, string?) ParseMode(string flag, string? teamArg, string selfTeam) => flag switch
    {
        "all-self" => (Mode.AllSelf, null),
        "team" => string.IsNullOrEmpty(teamArg) ? (default, "--team requires a value") : teamArg == selfTeam ? (default, "--team must differ from --self (a self-team assignment is --all-self)") : (Mode.Team, null),
        "unassigned" => (Mode.Unassigned, null),
        _ => (default, $"unknown mode '{flag}' (all-self | team | unassigned)"),
    };
    static (NewMode, string?) ParseNewMode(string flag, string? teamArg, string selfTeam) => flag switch
    {
        "new-self" => (NewMode.NewSelf, null),
        "new-team" => string.IsNullOrEmpty(teamArg) ? (default, "--new-team requires a value") : teamArg == selfTeam ? (default, "--new-team must differ from the self team") : (NewMode.NewTeam, null),
        "new-unassigned" => (NewMode.NewUnassigned, null),
        _ => (default, $"unknown new-repo mode '{flag}' (new-self | new-team | new-unassigned)"),
    };

    // SPEC-017 §3: explicit dirs are deliberate acts — warned when out of scope, never filtered.
    static void WarnExplicitOutOfScope(string[] scanDirs, EstateScope? scope)
    {
        if (scope is null) return;
        foreach (var d in scanDirs)
            if (!Scope.IsInScope(Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), scope))
                Console.Error.WriteLine($"warning: {Path.GetFileName(d)} is out of scope by config — included because explicit");
    }

    static string[]? ResolveDirs(string[] scanDirs, string? scansRoot, EstateScope? scope = null)
    {
        // EXACTLY ingest's discovery rules (SPEC-011/014): explicit dirs first; discovered children must
        // have facts.ndjson; manifest-only children warn + skip; alternate snapshots of an already-seen
        // repo skip (deliberate combination = explicit dirs). Alternate detection uses repo KEYS (the
        // shared identity), not raw repoName — snapshots that differ in remoteUrl spelling are distinct.
        // SPEC-017: dot-prefixed children are ignored (swap leftovers); scope-excluded children skip
        // but still register their keys for alternate detection.
        if (scansRoot is null) return scanDirs.Length > 0 ? scanDirs : null;
        if (!Directory.Exists(scansRoot)) { Console.Error.WriteLine($"error: --scans-root directory not found: {scansRoot}"); return null; }
        var list = scanDirs.ToList();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        if (list.Count > 0)
        {
            var (initial, _) = Ingest.EnumerateRepoKeysChecked(list.ToArray());
            foreach (var k in initial) seenKeys.Add(k);
        }
        foreach (var child in Directory.GetDirectories(Path.GetFullPath(scansRoot)).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
        {
            var childName = Path.GetFileName(child);
            if (Ingest.IsSwapDir(childName)) { Console.Error.WriteLine($"note: {childName} ignored (scan-estate swap leftover, not a scan)"); continue; }
            if (scope is not null && !Scope.IsInScope(childName, scope))
            {
                // Register alternates only for real scans (facts.ndjson present); a broken EXCLUDED child
                // warns and moves on — an out-of-scope directory must never abort the whole run, matching
                // the unscoped path's warn-and-skip (PR #2 Baz round 1).
                if (File.Exists(Path.Combine(child, "facts.ndjson")))
                {
                    var (xkeys, xerr) = Ingest.EnumerateRepoKeysChecked(new[] { child });
                    if (xerr is not null) Console.Error.WriteLine($"warning: excluded {childName}: {xerr} — not registered for alternate detection");
                    else foreach (var k in xkeys) seenKeys.Add(k); // excluded children still register for alternate detection
                }
                Console.Error.WriteLine($"note: {childName} skipped by discovery (out of scope by config)");
                continue;
            }
            if (!File.Exists(Path.Combine(child, "facts.ndjson")))
            {
                if (File.Exists(Path.Combine(child, "scan-manifest.json")))
                    Console.Error.WriteLine($"warning: {Path.GetFileName(child)} looks like a scan (scan-manifest.json) but has no facts.ndjson — skipped");
                continue;
            }
            var (keys, err) = Ingest.EnumerateRepoKeysChecked(new[] { child });
            if (err is not null) { Console.Error.WriteLine($"error: {err}"); return null; }
            if (keys.Any(seenKeys.Contains))
            { Console.Error.WriteLine($"note: {Path.GetFileName(child)} skipped by discovery (alternate snapshot of an already-discovered repo — pass it explicitly to combine deliberately)"); continue; }
            foreach (var k in keys) seenKeys.Add(k);
            list.Add(child);
        }
        var result = list.ToArray();
        if (result.Length == 0) { Console.Error.WriteLine("error: no scan directories found (check --scans-root — every child lacks facts.ndjson)"); return null; }
        return result;
    }

    static void Write(string outFile, string selfTeam, List<OwnershipEntry> ownerships) =>
        WriteFile(outFile, new OwnershipFile { SchemaVersion = "ownership.v0", SelfTeamId = selfTeam, Ownerships = ownerships });

    static void WriteFile(string outFile, OwnershipFile file)
    {
        var outParent = Path.GetDirectoryName(Path.GetFullPath(outFile)) ?? ".";
        Directory.CreateDirectory(outParent);
        var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outFile, json.ReplaceLineEndings("\n") + "\n");
    }

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// SPEC-018: outdated discovery. Compares the estate's installed-version inventory (lockfile rows +
// exact declared pins) against feed truth (feed-versions.v1 — a file, or a local folder feed of
// *.nupkg names), classifies every installation (behind/current/ahead/unclassified/unknown-feed)
// with apply's own comparer so drift can never disagree with the upgrade-only policy, and emits
// ready-to-run single-change delta candidates. Feed truth is never fetched live: the file IS the
// truth, as-of its own provenance ("stated, not verified live").
public static class Drift
{
    public sealed class FeedFile
    {
        [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("asOf")] public string? AsOf { get; set; }
        [JsonPropertyName("packages")] public List<FeedPackage> Packages { get; set; } = new();
    }

    public sealed class FeedPackage
    {
        [JsonPropertyName("packageId")] public string PackageId { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
    }

    public sealed class Installation
    {
        public string Repo = "";
        public string Version = "";
        public string Evidence = ""; // "lockfile" | "declared-pin"
        public string Status = "";   // behind | current | ahead | unclassified | unknown-feed
        public string? Reason;       // D1/D2/D3, unclassified rows only
    }

    public sealed class DriftPackage
    {
        public string PackageId = "";
        public string? Latest;
        public Dictionary<string, int> Statuses = new(); // behind/current/ahead/unclassified (installation counts)
        public List<Installation> Installations = new();
    }

    public sealed class DriftReport
    {
        public string Source = "";
        public string? AsOf;
        public int PackageCount;
        public List<DriftPackage> Packages = new();
    }

    // ---- entry: returns (report, exitCode). exitCode != 0 → report null, message already printed. ----
    public static (DriftReport? report, string? canonical, int rc) Run(string fixtureDir, string? feedPath, string? folderFeed, string? emitDeltas)
    {
        // precedence: flags → fixture load → feed load/validate → classify → emit (SPEC-018 §2)
        if ((feedPath is null) == (folderFeed is null))
        { Console.Error.WriteLine("error: exactly one truth source required — pass --feed <file> OR --folder-feed <dir>"); return (null, null, 1); }
        Engine engine;
        try { engine = Program.LoadEngine(fixtureDir); } // delta.json = loader precondition only; content never read here
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return (null, null, 3); } // typed runtime errors exit 3 (§2) — in-process callers get rc, not a throw

        var (feed, feedErr, feedRc) = feedPath is not null ? LoadFeedFile(feedPath) : LoadFolderFeed(folderFeed!);
        if (feed is null) { Console.Error.WriteLine($"error: {feedErr}"); return (null, null, feedRc); }

        var report = Build(engine, feed);

        if (emitDeltas is not null)
        {
            var rcE = EmitDeltas(report, emitDeltas);
            if (rcE != 0) return (null, null, rcE);
        }
        return (report, Write(report), 0);
    }

    // ---- feed truth: file form ----
    static (FeedFile?, string?, int) LoadFeedFile(string path)
    {
        if (!File.Exists(path)) return (null, $"--feed file not found: {path}", 1);
        FeedFile? raw;
        try { raw = JsonSerializer.Deserialize<FeedFile>(File.ReadAllText(path), Opts); }
        catch (JsonException ex) { return (null, $"feed file {Esc(Path.GetFileName(path))}: invalid JSON ({ex.Message})", 5); } // basename is untrusted on unix (PR #3 Codex r4)
        return Validate(raw, Path.GetFileName(path));
    }

    static (FeedFile?, string?, int) Validate(FeedFile? raw, string displayName)
    {
        displayName = Esc(displayName); // the basename itself is untrusted on unix (PR #3 Codex r3)
        if (raw is null) return (null, $"feed file {displayName}: unparseable", 5);
        if (raw.SchemaVersion != "feed-versions.v1")
            return (null, $"feed file {displayName}: schemaVersion mismatch — expected 'feed-versions.v1', got '{Esc(raw.SchemaVersion ?? "")}'", 5);
        if (raw.Source != "operator-provided" && raw.Source != "folder-feed")
            return (null, $"feed file {displayName}: source must be 'operator-provided' or 'folder-feed', got '{Esc(raw.Source ?? "")}'", 5);
        if (raw.Packages is null)
            return (null, $"feed file {displayName}: packages is null — a feed with no packages must not exist at all", 5);
        var byId = new Dictionary<string, FeedPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in raw.Packages)
        {
            if (p is null)
                return (null, $"feed file {displayName}: null packages[] element — every entry must be an object", 5);
            if (string.IsNullOrEmpty(p.PackageId) || string.IsNullOrEmpty(p.Version))
                return (null, $"feed file {displayName}: empty packageId or version — every entry needs both", 5);
            if (byId.TryGetValue(p.PackageId, out var dup))
            {
                if (dup.Version == p.Version) continue; // idempotent collapse
                return (null, $"feed file {displayName}: '{Esc(p.PackageId)}' at two versions ({Esc(dup.Version)} and {Esc(p.Version)}) — the feed contradicts itself", 5);
            }
            byId[p.PackageId] = p;
        }
        raw.Packages = byId.Values.OrderBy(p => p.PackageId, StringComparer.Ordinal).ToList();
        return (raw, null, 0);
    }

    // Untrusted values (feed fields, filenames) reach diagnostics verbatim; control characters would
    // forge terminal/log line structure the redactor does not escape (PR #3 Baz round 1).
    static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            sb.Append(c < 0x20 || c == 0x7f ? $"\\u{(int)c:x4}" : c);
        return sb.ToString();
    }

    // ---- feed truth: folder form (flat, depth-1, *.nupkg; latest-only by construction) ----
    static (FeedFile?, string?, int) LoadFolderFeed(string dir)
    {
        if (!Directory.Exists(dir)) return (null, $"--folder-feed directory not found: {dir}", 1);
        var feed = new FeedFile { SchemaVersion = "feed-versions.v1", Source = "folder-feed" };
        var byId = new Dictionary<string, (string Version, string File)>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(Path.GetFullPath(dir), "*.nupkg").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file)[..^".nupkg".Length];
            var (id, version) = ParseNupkgName(name);
            if (id is null || version is null)
            { Console.Error.WriteLine($"warning: {Esc(Path.GetFileName(file))} does not resolve as {{PackageId}}.{{version}} — skipped (never guessed)"); continue; }
            if (byId.TryGetValue(id!, out var dup))
            {
                if (dup.Version == version) continue; // identical (id, version) — case-variant filename duplicates collapse, matching the file form (PR #3 Codex round 1)
                return (null, $"folder feed carries '{Esc(id!)}' at two versions ({Esc(dup.Version)} in {Esc(dup.File)} and {Esc(version!)} in {Esc(Path.GetFileName(file))}) — latest-selection needs prerelease ordering V0 refuses; keep one version per package or use --feed", 5);
            }
            byId[id!] = (version!, Path.GetFileName(file));
        }
        feed.Packages = byId.Select(kv => new FeedPackage { PackageId = kv.Key, Version = kv.Value.Version }).OrderBy(p => p.PackageId, StringComparer.Ordinal).ToList();
        return (feed, null, 0);
    }

    // longest trailing substring matching core(.core){0,3}(-prerelease)?(+build)?, each core = 1+ digits —
    // the FIRST viable cut scanning LEFT→RIGHT yields the longest suffix. Worked: Newtonsoft.Json.13.0.3 →
    // (Newtonsoft.Json, 13.0.3); X.13.0.3-beta.1 → (X, 13.0.3-beta.1); Foo-Bar.1.0.0 → (Foo-Bar, 1.0.0);
    // Foo.1.2.0.0 → (Foo, 1.2.0.0) — documented NuGet ambiguity (§9).
    internal static (string? id, string? version) ParseNupkgName(string name)
    {
        for (var cut = name.IndexOf('.'); cut > 0 && cut < name.Length - 1; cut = name.IndexOf('.', cut + 1))
        {
            var candidate = name[(cut + 1)..];
            if (IsVersion(candidate)) return (name[..cut], candidate);
        }
        // no viable split (or the whole name would be the version with an empty id) — not a package file
        return (null, null);
    }

    // core(.core){0,3}(-prerelease)?(+build)? — prerelease/build segments keep their own chars (dots legal)
    internal static bool IsVersion(string s)
    {
        var core = s;
        var pre = "";
        var build = "";
        var plus = core.IndexOf('+');
        if (plus >= 0) { build = core[(plus + 1)..]; core = core[..plus]; }
        var dash = core.IndexOf('-', 1); // a leading char can't be '-' for a numeric core; search past index 0
        if (dash >= 0) { pre = core[(dash + 1)..]; core = core[..dash]; }
        var parts = core.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        foreach (var p in parts) if (p.Length == 0 || !p.All(char.IsDigit)) return false;
        if (pre.Length == 0 && dash >= 0) return false; // "-" with empty prerelease
        if (build.Length == 0 && plus >= 0) return false;
        // prerelease/build must not be empty and must not start with a dot (guarded by split shapes)
        return true;
    }

    // ---- inventory + classification ----
    static DriftReport Build(Engine engine, FeedFile feed)
    {
        var latestById = feed.Packages.ToDictionary(p => p.PackageId, p => p.Version, StringComparer.OrdinalIgnoreCase);

        // (repo, packageKey, version) → evidence, lockfile wins; keep distinct versions (visible discrepancy).
        // Reported spelling: lockfile spelling when any lockfile row exists for the group, else pin spelling;
        // among case-variants of one source, ordinal-first wins (input-order-independent — permutation-stable).
        var byPackage = new SortedDictionary<string, DriftPackage>(StringComparer.OrdinalIgnoreCase);
        var lockSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase); // packageKey -> spellings from lockfile rows
        var depsSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase); // SPEC-019: build-output spellings (lockfile wins the reported spelling)
        var pinSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<(string Repo, string PackageKey, string Version, string Evidence)>();

        foreach (var (repo, lockRows) in engine.LockRowsView)
            foreach (var row in lockRows)
            {
                if (string.IsNullOrEmpty(row.Version)) continue; // unevidenced resolution never counts (SPEC-007)
                var evidence = row.Provenance is not null ? "deps.json" : "lockfile"; // SPEC-019 §4: an inventory reader can always tell resolution truth from build output
                rows.Add((repo, row.PackageId, row.Version, evidence));
                if (evidence == "lockfile")
                {
                    if (!lockSpellings.TryGetValue(row.PackageId, out var set)) lockSpellings[row.PackageId] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(row.PackageId);
                }
                else
                {
                    if (!depsSpellings.TryGetValue(row.PackageId, out var set)) depsSpellings[row.PackageId] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(row.PackageId);
                }
            }
        foreach (var (repo, facts) in engine.FactsView)
            foreach (var f in facts)
            {
                if (!Apply.IsExactPin(f.DeclaredConstraint)) continue;
                rows.Add((repo, f.PackageId, f.DeclaredConstraint, "declared-pin"));
                if (!pinSpellings.TryGetValue(f.PackageId, out var set)) pinSpellings[f.PackageId] = set = new SortedSet<string>(StringComparer.Ordinal);
                set.Add(f.PackageId);
            }

        var notedNull = 0;
        foreach (var (repo, lockRows) in engine.LockRowsView)
            foreach (var row in lockRows)
                if (string.IsNullOrEmpty(row.Version)) notedNull++;

        foreach (var key in rows.Select(r => r.PackageKey).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pkg = new DriftPackage();
            // reported spelling: lockfile > deps.json > declared-pin, ordinal-first within each (SPEC-019 §4 precedence)
            pkg.PackageId = lockSpellings.TryGetValue(key, out var ls) && ls.Count > 0 ? ls.Min!
                          : depsSpellings.TryGetValue(key, out var ds) && ds.Count > 0 ? ds.Min!
                          : pinSpellings.TryGetValue(key, out var ps) && ps.Count > 0 ? ps.Min!
                          : key;
            byPackage[key] = pkg;
        }
        if (notedNull > 0) Console.Error.WriteLine($"note: {notedNull} lockfile row(s) with no resolved version skipped — unevidenced resolution never counts");

        foreach (var g in rows.GroupBy(r => r.PackageKey, StringComparer.OrdinalIgnoreCase))
        {
            var pkg = byPackage[g.Key];
            // dedup on (repo, package, version): lockfile > deps.json > declared-pin (SPEC-018 §4 + SPEC-019 §4)
            foreach (var grp in g.GroupBy(x => (x.Repo, x.Version)))
            {
                var r = grp.FirstOrDefault(e => e.Evidence == "lockfile");
                if (r.Evidence is null) { var d = grp.FirstOrDefault(e => e.Evidence == "deps.json"); if (d.Evidence is not null) r = d; }
                if (r.Evidence is null) r = grp.First();
            {
                var inst = new Installation { Repo = r.Repo, Version = r.Version, Evidence = r.Evidence };
                if (!latestById.TryGetValue(g.Key, out var latest))
                {
                    inst.Status = "unknown-feed";
                }
                else
                {
                    var cmp = Apply.CompareCore(r.Version, latest);
                    inst.Status = cmp switch
                    {
                        < 0 => "behind",
                        0 => "current",
                        > 0 => "ahead",
                        null => "unclassified",
                    };
                    if (cmp is null)
                    {
                        bool badInstalled = Apply.CompareCore(r.Version, r.Version) is null;
                        bool badLatest = Apply.CompareCore(latest, latest) is null;
                        inst.Reason = (badInstalled, badLatest) switch
                        {
                            (true, true) => $"both installed {r.Version} and feed latest {latest} not plain numeric pins",
                            (true, false) => $"installed {r.Version} not a plain numeric pin",
                            (false, true) => $"feed latest {latest} not a plain numeric pin",
                            _ => null,
                        };
                    }
                }
                pkg.Installations.Add(inst);
            }
            }
            if (latestById.TryGetValue(g.Key, out var latestForCounts)) pkg.Latest = latestForCounts;
            pkg.Installations = pkg.Installations.OrderBy(i => i.Repo, StringComparer.Ordinal).ThenBy(i => i.Version, StringComparer.Ordinal).ToList();
            pkg.Statuses["behind"] = pkg.Installations.Count(i => i.Status == "behind"); // in-process consumers see the same counts Write emits (PR #3 Baz r4)
            pkg.Statuses["current"] = pkg.Installations.Count(i => i.Status == "current");
            pkg.Statuses["ahead"] = pkg.Installations.Count(i => i.Status == "ahead");
            pkg.Statuses["unclassified"] = pkg.Installations.Count(i => i.Status == "unclassified");
        }

        return new DriftReport
        {
            Source = feed.Source,
            AsOf = feed.AsOf,
            PackageCount = feed.Packages.Count,
            Packages = byPackage.Values.OrderBy(p => p.PackageId, StringComparer.Ordinal).ToList(),
        };
    }

    // ---- delta candidates ----
    static int EmitDeltas(DriftReport report, string dir)
    {
        // The stale-set refusal runs BEFORE the no-behind early return: an all-current rerun must not
        // leave a previously emitted candidate set masquerading as the latest result (PR #3 Codex r2).
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
        { var n = Directory.EnumerateFileSystemEntries(dir).Count(); Console.Error.WriteLine($"error: --emit-deltas directory is not empty: {dir} ({n} entr{(n == 1 ? "y" : "ies")}) — stale candidates must never sit beside a fresh set; clear it or pass a fresh directory"); return 3; }
        var behind = report.Packages.Where(p => p.Latest is not null && p.Installations.Any(i => i.Status == "behind")).ToList();
        if (behind.Count == 0) { Console.Error.WriteLine("note: no behind packages — nothing to emit (no directory created)"); return 0; }
        // Precompute and validate EVERY candidate before touching the destination: collisions and a
        // non-empty destination refuse up front, so a refusal never leaves partial or overwritten
        // output and a stale candidate can never sit beside the new set (PR #3 round 1).
        var staged = new List<(string File, string Content)>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pkg in behind)
            foreach (var from in pkg.Installations.Where(i => i.Status == "behind").Select(i => i.Version).Distinct(StringComparer.Ordinal))
            {
                var file = $"{Safe(pkg.PackageId)}.{Safe(from)}.delta.json";
                if (!taken.Add(file))
                { Console.Error.WriteLine($"error: filename collision after sanitization — two distinct candidates map to {file}"); return 3; }
                var delta = new DeltaFile
                {
                    Version = "package-delta.v1",
                    SourceRepo = "https://example.invalid/x.git",
                    SourceCommitSha = new string('0', 40),
                    Changes = new List<DeltaChange> { new()
                    {
                        Id = $"drift-{pkg.PackageId}-{from}-{pkg.Latest}",
                        PackageName = pkg.PackageId, // the ESTATE spelling — never the feed's (SPEC-018 §4)
                        Ecosystem = "nuget",
                        ChangeType = "updated",
                        OldVersion = from,
                        NewVersion = pkg.Latest!,
                    } },
                };
                staged.Add((file, JsonSerializer.Serialize(delta, WriteOpts).ReplaceLineEndings("\n") + "\n"));
            }
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
        { var n = Directory.EnumerateFileSystemEntries(dir).Count(); Console.Error.WriteLine($"error: --emit-deltas directory is not empty: {dir} ({n} entr{(n == 1 ? "y" : "ies")}) — stale candidates must never sit beside a fresh set; clear it or pass a fresh directory"); return 3; }
        Directory.CreateDirectory(dir); // iff >=1 candidate (spec §7)
        foreach (var (file, content) in staged)
            File.WriteAllText(Path.Combine(dir, file), content);
        Console.Error.WriteLine($"note: {staged.Count} delta candidate(s) → {dir} (single-change, ready for ua plan; picking which to run is a human decision)");
        return 0;
    }

    static string Safe(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_');
        return sb.ToString();
    }

    // ---- canonical drift.v1 writer (house style: 2-space, LF, trailing newline, fixed key order) ----
    public static string Write(DriftReport r)
    {
        var sb = new StringBuilder();
        sb.Append('{').Append('\n');
        sb.Append("  \"schemaVersion\": \"drift.v1\",\n");
        sb.Append("  \"feed\": {").Append('\n');
        sb.Append("    \"source\": ").Append(Q(r.Source)).Append(',').Append('\n');
        if (r.AsOf is not null) sb.Append("    \"asOf\": ").Append(Q(r.AsOf)).Append(',').Append('\n');
        sb.Append("    \"packageCount\": ").Append(r.PackageCount).Append('\n');
        sb.Append("  },").Append('\n');
        var behind = r.Packages.Sum(p => p.Installations.Count(i => i.Status == "behind"));
        var current = r.Packages.Sum(p => p.Installations.Count(i => i.Status == "current"));
        var ahead = r.Packages.Sum(p => p.Installations.Count(i => i.Status == "ahead"));
        var unclass = r.Packages.Sum(p => p.Installations.Count(i => i.Status == "unclassified"));
        var unknownPkgs = r.Packages.Count(p => p.Installations.Any(i => i.Status == "unknown-feed"));
        sb.Append("  \"summary\": {").Append('\n');
        sb.Append($"    \"packagesObserved\": {r.Packages.Count},").Append('\n');
        sb.Append($"    \"behind\": {behind},").Append('\n');
        sb.Append($"    \"current\": {current},").Append('\n');
        sb.Append($"    \"ahead\": {ahead},").Append('\n');
        sb.Append($"    \"unclassified\": {unclass},").Append('\n');
        sb.Append($"    \"unknownFeedPackages\": {unknownPkgs}").Append('\n');
        sb.Append("  },").Append('\n');
        sb.Append("  \"packages\": ");
        if (r.Packages.Count == 0) sb.Append("[]");
        else
        {
            sb.Append('[').Append('\n');
            for (int p = 0; p < r.Packages.Count; p++)
            {
                var pkg = r.Packages[p];
                sb.Append("    {").Append('\n');
                sb.Append("      \"packageId\": ").Append(Q(pkg.PackageId)).Append(',').Append('\n');
                sb.Append("      \"latest\": ").Append(pkg.Latest is null ? "null" : Q(pkg.Latest)).Append(',').Append('\n');
                var st = pkg.Installations.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count());
                int S(string k) => st.TryGetValue(k, out var n) ? n : 0;
                var nBehind = S("behind"); var nCurrent = S("current"); var nAhead = S("ahead"); var nUnc = S("unclassified");
                sb.Append("      \"statuses\": {").Append('\n');
                sb.Append($"        \"behind\": {nBehind},").Append('\n');
                sb.Append($"        \"current\": {nCurrent},").Append('\n');
                sb.Append($"        \"ahead\": {nAhead},").Append('\n');
                sb.Append($"        \"unclassified\": {nUnc}").Append('\n');
                sb.Append("      },").Append('\n');
                sb.Append("      \"installations\": ");
                if (pkg.Installations.Count == 0) sb.Append("[]");
                else
                {
                    sb.Append('[').Append('\n');
                    for (int i = 0; i < pkg.Installations.Count; i++)
                    {
                        var inst = pkg.Installations[i];
                        sb.Append("        {").Append('\n');
                        sb.Append("          \"repo\": ").Append(Q(inst.Repo)).Append(',').Append('\n');
                        sb.Append("          \"version\": ").Append(Q(inst.Version)).Append(',').Append('\n');
                        sb.Append("          \"evidence\": ").Append(Q(inst.Evidence)).Append(',').Append('\n');
                        sb.Append("          \"status\": ").Append(Q(inst.Status));
                        if (inst.Reason is not null) { sb.Append(',').Append('\n'); sb.Append("          \"reason\": ").Append(Q(inst.Reason)); }
                        sb.Append('\n');
                        sb.Append("        }").Append(i < pkg.Installations.Count - 1 ? "," : "").Append('\n');
                    }
                    sb.Append("      ]");
                }
                sb.Append('\n');
                sb.Append("    }").Append(p < r.Packages.Count - 1 ? "," : "").Append('\n');
            }
            sb.Append("  ]");
        }
        sb.Append('\n');
        sb.Append('}').Append('\n');
        return sb.ToString();
    }

    static string Q(string s)
    {
        var sb = new StringBuilder();
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };
    static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}

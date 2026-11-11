using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// The compatibility cache — one JSON file per package, the work-proven format
// (docs/UPGRADE-PLAN-DESIGN.md). Probe = read the .nupkg (a zip) and list lib/** — the
// folder names ARE the supported TFMs; one read per package-version, cached per major.
// The usedVersions/usedByRepos back-references ride in the same file so depends-lookups
// never rescan. Probing NEVER destroys estate joins: a re-probe updates version records
// and the cheap version list, and preserves usedVersions/usedByRepos as they were.
public sealed class CompatCacheFile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("versionsInspected")] public int VersionsInspected { get; set; }
    [JsonPropertyName("totalVersionsAvailable")] public int TotalVersionsAvailable { get; set; }
    [JsonPropertyName("fetchedUtc")] public string FetchedUtc { get; set; } = "";
    [JsonPropertyName("allVersionsSimple")] public List<string> AllVersionsSimple { get; set; } = new();
    [JsonPropertyName("usedVersions")] public Dictionary<string, List<string>> UsedVersions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonPropertyName("usedByRepos")] public List<string> UsedByRepos { get; set; } = new();
    [JsonPropertyName("versions")] public List<CompatVersion> Versions { get; set; } = new();
}

public sealed class CompatVersion
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("supportsNetStandard")] public bool SupportsNetStandard { get; set; }
    [JsonPropertyName("inUse")] public bool InUse { get; set; }
    [JsonPropertyName("isLatestForMajor")] public bool IsLatestForMajor { get; set; }
    [JsonPropertyName("targetFrameworks")] public List<string> TargetFrameworks { get; set; } = new();
    [JsonPropertyName("supportsNet8")] public bool? SupportsNet8 { get; set; }
    [JsonPropertyName("supportsNet10")] public bool? SupportsNet10 { get; set; }
    [JsonPropertyName("license")] public string? License { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public static class CompatCache
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true, Encoder = StableJsonEncoder.Instance };
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Encoder = StableJsonEncoder.Instance };

    public static string CachePath(string cacheDir, string packageId) => Path.Combine(cacheDir, packageId.ToLowerInvariant() + ".json");

    public static CompatCacheFile? Load(string cacheDir, string packageId)
    {
        var path = CachePath(cacheDir, packageId);
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<CompatCacheFile>(File.ReadAllText(path), ReadOpts);
    }

    // TFM compatibility, V1 derivation table (pinned by selftest): a package TFM "netA[.B]"
    // admits a repo TFM "netX[.Y]" when A <= X — runtimes run their own and OLDER package
    // frameworks (a net10-only package is invisible to a net8 repo; that IS the ceiling). Anything else (netstandard1.x, netcoreapp*, unknown shapes) is
    // NOT counted — an ancient package with only legacy assets stays undetermined and lands
    // in the onboarding path, never a guessed "compatible".
    public static bool? SupportsTfm(IReadOnlyList<string> packageTfms, string repoTfm)
    {
        var major = RepoTfmMajor(repoTfm);
        if (major is null) return null;
        var admitted = false;
        foreach (var tfm in packageTfms)
        {
            var t = tfm.Trim();
            if (t.Equals("netstandard2.0", StringComparison.OrdinalIgnoreCase) || t.Equals("netstandard2.1", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.StartsWith("net", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(t[3]))
            {
                var pkgMajor = RepoTfmMajor(t);
                if (pkgMajor is not null && pkgMajor <= major) admitted = true; // the package targets at-or-below the repo: runtimes run OLDER package frameworks
            }
        }
        return admitted;
    }

    private static int? RepoTfmMajor(string tfm)
    {
        var t = tfm.Trim();
        if (!t.StartsWith("net", StringComparison.OrdinalIgnoreCase)) return null;
        var digits = t[3..].Split('.');
        if (!int.TryParse(digits[0], out var major) || major < 0) return null;
        return major;
    }

    // The ceiling for one repo TFM under an optional peg bound: the highest probed,
    // stable-parseable, TFM-compatible version the bound admits. null = nothing qualifies
    // (onboarding/unknown — never guessed). Only PROBED records are candidates: the cheap
    // allVersionsSimple list carries no TFM evidence.
    public static string? Ceiling(CompatCacheFile cache, string repoTfm, string? boundKind, string? boundVersion)
    {
        string? best = null;
        foreach (var v in cache.Versions)
        {
            if (Apply.ParseCorePublic(v.Version) is null) continue; // prerelease/floating: not a stable candidate
            if (SupportsTfm(v.TargetFrameworks, repoTfm) != true) continue;
            if (boundKind == Pegs.KindExact && Apply.CompareCore(v.Version, boundVersion!) != 0) continue;
            if (boundKind == Pegs.KindMaxExclusive && Apply.CompareCore(v.Version, boundVersion!) >= 0) continue;
            if (best is null || Apply.CompareCore(v.Version, best) > 0) best = v.Version;
        }
        return best;
    }

    // The number the update driver acts on: compat ceiling under the peg bound, plus the
    // forceMax floor check (the floor is intent — an unmet floor is a typed violation the
    // driver refuses on, never a silent downgrade of intent).
    public static (string? Max, bool FloorUnsatisfiable, string? Floor) AllowedMax(CompatCacheFile cache, string repoTfm, Pegs.Resolved? peg)
    {
        var max = Ceiling(cache, repoTfm, peg?.BoundKind, peg?.BoundVersion);
        var floor = peg?.FloorVersion;
        var violated = floor is not null && (max is null || Apply.CompareCore(max, floor) < 0);
        return (max, violated, violated ? floor : null);
    }

    // ---- probing (V1 source: a local folder of .nupkg files — the drift folder-feed shape) ----
    public static int ProbeFolder(string nupkgDir, string cacheDir, string? packageFilter)
    {
        List<(string Id, string Version, string Path)> packages;
        try
        {
            packages = Directory.GetFiles(Path.GetFullPath(nupkgDir), "*.nupkg")
                .Select(path =>
                {
                    var stem = Path.GetFileName(path);
                    if (stem.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)) stem = stem[..^".nupkg".Length]; // drift's convention: parse the stem, not the extension
                    var (id, version) = Drift.ParseNupkgName(stem);
                    return (Id: id, Version: version, Path: path);
                })
                .Where(p => p.Id is not null && p.Version is not null)
                .Select(p => (Id: p.Id!, Version: p.Version!, Path: p.Path))
                .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Version, StringComparer.Ordinal)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: cannot read the nupkg folder: {ex.Message}");
            return 1;
        }
        if (packageFilter is not null)
            packages = packages.Where(p => p.Id.Equals(packageFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (packages.Count == 0)
        {
            Console.Error.WriteLine("error: no matching .nupkg files found (flat depth-1 folder, {id}.{version}.nupkg)");
            return 1;
        }
        Directory.CreateDirectory(cacheDir);
        var probed = 0; var onboarding = 0;
        foreach (var group in packages.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var cache = Load(cacheDir, group.Key) ?? new CompatCacheFile { Id = group.Key };
            cache.TotalVersionsAvailable = group.Select(p => p.Version).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            cache.AllVersionsSimple = group.Select(p => p.Version).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.Ordinal).ToList();
            cache.FetchedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            // probe targets: the latest stable-parseable version per major (selective probing —
            // the cheap list carries the rest); versions already probed are NOT re-read.
            var known = cache.Versions.Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var probeTargets = group
                .Select(p => (p.Version, Core: Apply.ParseCorePublic(p.Version)))
                .Where(p => p.Core is not null)
                .GroupBy(p => p.Core![0])
                .Select(g => g.OrderByDescending(p => p.Version, VersionComparer.Instance).First().Version) // full 4-part rank, not major-only
                .Where(v => !known.Contains(v))
                .ToList();
            foreach (var version in probeTargets)
            {
                var path = group.First(p => p.Version == version).Path;
                List<string> tfms;
                try
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                    tfms = zip.Entries
                        .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && e.FullName.Length > "lib/".Length)
                        .Select(e => e.FullName["lib/".Length..].Split('/')[0])
                        .Where(seg => seg.StartsWith("net", StringComparison.OrdinalIgnoreCase)) // TFM dirs (net8.0, netstandard2.0); excludes marker files like lib/_._
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(t => t, StringComparer.Ordinal)
                        .ToList();
                }
                catch (Exception ex) when (ex is IOException or System.IO.InvalidDataException)
                {
                    Console.Error.WriteLine($"warning: {group.Key} {version}: unreadable nupkg ({ex.Message}) — skipped (onboarding)");
                    onboarding++;
                    continue;
                }
                var record = new CompatVersion
                {
                    Version = version,
                    TargetFrameworks = tfms,
                    SupportsNetStandard = tfms.Any(t => t.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)),
                    Note = tfms.Count == 0 ? "no lib assets probed — undetermined (onboarding)" : null,
                };
                record.SupportsNet8 = tfms.Count == 0 ? null : SupportsTfm(tfms, "net8.0");
                record.SupportsNet10 = tfms.Count == 0 ? null : SupportsTfm(tfms, "net10.0");
                cache.Versions.RemoveAll(old => old.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
                cache.Versions.Add(record);
                probed++;
                if (tfms.Count == 0) onboarding++;
            }
            // isLatestForMajor: the highest stable-parseable probed version within each major
            foreach (var grp in cache.Versions.Where(v => Apply.ParseCorePublic(v.Version) is not null).GroupBy(v => Apply.ParseCorePublic(v.Version)![0]))
            {
                var latest = grp.OrderByDescending(v => v.Version, VersionComparer.Instance).First();
                foreach (var v in grp) v.IsLatestForMajor = v.Version == latest.Version;
            }
            Program.WriteArtifactFile(CachePath(cacheDir, group.Key),
                JsonSerializer.Serialize(cache, WriteOpts).ReplaceLineEndings("\n") + "\n");
            Console.Error.WriteLine($"discovered {group.Key}: {cache.Versions.Count} probed of {cache.TotalVersionsAvailable} available (probed {probeTargets.Count} new this run)");
        }
        if (onboarding > 0)
            Console.Error.WriteLine($"note: {onboarding} version(s) probed undetermined (no lib assets / unreadable) — they stay out of ceilings until onboarded");
        return 0;
    }

    // Full 4-part numeric rank for stable-parseable versions (CompareCore against a constant
    // ranks by the FIRST differing part only — it cannot order 15.0.2 vs 15.0.4 against "0").
    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();
        public int Compare(string? a, string? b)
        {
            var pa = Apply.ParseCorePublic(a ?? ""); var pb = Apply.ParseCorePublic(b ?? "");
            if (pa is null || pb is null) return StringComparer.Ordinal.Compare(a, b);
            for (var i = 0; i < 4; i++)
            {
                var x = i < pa.Length ? pa[i] : 0; var y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }
    }
}

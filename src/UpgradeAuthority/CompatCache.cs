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
    // TFM normalization: nuspec group names come in long form (.NETFramework4.6.1,
    // .NETStandard2.0, .NETCoreApp8.0 — iText7's nuspec uses exactly that shape) and the short
    // forms hide a trap: net20/net462/net48 are .NET FRAMEWORK, not "net major 20" — a legacy
    // framework asset never admits a modern repo. Returns the canonical short form plus a kind.
    internal static (string Tfm, string Kind) NormalizeTfm(string raw)
    {
        var s = raw.Trim();
        if (s.Equals("any", StringComparison.OrdinalIgnoreCase)) return ("any", "any");
        var dotNetStandard = s.StartsWith(".NETStandard", StringComparison.OrdinalIgnoreCase) ? s[".NETStandard".Length..] : null;
        var dotNetFramework = s.StartsWith(".NETFramework", StringComparison.OrdinalIgnoreCase) ? s[".NETFramework".Length..] : null;
        var dotNetCoreApp = s.StartsWith(".NETCoreApp", StringComparison.OrdinalIgnoreCase) ? s[".NETCoreApp".Length..] : null;
        if (dotNetStandard is not null && dotNetStandard.Length > 0) return ("netstandard" + dotNetStandard, "netstandard");
        if (dotNetFramework is not null && dotNetFramework.Length > 0) return ("net" + dotNetFramework.Replace(".", ""), "framework"); // 4.6.1 -> net461: LEGACY
        if (dotNetCoreApp is not null && dotNetCoreApp.Length > 0) return ("net" + dotNetCoreApp, "modern"); // 8.0 -> net8.0
        if (s.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)) return (s.ToLowerInvariant(), "netstandard");
        var lower = s.ToLowerInvariant();
        if (lower.StartsWith("net") && lower.Length > 3)
        {
            var version = lower[3..];
            return (version.Contains('.') ? ("net" + version, "modern") : ("net" + version, "framework")); // net8.0 modern; net20/net462 LEGACY
        }
        return (lower, "other"); // native, uap, wp...: not .NET asset evidence
    }

    public static bool? SupportsTfm(IReadOnlyList<string> packageTfms, string repoTfm)
    {
        if (packageTfms.Any(t => t.Equals("any", StringComparison.OrdinalIgnoreCase))) return true; // the evidence-of-absence case: nothing in the package excludes a framework
        var major = RepoTfmMajor(repoTfm);
        if (major is null) return null;
        var admitted = false;
        foreach (var tfm in packageTfms)
        {
            var (tfmShort, kind) = NormalizeTfm(tfm);
            if (tfmShort.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase))
            {
                var nsVersion = tfmShort["netstandard".Length..];
                if (nsVersion.StartsWith("2")) return true; // netstandard2.x admits net5+; netstandard1.x does not (legacy)
                continue;
            }
            if (kind == "modern")
            {
                var pkgMajor = RepoTfmMajor(tfmShort);
                if (pkgMajor is not null && pkgMajor <= major) admitted = true; // the package targets at-or-below the repo: runtimes run OLDER package frameworks
            }
            // "framework" (net20/net462/net48) and "other" admit nothing modern — by parse, not by accident
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
    public static string? Ceiling(CompatCacheFile cache, string repoTfm, string? boundKind, string? boundVersion, string? maxVersion = null)
    {
        string? best = null;
        foreach (var v in cache.Versions)
        {
            if (Apply.ParseCorePublic(v.Version) is null) continue; // prerelease/floating: not a stable candidate
            if (SupportsTfm(v.TargetFrameworks, repoTfm) != true) continue;
            if (boundKind == Pegs.KindExact && Apply.CompareCore(v.Version, boundVersion!) != 0) continue;
            if (boundKind == Pegs.KindMaxExclusive && Apply.CompareCore(v.Version, boundVersion!) >= 0) continue;
            if (maxVersion is not null && Apply.CompareCore(v.Version, maxVersion) > 0) continue; // feed-truth cap: a probed version the feed does not carry is not a recommendation
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
            // probe targets (the brief's selection rule): the latest stable-parseable version per
            // major PLUS every in-use version — the estate's pinned versions MUST get TFM records
            // or their ceilings cannot resolve (the first estate census: 199 compat-blocked because
            // latest-per-major alone skipped the pinned versions). Already-probed versions are
            // never re-read.
            // "already probed" = a record with real TFM evidence: the estate join's stub records
            // (InUse, undetermined TFMs) are precisely the versions that need probing
            var known = cache.Versions.Where(v => v.TargetFrameworks.Count > 0).Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var inUse = cache.Versions.Where(v => v.InUse).Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var probeTargets = group
                .Select(p => (p.Version, Core: Apply.ParseCorePublic(p.Version)))
                .Where(p => p.Core is not null)
                .GroupBy(p => p.Core![0])
                .Select(g => g.OrderByDescending(p => p.Version, VersionComparer.Instance).First().Version) // full 4-part rank, not major-only
                .Concat(group.Select(p => p.Version).Where(v => inUse.Contains(v)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(v => !known.Contains(v))
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();
            foreach (var version in probeTargets)
            {
                var path = group.First(p => p.Version == version).Path;
                List<string> tfms;
                try
                {
                    using var zip = System.IO.Compression.ZipFile.OpenRead(path);
                    tfms = GetTfmEvidence(zip);
                }
                catch (Exception ex) when (ex is IOException or System.IO.InvalidDataException)
                {
                    Console.Error.WriteLine($"warning: {group.Key} {version}: unreadable nupkg ({ex.Message}) — skipped (onboarding)");
                    onboarding++;
                    continue;
                }
                if (ApplyProbeRecord(cache, version, tfms)) onboarding++; else probed++;
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

    // ---- the shared probe core (folder path and feed path both land here) ----

    // TFM evidence, in confidence order: lib/** is the runtime truth; when a package has NO
    // lib assets (collector/build packages — coverlet.collector ships build/netstandard2.0
    // with no lib), ref/** then build/** carry the same framework-named evidence; when there
    // are no asset folders AT ALL (analyzer packages — StyleCop.Analyzers ships only
    // analyzers/dotnet/), the nuspec's dependency groups are the evidence: a group with a
    // targetFramework names it, and a package with NO dependency groups (or none carrying a
    // targetFramework) admits EVERY framework — that is how NuGet's own engine decides
    // compatibility (nothing exists that could exclude a framework), so recording "any" is
    // the evidence of absence of exclusions, not a guess. Marker entries and non-net segments
    // are not asset evidence.
    private static List<string> GetTfmEvidence(System.IO.Compression.ZipArchive zip)
    {
        foreach (var root in new[] { "lib/", "ref/", "build/" })
        {
            var tfms = zip.Entries
                .Where(e => e.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase) && e.FullName.Length > root.Length)
                .Select(e => e.FullName[root.Length..].Split('/')[0])
                .Where(seg => seg.StartsWith("net", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
            if (tfms.Count > 0) return tfms;
        }
        return NuspecDependencyTfms(zip);
    }

    // the nuspec's dependency groups as the last evidence tier
    private static List<string> NuspecDependencyTfms(System.IO.Compression.ZipArchive zip)
    {
        var nuspec = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/'));
        if (nuspec is null || nuspec.Length > 1024 * 1024) return [];
        try
        {
            using var stream = nuspec.Open();
            var doc = System.Xml.Linq.XDocument.Load(stream, System.Xml.Linq.LoadOptions.None);
            var groups = doc.Descendants()
                .Where(e => e.Name.LocalName.Equals("dependencies", StringComparison.OrdinalIgnoreCase))
                .SelectMany(d => d.Elements().Where(g => g.Name.LocalName.Equals("group", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (groups.Count == 0) return ["any"]; // no dependency groups: nothing excludes any framework
            var declared = groups
                .Select(g => NormalizeTfm(g.Attribute("targetFramework")?.Value ?? ""))
                .Where(n => n.Tfm.Length > 0 && n.Kind != "other") // native/uap groups are not .NET evidence
                .Select(n => n.Tfm)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.Ordinal)
                .ToList();
            if (declared.Count == 0) return ["any"]; // groups exist but none carries .NET evidence: applies to any
            return declared;
        }
        catch (System.Xml.XmlException) { return []; } // unparseable nuspec: undetermined, never guessed
    }

    // Apply one probe result: a stub record upgrades (InUse preserved), a probed record is
    // replaced. Returns true when the version landed UNDETERMINED (onboarding).
    private static bool ApplyProbeRecord(CompatCacheFile cache, string version, List<string> tfms)
    {
        var prior = cache.Versions.FirstOrDefault(old => old.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        var record = new CompatVersion
        {
            Version = version,
            TargetFrameworks = tfms,
            SupportsNetStandard = tfms.Any(t => t.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)),
            InUse = prior?.InUse ?? false, // the estate join's InUse survives the probe (a stub record upgrading to a probed record)
            Note = tfms.Count == 0 ? "no lib assets probed — undetermined (onboarding)" : null,
        };
        record.SupportsNet8 = tfms.Count == 0 ? null : SupportsTfm(tfms, "net8.0");
        record.SupportsNet10 = tfms.Count == 0 ? null : SupportsTfm(tfms, "net10.0");
        cache.Versions.RemoveAll(old => old.Version.Equals(version, StringComparison.OrdinalIgnoreCase));
        cache.Versions.Add(record);
        return tfms.Count == 0;
    }

    // the candidate selection (the brief's selection rule, pure — pinned by selftest): the
    // latest stable version per major PLUS every in-use version, never re-probing evidence
    // that already exists; prerelease strings are not candidates
    internal static List<string> SelectCandidates(IEnumerable<string> versions, IReadOnlySet<string> inUse, IReadOnlySet<string> alreadyProbed)
    {
        return versions
            .Select(v => (v, Core: Apply.ParseCorePublic(v)))
            .Where(p => p.Core is not null)
            .GroupBy(p => p.Core![0])
            .Select(g => g.OrderByDescending(p => p.v, VersionComparer.Instance).First().v)
            .Concat(versions.Where(v => inUse.Contains(v)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(v => !alreadyProbed.Contains(v))
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
    }

    internal static string IndexUrl(string feedBase, string packageId) =>
        feedBase.TrimEnd('/') + "/" + packageId.ToLowerInvariant() + "/index.json";

    internal static string NupkgUrl(string feedBase, string packageId, string version) =>
        feedBase.TrimEnd('/') + "/" + packageId.ToLowerInvariant() + "/" + version.ToLowerInvariant() + "/" + packageId.ToLowerInvariant() + "." + version.ToLowerInvariant() + ".nupkg";

    // ---- the feed path: version lists + candidate nupkg downloads from a NuGet v3 flat
    // container (nuget.org, or your Artifactory's equivalent base). Network lives HERE at the
    // edge — the engine still reads files. The HTTP handler is injectable so the selftest
    // pins the whole loop hermetically (URL casing, JSON shape, download, probe, feed-out).
    public static int ProbeFeed(string feedBase, string cacheDir, string? packageFilter, string? feedOutPath, System.Net.Http.HttpMessageHandler? handler = null)
    {
        using var http = new System.Net.Http.HttpClient(handler ?? new System.Net.Http.HttpClientHandler());
        http.Timeout = TimeSpan.FromSeconds(120); // nupkg downloads reach tens of MB through corporate proxies; 30s starved the itext7-class packages into onboarding stubs
        // corporate feeds authenticate: NUGET_FEED_USER + NUGET_FEED_PASSWORD (password may be
        // an Artifactory API key) — env vars, never command-line secrets (console history)
        var feedUser = Environment.GetEnvironmentVariable("NUGET_FEED_USER");
        var feedPassword = Environment.GetEnvironmentVariable("NUGET_FEED_PASSWORD");
        if (!string.IsNullOrEmpty(feedUser) && !string.IsNullOrEmpty(feedPassword))
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(feedUser + ":" + feedPassword)));
        // accept the SERVICE INDEX (the NuGet.config URL — ".../index.json"): the v3 protocol's
        // entry point advertises its resources; the flat container is the PackageBaseAddress one
        var ids = Directory.GetFiles(Path.GetFullPath(cacheDir), "*.json")
            .Select(f => Path.GetFileName(f)[..^".json".Length])
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        if (packageFilter is not null)
            ids = ids.Where(id => id.Equals(packageFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (ids.Count == 0)
        { Console.Error.WriteLine($"error: no cache entries in {cacheDir} — run the join first (discover-max --join-fixture)"); return 1; }
        var registrationBase = ""; // set when the index speaks JFrog's registrations dialect
        var packageContent = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase); // id-lower -> version-lower -> download url
        if (feedBase.TrimEnd('/').EndsWith("index.json", StringComparison.OrdinalIgnoreCase))
        {
            string resolved = "";
            List<string> advertised;
            List<(string Id, string Type)> resources;
            try
            {
                var indexText = http.GetStringAsync(feedBase).GetAwaiter().GetResult();
                using var indexDoc = System.Text.Json.JsonDocument.Parse(indexText);
                resources = indexDoc.RootElement.GetProperty("resources").EnumerateArray()
                    .Select(r => (Id: r.TryGetProperty("@id", out var rid) ? rid.GetString() ?? "" : "", Type: r.TryGetProperty("@type", out var rt) ? rt.GetString() ?? "" : ""))
                    .ToList();
                advertised = resources.Select(r => r.Type).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();
                resolved = resources
                    .Where(r => r.Type.StartsWith("PackageBaseAddress", StringComparison.Ordinal))
                    .Select(r => r.Id)
                    .FirstOrDefault() ?? "";
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
            {
                Console.Error.WriteLine($"error: the service index at {feedBase} is not fetchable/parseable ({FirstLine(ex.Message)}) — pass the flat-container base directly if you know it");
                return 1;
            }
            // THE JFROG DOOR (verified against a real JFrog cloud trial, 2026-11-13): JFrog's
            // NuGet v3 index advertises NO PackageBaseAddress at all — versions and nupkg
            // downloads live behind RegistrationsBaseUrl instead. Prefer it when present.
            var registrations = resources
                .Where(r => r.Type.StartsWith("RegistrationsBaseUrl", StringComparison.Ordinal))
                .Select(r => r.Id)
                .FirstOrDefault() ?? "";
            if (registrations.Length > 0)
            {
                registrationBase = registrations;
                Console.Error.WriteLine($"feed: no PackageBaseAddress (it has: {string.Join(", ", advertised.Take(6))}) — using the registrations door at {registrationBase}");
            }
            else
            {
                // no registrations either: verify the sibling flat container with a canary on the
                // FIRST cache entry before adopting it — an endpoint that answers is evidence
                var sibling = new Uri(new Uri(feedBase), "flatcontainer/").ToString();
                var canaryId = ids[0].ToLowerInvariant();
                try
                {
                    var canary = http.GetStringAsync(sibling + canaryId + "/index.json").GetAwaiter().GetResult();
                    using var canaryDoc = System.Text.Json.JsonDocument.Parse(canary);
                    if (!canaryDoc.RootElement.TryGetProperty("versions", out _)) throw new System.Text.Json.JsonException("no versions array");
                    resolved = sibling;
                    Console.Error.WriteLine($"note: the index advertises no PackageBaseAddress (it has: {string.Join(", ", advertised.Take(6))}) — the sibling flat container verified via canary on {canaryId}, using it");
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
                {
                    Console.Error.WriteLine($"error: the service index advertises no PackageBaseAddress and no RegistrationsBaseUrl, and the sibling flat container did not answer ({FirstLine(ex.Message)})");
                    Console.Error.WriteLine($"  advertised resources: {string.Join(", ", advertised.Take(10))}");
                    return 1;
                }
            }
            if (resolved.Length > 0)
            {
                Console.Error.WriteLine($"feed: service index resolved to the flat container at {resolved}");
                feedBase = resolved;
            }
        }
        var feedPackages = new List<Drift.FeedPackage>();
        var probed = 0; var failures = 0; var notOnFeed = 0;
        foreach (var id in ids)
        {
            Console.Error.WriteLine($"feed {id}: fetching version list");
            string[] versions;
            Dictionary<string, string> contentUrls = new(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (registrationBase.Length > 0)
                {
                    // the registrations door (JFrog): the registration index lists pages; each page
                    // carries catalogEntry.version + packageContent (the download URL)
                    var regText = http.GetStringAsync(registrationBase.TrimEnd('/') + "/" + id.ToLowerInvariant() + "/index.json").GetAwaiter().GetResult();
                    using var regDoc = System.Text.Json.JsonDocument.Parse(regText);
                    var pages = new List<string>();
                    var collected = new List<(string Version, string Content)>();
                    foreach (var page in regDoc.RootElement.GetProperty("items").EnumerateArray())
                    {
                        if (page.TryGetProperty("items", out var inline))
                        {
                            foreach (var leaf in inline.EnumerateArray())
                            {
                                var ce = leaf.GetProperty("catalogEntry");
                                collected.Add((ce.GetProperty("version").GetString() ?? "", ce.TryGetProperty("packageContent", out var pc) ? pc.GetString() ?? "" : leaf.TryGetProperty("packageContent", out var pc2) ? pc2.GetString() ?? "" : ""));
                            }
                        }
                        else pages.Add(page.GetProperty("@id").GetString() ?? "");
                    }
                    foreach (var pageUrl in pages.Distinct(StringComparer.Ordinal))
                    {
                        var pageText = http.GetStringAsync(pageUrl).GetAwaiter().GetResult();
                        using var pageDoc = System.Text.Json.JsonDocument.Parse(pageText);
                        foreach (var leaf in pageDoc.RootElement.GetProperty("items").EnumerateArray())
                        {
                            var ce = leaf.GetProperty("catalogEntry");
                            collected.Add((ce.GetProperty("version").GetString() ?? "", ce.TryGetProperty("packageContent", out var pc) ? pc.GetString() ?? "" : leaf.TryGetProperty("packageContent", out var pc2) ? pc2.GetString() ?? "" : ""));
                        }
                    }
                    versions = collected.Select(c => c.Version).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    foreach (var (v, url) in collected) if (url.Length > 0 && !contentUrls.ContainsKey(v.ToLowerInvariant())) contentUrls[v.ToLowerInvariant()] = url;
                }
                else
                {
                    var text = http.GetStringAsync(IndexUrl(feedBase, id)).GetAwaiter().GetResult();
                    using var doc = System.Text.Json.JsonDocument.Parse(text);
                    versions = (doc.RootElement.TryGetProperty("versions", out var arr) ? arr.EnumerateArray().Select(v => v.GetString() ?? "") : Enumerable.Empty<string>())
                        .Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
            {
                // the REASON travels: a systemic failure (auth/TLS/base-shape) must say so on
                // every line, never hide behind the exception type name
                var status = (ex as System.Net.Http.HttpRequestException)?.StatusCode;
                var hint = status switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "401 — feed credentials required (set NUGET_FEED_USER / NUGET_FEED_PASSWORD)",
                    System.Net.HttpStatusCode.Forbidden => "403 — credentials lack read on this repo",
                    System.Net.HttpStatusCode.NotFound => "404 — wrong base shape (the flat-container base ends right before {id}/index.json)",
                    null => "transport/TLS/proxy failure",
                    _ => $"{(int)status} {status}",
                };
                if (ex is not System.Net.Http.HttpRequestException) hint = $"feed returned unparseable data ({ex.GetType().Name}: {FirstLine(ex.Message)})";
                Console.Error.WriteLine($"note: {id}: not fetchable from this feed — {hint}: {FirstLine(ex.Message)}");
                notOnFeed++;
                continue;
            }
            var cache = Load(cacheDir, id) ?? new CompatCacheFile { Id = id };
            var merged = cache.AllVersionsSimple.Concat(versions).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
            cache.AllVersionsSimple = merged;
            cache.TotalVersionsAvailable = merged.Count;
            cache.FetchedUtc = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            var latestStable = versions.Where(v => Apply.ParseCorePublic(v) is not null)
                .OrderByDescending(v => v, VersionComparer.Instance).FirstOrDefault();
            if (latestStable is not null) feedPackages.Add(new Drift.FeedPackage { PackageId = cache.Id, Version = latestStable }); // the cache's CANONICAL casing, never the filename's lowercase
            var known = cache.Versions.Where(v => v.TargetFrameworks.Count > 0).Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var inUse = cache.Versions.Where(v => v.InUse).Select(v => v.Version).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidates = SelectCandidates(versions, inUse, known);
            var probedHere = 0;
            foreach (var version in candidates)
            {
                byte[] bytes;
                try
                {
                    var url = contentUrls.TryGetValue(version.ToLowerInvariant(), out var content) ? content : NupkgUrl(feedBase, id, version);
                    bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
                {
                    Console.Error.WriteLine($"warning: {id} {version}: download failed ({ex.GetType().Name}) — skipped (onboarding)");
                    failures++;
                    continue;
                }
                List<string> tfms;
                try
                {
                    using var stream = new MemoryStream(bytes);
                    using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                    tfms = GetTfmEvidence(zip);
                }
                catch (Exception ex) when (ex is IOException or System.IO.InvalidDataException)
                {
                    Console.Error.WriteLine($"warning: {id} {version}: unreadable nupkg ({ex.Message}) — skipped (onboarding)");
                    failures++;
                    continue;
                }
                if (ApplyProbeRecord(cache, version, tfms)) failures++; else { probed++; probedHere++; }
            }
            foreach (var grp in cache.Versions.Where(v => Apply.ParseCorePublic(v.Version) is not null).GroupBy(v => Apply.ParseCorePublic(v.Version)![0]))
            {
                var latest = grp.OrderByDescending(v => v.Version, VersionComparer.Instance).First();
                foreach (var v in grp) v.IsLatestForMajor = v.Version == latest.Version;
            }
            Program.WriteArtifactFile(CachePath(cacheDir, id),
                JsonSerializer.Serialize(cache, WriteOpts).ReplaceLineEndings("\n") + "\n");
            Console.Error.WriteLine($"discovered {id}: feed carries {versions.Length} versions, probed {probedHere} candidate(s), cache now holds {cache.Versions.Count} probed record(s)");
        }
        if (feedOutPath is not null && feedPackages.Count > 0)
            Program.WriteArtifactFile(feedOutPath, JsonSerializer.Serialize(
                new Drift.FeedFile { SchemaVersion = "feed-versions.v1", Source = feedBase, AsOf = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture), Packages = feedPackages.OrderBy(p => p.PackageId, StringComparer.Ordinal).ToList() }, WriteOpts).ReplaceLineEndings("\n") + "\n");
        Console.Error.WriteLine($"feed probe done: {probed} candidate(s) probed, {notOnFeed} package(s) not on this feed, {failures} download/parse failure(s)");
        return 0;
    }

    private static string FirstLine(string s)
    {
        var cut = s.IndexOfAny(['\r', '\n']);
        var line = cut < 0 ? s : s[..cut];
        return line.Length > 220 ? line[..220] + "…" : line;
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

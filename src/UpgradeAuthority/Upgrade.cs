using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// ---- the upgrade-plan layer, slice 2 (docs/UPGRADE-PLAN-DESIGN.md): the estate join,
// the per-repo update driver (surface 1), and the per-TFM estate view (surface 2).
// The engine's laws are mirrored, not weakened: package identity joins OrdinalIgnoreCase
// (SPEC-021 §2(c)), already-satisfied sites never downgrade, prerelease/floating sites stay
// unresolved (N3), and every edit re-checks the actual file — 0 occurrences = stale evidence,
// >1 = ambiguous — the Apply law, same refusals, never a guess.

public static class Upgrade
{
    private static readonly JsonSerializerOptions ReadOpts = new() { PropertyNameCaseInsensitive = true, Encoder = StableJsonEncoder.Instance };
    private static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Encoder = StableJsonEncoder.Instance };

    // full 4-part rank (CompareCore orders by the first differing part only — it cannot
    // order 15.0.2 vs 15.0.4 as a comparison key)
    internal static int Rank(string a, string b)
    {
        var pa = Apply.ParseCorePublic(a); var pb = Apply.ParseCorePublic(b);
        if (pa is null || pb is null) return string.CompareOrdinal(a, b);
        for (var i = 0; i < 4; i++)
        {
            var x = i < pa.Length ? pa[i] : 0; var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static bool TryLoadEvidence(string fixtureDir, out PackageEvidence? evidence, out string error)
    {
        evidence = null; error = "";
        var evidencePath = Path.Combine(Path.GetFullPath(fixtureDir), "input", "package-evidence.v0.json");
        if (!File.Exists(evidencePath)) { error = $"fixture has no input/package-evidence.v0.json: {fixtureDir}"; return false; }
        try { evidence = JsonSerializer.Deserialize<PackageEvidence>(File.ReadAllText(evidencePath), ReadOpts); }
        catch (JsonException ex) { error = $"package-evidence is not valid JSON: {ex.Message}"; return false; }
        if (evidence is null) { error = "package-evidence is empty"; return false; }
        return true;
    }

    internal static Dictionary<string, Pegs.Resolved> LoadPegs(string? master, string? estate)
    {
        if (master is null && estate is null) return new Dictionary<string, Pegs.Resolved>(StringComparer.OrdinalIgnoreCase);
        var m = master is null ? new PegsFile() : Pegs.Load(master);
        var e = estate is null ? new PegsFile() : Pegs.Load(estate);
        return Pegs.Merge(m, e).ByPackage;
    }

    // ------------------------------------------------------------------ estate join ----
    // Fill usedVersions/usedByRepos/InUse from the fixture's live pins. The join is a fresh
    // projection of the CURRENT fixture: touched packages get their joins recomputed; probed
    // records are never rewritten; a pinned-but-unprobed version gains a stub record
    // (undetermined TFMs — onboarding territory) carrying InUse so the next probe preserves it.
    public static int Join(string cacheDir, string fixtureDir)
    {
        if (!TryLoadEvidence(fixtureDir, out var evidence, out var error)) { Console.Error.WriteLine($"error: {error}"); return 1; }
        Directory.CreateDirectory(cacheDir);
        var touched = new SortedDictionary<string, (Dictionary<string, SortedSet<string>> ByVersion, SortedSet<string> Repos)>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in evidence!.Facts)
        {
            if (fact.PackageId.Length == 0 || fact.Repo.Length == 0) continue;
            if (!touched.TryGetValue(fact.PackageId, out var entry))
                entry = touched[fact.PackageId] = (new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase), new SortedSet<string>(StringComparer.Ordinal));
            if (fact.DeclaredConstraint.Length > 0 && Apply.ParseCorePublic(fact.DeclaredConstraint) is not null)
            {
                if (!entry.ByVersion.TryGetValue(fact.DeclaredConstraint, out var repos)) entry.ByVersion[fact.DeclaredConstraint] = repos = new SortedSet<string>(StringComparer.Ordinal);
                repos.Add(fact.Repo);
            }
            entry.Repos.Add(fact.Repo);
        }
        var joined = 0;
        foreach (var (packageId, entry) in touched)
        {
            var cache = CompatCache.Load(cacheDir, packageId) ?? new CompatCacheFile { Id = packageId };
            cache.UsedVersions = entry.ByVersion.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
            cache.UsedByRepos = entry.Repos.ToList();
            var pinnedVersions = entry.ByVersion.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var record in cache.Versions) record.InUse = pinnedVersions.Contains(record.Version);
            foreach (var version in pinnedVersions.Where(v => !cache.Versions.Any(r => r.Version.Equals(v, StringComparison.OrdinalIgnoreCase))))
                cache.Versions.Add(new CompatVersion { Version = version, InUse = true, Note = "in use but unprobed — TFMs undetermined (onboarding)" });
            Program.WriteArtifactFile(CompatCache.CachePath(cacheDir, packageId), JsonSerializer.Serialize(cache, WriteOpts).ReplaceLineEndings("\n") + "\n");
            joined++;
        }
        Console.Error.WriteLine($"joined {joined} package(s) from the fixture into the cache ({cacheDir})");
        return 0;
    }

    // Repo TFM evidence, three channels (any one suffices): the lockfile envelope carries a tfm
    // per row; the repo-frameworks channel carries the csproj's own TargetFrameworkDeclared
    // facts (the estate-wide source — no lockfiles or build output needed); a fact's own tfm
    // field is the rare third. A csproj PackageReference names no framework, so real estates
    // rely on the first two.
    internal static Dictionary<string, List<string>> LoadRepoTfms(string fixtureDir)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var lockPath = Path.Combine(Path.GetFullPath(fixtureDir), "input", "lockfile-rows.v2.json");
        if (File.Exists(lockPath))
        {
            try
            {
                var v2 = JsonSerializer.Deserialize<LockfileRowsV2>(File.ReadAllText(lockPath), ReadOpts);
                if (v2 is not null)
                    foreach (var repo in v2.Repos)
                    {
                        var tfms = repo.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Tfm)).Select(r => r.Tfm!.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.Ordinal).ToList();
                        if (tfms.Count > 0) map[repo.Repo] = tfms;
                    }
            }
            catch (JsonException) { /* unparseable lockfile channel: no TFM evidence — never guessed */ }
        }
        var fwPath = Path.Combine(Path.GetFullPath(fixtureDir), "input", "repo-frameworks.v0.json");
        if (File.Exists(fwPath))
        {
            try
            {
                var fw = JsonSerializer.Deserialize<RepoFrameworksFile>(File.ReadAllText(fwPath), ReadOpts);
                if (fw is not null)
                    foreach (var repo in fw.Repos)
                    {
                        var tfms = repo.Tfms.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.Ordinal).ToList();
                        if (tfms.Count == 0) continue;
                        if (map.TryGetValue(repo.Repo, out var existing)) existing.AddRange(tfms.Where(t => !existing.Contains(t, StringComparer.OrdinalIgnoreCase)));
                        else map[repo.Repo] = tfms;
                    }
            }
            catch (JsonException) { /* unparseable frameworks channel: the lockfile channel stands */ }
        }
        return map;
    }

    // ------------------------------------------------------------ shared resolution ----
    internal sealed record PackageResolution(
        string PackageName, string? PotentialMax, string? AllowedMax, Pegs.Resolved? Peg,
        string Status, bool Pegged, bool LicenseShift, string? Note, IReadOnlyList<string> Tfms);

    // Resolve one consumed package for a repo whose evidenced TFM set is tfms: potentialMax
    // from the cheap feed list; allowedMax = min over TFMs of the compat ceiling under the
    // peg bound (min-with-unknown = unknown — one TFM with no compatible probed version
    // blocks the whole package); a forceMax floor above the ceiling is a typed violation.
    internal static PackageResolution Resolve(CompatCacheFile? cache, IReadOnlyList<string> tfms, Pegs.Resolved? peg, IReadOnlyList<string> pins)
    {
        string? potentialMax = null;
        if (cache is not null)
            potentialMax = cache.AllVersionsSimple.Where(v => Apply.ParseCorePublic(v) is not null)
                .OrderByDescending(v => v, Comparer<string>.Create((a, b) => Rank(a, b))).FirstOrDefault();
        if (cache is null)
            return new PackageResolution("", potentialMax, null, peg, "unknown-package", peg is not null, false, "no compatibility cache entry — run discover-max (onboarding)", tfms);
        if (tfms.Count == 0)
            return new PackageResolution(cache.Id, potentialMax, null, peg, "tfm-unknown", peg is not null, LicenseShift(cache), "the repo's target framework is not evidenced (no tfm on any fact) — cannot compute a ceiling", tfms);
        string? ceiling = null; string? blocked = null;
        foreach (var tfm in tfms)
        {
            var one = CompatCache.Ceiling(cache, tfm, peg?.BoundKind, peg?.BoundVersion);
            if (one is null) { blocked = tfm; break; }
            if (ceiling is null || Rank(one, ceiling) < 0) ceiling = one;
        }
        if (blocked is not null)
            return new PackageResolution(cache.Id, potentialMax, null, peg, "compat-blocked", peg is not null, LicenseShift(cache), $"no probed version of this package admits {blocked} (onboarding or an empty peg-bounded set)", tfms);
        var floor = peg?.FloorVersion;
        if (floor is not null && (ceiling is null || Rank(ceiling, floor) < 0))
            return new PackageResolution(cache.Id, potentialMax, ceiling, peg, "floor-violation", true, LicenseShift(cache), $"forceMax floor {floor} is unreachable on this TFM set (ceiling {ceiling})", tfms);
        if (pins.Count > 0)
        {
            var highest = pins.OrderByDescending(v => v, Comparer<string>.Create((a, b) => Rank(a, b))).First();
            if (Rank(highest, ceiling!) > 0)
                return new PackageResolution(cache.Id, potentialMax, ceiling, peg, "pin-above-ceiling", peg is not null, LicenseShift(cache), $"a pin ({highest}) sits above the computed ceiling ({ceiling}) — the cache is stale or predates the pin; NEVER downgraded, refresh the cache", tfms);
            if (Rank(highest, ceiling!) == 0)
                return new PackageResolution(cache.Id, potentialMax, ceiling, peg, "current", peg is not null, LicenseShift(cache), null, tfms);
        }
        return new PackageResolution(cache.Id, potentialMax, ceiling, peg, "would-update", peg is not null, LicenseShift(cache), null, tfms);
    }

    private static bool LicenseShift(CompatCacheFile cache)
    {
        var licenses = cache.Versions.Where(v => v.License is { Length: > 0 }).Select(v => v.License!).Distinct(StringComparer.Ordinal).ToList();
        return licenses.Count > 1;
    }

    private static Dictionary<string, object?> PegRow(Pegs.Resolved? peg) => peg is null
        ? null!
        : new Dictionary<string, object?> { ["kind"] = peg.BoundKind ?? "forceMax", ["version"] = peg.BoundVersion ?? peg.FloorVersion };

    // ------------------------------------------------------- surface 1: ua update ----
    public sealed record UpdateOptions(string FixtureDir, string Repo, string ReposRoot, string CacheDir, string? MasterPegs, string? EstatePegs, bool Apply, string? OutPath, bool UpdateDotNet);

    public static (int Rc, string? Text) Update(UpdateOptions o)
    {
        if (o.UpdateDotNet)
        { Console.Error.WriteLine("error: --update-dot-net is RESERVED in V0 — TFM-touching moves need compatibility proof plus an explicit hand (the brief: -keep-dot-net is the only V0 behavior)"); return (1, null); }
        if (!TryLoadEvidence(o.FixtureDir, out var evidence, out var error)) { Console.Error.WriteLine($"error: {error}"); return (1, null); }
        var repoDir = Path.Combine(Path.GetFullPath(o.ReposRoot), o.Repo);
        if (!Directory.Exists(repoDir))
        { Console.Error.WriteLine($"error: repo checkout not found: {repoDir} (--repos-root + the repo's folder name)"); return (1, null); }
        var pegs = LoadPegs(o.MasterPegs, o.EstatePegs);
        var repoFacts = evidence!.Facts.Where(f => string.Equals(f.Repo, o.Repo, StringComparison.Ordinal)).ToList();
        if (repoFacts.Count == 0)
        { Console.Error.WriteLine($"error: repo '{o.Repo}' has no facts in the fixture (repo keys are exact — check spelling/leaf form)"); return (1, null); }
        var repoTfms = LoadRepoTfms(o.FixtureDir); // the lockfile channel: where a repo's TFMs are actually evidenced
        var tfms = repoFacts.Where(f => f.Tfm is { Length: > 0 }).Select(f => f.Tfm!)
            .Concat(repoTfms.TryGetValue(o.Repo, out var lt) ? lt : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.Ordinal).ToList();

        var packageRows = new List<Dictionary<string, object?>>();
        foreach (var group in repoFacts.GroupBy(f => f.PackageId, PkgComparer.Instance).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var cache = CompatCache.Load(o.CacheDir, group.Key);
            var peg = pegs.TryGetValue(group.Key, out var p) ? p : null;
            var pins = group.Where(f => f.DeclaredConstraint.Length > 0 && Apply.ParseCorePublic(f.DeclaredConstraint) is not null)
                .Select(f => f.DeclaredConstraint).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var resolution = Resolve(cache, tfms, peg, pins);
            var row = new Dictionary<string, object?>
            {
                ["packageName"] = group.Key,
                ["potentialMax"] = resolution.PotentialMax,
                ["allowedMax"] = resolution.AllowedMax,
                ["pegged"] = resolution.Pegged,
                ["peg"] = PegRow(resolution.Peg),
                ["status"] = resolution.Status,
                ["licenseShift"] = resolution.LicenseShift,
                ["note"] = resolution.Note,
            };
            var edits = new List<Dictionary<string, object?>>();
            if (resolution.AllowedMax is { } target && resolution.Status is not ("pin-above-ceiling" or "floor-violation"))
                foreach (var fact in group.Where(f => f.DeclaredConstraint.Length > 0 && f.Line is not null))
                {
                    var cmp = Apply.CompareCore(fact.DeclaredConstraint, target);
                    if (cmp is null || cmp >= 0) continue; // prerelease/floating = unresolved (N3); at-or-above = never a downgrade
                    var edit = new Dictionary<string, object?>
                    {
                        ["path"] = fact.Path, ["line"] = fact.Line, ["attribute"] = AttrName(fact),
                        ["fromVersion"] = fact.DeclaredConstraint, ["toVersion"] = target,
                        ["constraintSource"] = fact.ConstraintSource,
                    };
                    if (o.Apply)
                    {
                        var (applied, why) = TryEdit(repoDir, fact, target);
                        edit["applied"] = applied;
                        if (!applied) edit["refusal"] = why;
                    }
                    edits.Add(edit);
                }
            row["edits"] = edits;
            packageRows.Add(row);
        }
        var report = new Dictionary<string, object?>
        {
            ["schemaVersion"] = "repo-update.v0",
            ["repo"] = o.Repo,
            ["mode"] = o.Apply ? "applied" : "plan",
            ["tfms"] = tfms,
            ["packages"] = packageRows,
        };
        return (0, JsonSerializer.Serialize(report, WriteOpts).ReplaceLineEndings("\n") + "\n");
    }

    // E2's attribute spells "PackageVersion/Version" in the evidence; the FILE attribute is "Version"
    private static string AttrName(Fact fact) => fact.ConstraintSource switch
    {
        "Directory.Packages.props" => "Version",
        "VersionOverride" => "VersionOverride",
        _ => fact.Format == "packages.config" ? "version" : "Version",
    };

    // The edit execution, mirroring Apply's guards: control-character refusal, checkout
    // containment (symlink chain resolved), the evidence window (±2 around the span), an
    // element naming the package, ONE occurrence of attr="old" inside it — 0 = stale,
    // >1 = ambiguous, else replace in place. Newline bytes are the file's own (split/join on
    // '\n' preserves CRLF carriers untouched).
    internal static (bool Applied, string Why) TryEdit(string repoDir, Fact fact, string newVersion)
    {
        if (fact.Path.Any(char.IsControl)) return (false, "stale evidence: edit path contains control characters — refused");
        var checkoutRoot = ResolveLinkChain(Path.GetFullPath(repoDir));
        var full = ResolveLinkChain(Path.GetFullPath(Path.Combine(Path.GetFullPath(repoDir), fact.Path)));
        if (!full.StartsWith(checkoutRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) && full != checkoutRoot)
            return (false, $"stale evidence: {fact.Path} resolves outside the checkout — refused");
        if (!File.Exists(full)) return (false, $"stale evidence: {fact.Path} does not exist in the checkout");
        var attr = AttrName(fact);
        var namers = fact.Format == "packages.config"
            ? new[] { $"id=\"{fact.PackageId}\"", $"id='{fact.PackageId}'" }
            : new[] { $"Include=\"{fact.PackageId}\"", $"Include='{fact.PackageId}'", $"Update=\"{fact.PackageId}\"", $"Update='{fact.PackageId}'" };
        var patterns = new List<(string Old, string New)>
        {
            ($"{attr}=\"{fact.DeclaredConstraint}\"", $"{attr}=\"{newVersion}\""),
            ($"{attr}='{fact.DeclaredConstraint}'", $"{attr}='{newVersion}'"),
            ($"<{attr}>{fact.DeclaredConstraint}</{attr}>", $"<{attr}>{newVersion}</{attr}>"),
        };
        string text;
        try { text = File.ReadAllText(full); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (false, $"unreadable: {ex.Message}"); }
        var lines = text.Split('\n');
        var start = Math.Max(1, (fact.Line ?? 1) - 2);
        var end = Math.Min(lines.Length, (fact.EndLine ?? fact.Line ?? 1) + 2);
        var hits = new List<(int LineNo, int Col, string Old, string New)>();
        for (var i = start; i <= end; i++)
        {
            var line = lines[i - 1];
            if (!namers.Any(n => line.Contains(n, StringComparison.Ordinal))) continue;
            foreach (var (oldFragment, newFragment) in patterns)
                for (var idx = line.IndexOf(oldFragment, StringComparison.Ordinal); idx >= 0; idx = line.IndexOf(oldFragment, idx + 1, StringComparison.Ordinal))
                    hits.Add((i, idx, oldFragment, newFragment));
        }
        if (hits.Count == 0)
            return (false, $"stale evidence: {fact.Path} line ~{fact.Line}: no occurrence of {attr}=\"{fact.DeclaredConstraint}\" naming {fact.PackageId} within the evidence window");
        if (hits.Count > 1)
            return (false, $"ambiguous site: {fact.Path} line ~{fact.Line}: {hits.Count} identical occurrences — refusing to guess");
        var (lineNo, col, oldFrag, newFrag) = hits[0];
        lines[lineNo - 1] = lines[lineNo - 1][..col] + newFrag + lines[lineNo - 1][(col + oldFrag.Length)..];
        try { File.WriteAllText(full, string.Join("\n", lines), new System.Text.UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return (false, $"write failed: {ex.Message}"); }
        return (true, "");
    }

    // The symlink-chain resolution (Apply.ResolveSymlinks's job): every component that is a
    // link is resolved to its final target before the containment check.
    private static string ResolveLinkChain(string path)
    {
        try
        {
            var current = path;
            for (var hops = 0; hops < 16; hops++)
            {
                var link = new FileInfo(current).ResolveLinkTarget(returnFinalTarget: false);
                if (link is null) return current;
                current = Path.GetFullPath(link.FullName);
            }
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return path; }
    }

    // ------------------------------------------------- surface 2: ua upgrade-plan ----
    public sealed record ViewOptions(string FixtureDir, string CacheDir, string? MasterPegs, string? EstatePegs, string? OutPath);

    public static (int Rc, string? Text) View(ViewOptions o)
    {
        if (!TryLoadEvidence(o.FixtureDir, out var evidence, out var error)) { Console.Error.WriteLine($"error: {error}"); return (1, null); }
        var pegs = LoadPegs(o.MasterPegs, o.EstatePegs);
        var repoTfms = LoadRepoTfms(o.FixtureDir); // the lockfile channel: where a repo's TFMs are actually evidenced
        // A pin applies to EVERY framework its repo targets: each fact expands into one row per
        // evidenced TFM (the fact's own tfm when present, else all of the repo's lockfile TFMs),
        // so a multi-framework repo appears in each of its groups. A repo with no TFM evidence
        // lands in the explicit "(tfm-unknown)" group — visible, never dropped (the rev3 law).
        var groups = new SortedDictionary<string, List<Dictionary<string, object?>>>(StringComparer.Ordinal);
        foreach (var g in evidence!.Facts
            .SelectMany(f => f.Tfm is { Length: > 0 }
                    ? new[] { f.Tfm.Trim() }
                    : repoTfms.TryGetValue(f.Repo, out var lt) && lt.Count > 0 ? lt.ToArray() : new[] { "(tfm-unknown)" },
                (f, tfm) => (Fact: f, Tfm: tfm))
            .GroupBy(x => x.Tfm.ToLowerInvariant())
            .OrderBy(g => g.Key == "(tfm-unknown)" ? "~" : g.Key, StringComparer.Ordinal))
        {
            var rows = new List<Dictionary<string, object?>>();
            foreach (var pkg in g.GroupBy(x => x.Fact.PackageId, PkgComparer.Instance).OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                var cache = CompatCache.Load(o.CacheDir, pkg.Key);
                var peg = pegs.TryGetValue(pkg.Key, out var p) ? p : null;
                var pins = pkg.Select(x => x.Fact).Where(f => f.DeclaredConstraint.Length > 0 && Apply.ParseCorePublic(f.DeclaredConstraint) is not null).Select(f => f.DeclaredConstraint).ToList();
                var pinCounts = pins.GroupBy(v => v, StringComparer.OrdinalIgnoreCase).ToDictionary(k => k.Key, k => k.Count(), StringComparer.OrdinalIgnoreCase);
                var resolution = Resolve(cache, g.Key == "(tfm-unknown)" ? Array.Empty<string>() : new[] { g.Key }, peg, pinCounts.Keys.ToList());
                rows.Add(new Dictionary<string, object?>
                {
                    ["packageName"] = pkg.Key,
                    ["pinCounts"] = pinCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                    ["repos"] = pkg.Select(x => x.Fact.Repo).Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal).ToList(),
                    ["potentialMax"] = resolution.PotentialMax,
                    ["allowedMax"] = resolution.AllowedMax,
                    ["pegged"] = resolution.Pegged,
                    ["peg"] = PegRow(resolution.Peg),
                    ["status"] = resolution.Status,
                    ["licenseShift"] = resolution.LicenseShift,
                    ["note"] = resolution.Note,
                });
            }
            groups[g.Key] = rows;
        }
        var report = new Dictionary<string, object?> { ["schemaVersion"] = "upgrade-plan.v0", ["groups"] = groups };
        return (0, JsonSerializer.Serialize(report, WriteOpts).ReplaceLineEndings("\n") + "\n");
    }

    // SPEC-021 §2(c): package identity is OrdinalIgnoreCase
    private sealed class PkgComparer : IEqualityComparer<string>
    {
        public static readonly PkgComparer Instance = new();
        public bool Equals(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(string s) => StringComparer.OrdinalIgnoreCase.GetHashCode(s);
    }
}

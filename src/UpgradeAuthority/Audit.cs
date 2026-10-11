using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// SPEC-024: the security-intelligence report. Joins drift's installed-version inventory against an
// operator-provided advisory truth file (advisory-versions.v1 — OSV/GHSA export or hand-curated,
// stated-not-verified-live) and reports WITH the registry boundary: our code exposed vs the
// internet's problem vs unknown ownership. A report, not a gate: affected findings are content.
public static class Audit
{
    public sealed class AdvisoryFile
    {
        [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("asOf")] public string? AsOf { get; set; }
        [JsonPropertyName("advisories")] public List<Advisory> Advisories { get; set; } = new();
    }

    public sealed class Advisory
    {
        [JsonPropertyName("advisoryId")] public string AdvisoryId { get; set; } = "";
        [JsonPropertyName("packageId")] public string PackageId { get; set; } = "";
        [JsonPropertyName("ecosystem")] public string? Ecosystem { get; set; }
        [JsonPropertyName("vulnerableVersions")] public List<string>? VulnerableVersions { get; set; }
        [JsonPropertyName("fixedIn")] public string? FixedIn { get; set; }
        [JsonPropertyName("severity")] public string? Severity { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
    }

    public sealed class ValidatedFile
    {
        public AdvisoryFile File = new();
        public int AdvisoryCount, ConsideredCount;
    }

    public const string ExportGapSeverity = "export-gap";
    public const string ExportGapReason = "export gap: the advisory's vulnerable window could not be soundly exported — manual review";

    // ---- entry: returns (canonical, rc). rc != 0 → canonical null, message already printed. ----
    public static (string? canonical, int rc) Run(string fixtureDir, string? advisoryPath)
    {
        if (advisoryPath is null) { Console.Error.WriteLine("error: --advisories <file> is required"); return (null, 1); }
        if (!File.Exists(advisoryPath)) { Console.Error.WriteLine($"error: --advisories file not found: {advisoryPath}"); return (null, 1); }
        Engine engine;
        try { engine = Program.LoadEngine(fixtureDir); }
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return (null, 3); }
        AdvisoryFile? raw;
        try { raw = JsonSerializer.Deserialize<AdvisoryFile>(File.ReadAllText(advisoryPath), Opts); }
        catch (JsonException ex) { Console.Error.WriteLine($"advisory file {Esc(Path.GetFileName(advisoryPath))}: invalid JSON ({ex.Message})"); return (null, 5); }
        var (parsed, err) = Validate(raw, Path.GetFileName(advisoryPath));
        if (parsed is null) { Console.Error.WriteLine($"error: {err}"); return (null, 5); }
        return (Write(Build(engine, parsed)), 0);
    }

    static string Esc(string s) { var sb = new StringBuilder(); foreach (var c in s) sb.Append(c < 0x20 || c == 0x7f ? $"\\u{(int)c:x4}" : c); return sb.ToString(); }

    static (ValidatedFile?, string?) Validate(AdvisoryFile? raw, string name)
    {
        name = Esc(name);
        if (raw is null) return (null, $"advisory file {name}: unparseable");
        if (raw.SchemaVersion != "advisory-versions.v1")
            return (null, $"advisory file {name}: schemaVersion mismatch — expected 'advisory-versions.v1', got '{Esc(raw.SchemaVersion ?? "")}'");
        if (raw.Source is not ("operator-provided" or "osv-export" or "ghsa-export"))
            return (null, $"advisory file {name}: source must be operator-provided | osv-export | ghsa-export, got '{Esc(raw.Source ?? "")}'");
        if (raw.Advisories is null) return (null, $"advisory file {name}: advisories is null — a truth file with no advisories must not exist at all");
        var considered = 0;
        var advisoryCount = 0;
        var collapsed = 0;
        var byKey = new Dictionary<(string, string), Advisory>();
        foreach (var a in raw.Advisories)
        {
            if (a is null) return (null, $"advisory file {name}: null advisories[] element");
            // identity guards BEFORE the skip note (Esc never dereferences null; the note needs a real id)
            if (string.IsNullOrEmpty(a.AdvisoryId) || string.IsNullOrEmpty(a.PackageId))
                return (null, $"advisory file {name}: empty advisoryId or packageId — every entry needs both");
            if (a.VulnerableVersions is not null && a.VulnerableVersions.Any(v => v is null))
                return (null, $"advisory file {name}: advisory '{Esc(a.AdvisoryId)}' carries a null vulnerableVersions entry — every entry must be a string");
            // dedup FIRST (identical rows collapse before ANY counting/skip — even non-nuget ones)
            var key = (a.AdvisoryId, a.PackageId.ToLowerInvariant());
            if (byKey.TryGetValue(key, out var dup))
            {
                if (SameContent(dup, a)) { collapsed++; continue; } // identical duplicate collapses silently
                return (null, $"advisory file {name}: '{Esc(a.AdvisoryId)}' for '{Esc(a.PackageId)}' appears twice with contradictory content — the truth file contradicts itself");
            }
            advisoryCount++; // the FILE total post-collapse; ecosystem-skipped rows count, consideredCount shows the gap
            if (!string.IsNullOrEmpty(a.Ecosystem) && !string.Equals(a.Ecosystem, "nuget", StringComparison.OrdinalIgnoreCase))
            { Console.Error.WriteLine($"note: advisory '{Esc(a.AdvisoryId)}' ecosystem '{Esc(a.Ecosystem)}' skipped (NuGet-only V0)"); continue; }
            var isExportGap = string.Equals(a.Severity, ExportGapSeverity, StringComparison.Ordinal);
            var hasList = a.VulnerableVersions is { Count: > 0 };
            var hasFixed = a.FixedIn is not null && Apply.ParseCorePublic(a.FixedIn) is not null;
            if (!isExportGap && !hasList && !hasFixed)
                return (null, $"advisory file {name}: advisory '{Esc(a.AdvisoryId)}' has neither a usable vulnerableVersions list nor a parseable fixedIn (and is not an export-gap entry) — it could never classify anything");
            byKey[key] = a;
            considered++;
        }
        raw.Advisories = byKey.Values.OrderBy(a => a.AdvisoryId, StringComparer.Ordinal).ThenBy(a => a.PackageId, StringComparer.Ordinal).ToList();
        // SPEC-024 §4: advisoryCount = the file's POST-COLLAPSE total (skipped-ecosystem rows still count —
        // the FILE total; consideredCount shows the gap; identical duplicates collapse to one)
        return (new ValidatedFile { File = raw, AdvisoryCount = advisoryCount - collapsed, ConsideredCount = considered }, null);
    }

    static bool SameContent(Advisory a, Advisory b) =>
        a.AdvisoryId == b.AdvisoryId && string.Equals(a.PackageId, b.PackageId, StringComparison.OrdinalIgnoreCase)
        && a.FixedIn == b.FixedIn && a.Severity == b.Severity && a.Title == b.Title
        && (a.VulnerableVersions ?? new()).SequenceEqual(b.VulnerableVersions ?? new());

    public sealed class AuditReport
    {
        public string Source = "";
        public string? AsOf;
        public int AdvisoryCount, ConsideredCount;
        public int PackagesObserved;
        public List<AuditPackage> Packages = new();
    }

    public sealed class AuditPackage
    {
        public string PackageId = "";
        public string Boundary = "";
        public string? Anomaly;
        public List<AuditFinding> Findings = new();
    }

    public sealed class AuditFinding
    {
        public string AdvisoryId = "";
        public string? Severity;
        public List<AuditInstallation> Installations = new();
    }

    public sealed class AuditInstallation
    {
        public string Repo = "", Version = "", Evidence = "", Status = "";
        public string? Reason;
    }

    static AuditReport Build(Engine engine, ValidatedFile validated)
    {
        var file = validated.File;
        var advisories = file.Advisories;

        var rows = new List<(string Repo, string PackageKey, string Version, string Evidence)>();
        var lockSpell = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var depsSpell = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var pinSpell = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var skippedNull = 0;
        foreach (var (repo, lockRows) in engine.LockRowsView)
            foreach (var row in lockRows)
            {
                if (string.IsNullOrEmpty(row.Version)) { skippedNull++; continue; }
                NoteSpelling(row.Provenance is null ? lockSpell : depsSpell, row.PackageId);
                rows.Add((repo, row.PackageId, row.Version!, row.Provenance is not null ? "deps.json" : "lockfile"));
            }
        foreach (var (repo, facts) in engine.FactsView)
            foreach (var f in facts)
            {
                if (!Apply.IsExactPin(f.DeclaredConstraint)) continue;
                NoteSpelling(pinSpell, f.PackageId);
                rows.Add((repo, f.PackageId, f.DeclaredConstraint, "declared-pin"));
            }
        if (skippedNull > 0) Console.Error.WriteLine($"note: {skippedNull} lockfile row(s) with no resolved version skipped — unevidenced resolution never counts");
        string SpellingOf(string key) =>
            lockSpell.TryGetValue(key, out var ls) && ls.Count > 0 ? ls.Min!
            : depsSpell.TryGetValue(key, out var ds) && ds.Count > 0 ? ds.Min!
            : pinSpell.TryGetValue(key, out var ps) && ps.Count > 0 ? ps.Min!
            : key;
        static void NoteSpelling(Dictionary<string, SortedSet<string>> sets, string id)
        { if (!sets.TryGetValue(id, out var s)) sets[id] = s = new(StringComparer.Ordinal); s.Add(id); }

        var produced = engine.ProducersView.Select(p => p.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declared = engine.ExternalView.Select(e => e.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string BoundaryOf(string pkg, out string? anomaly)
        {
            anomaly = null;
            if (produced.Contains(pkg)) { if (declared.Contains(pkg)) anomaly = "producedAndDeclaredExternal"; return "internal"; }
            if (declared.Contains(pkg)) return "external";
            return "unknown";
        }

        var observed = rows.Select(r => r.PackageKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var byPackage = new SortedDictionary<string, AuditPackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var advGroup in advisories.GroupBy(a => a.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            var pkgKey = advGroup.Key;
            var pkgRows = rows.Where(r => string.Equals(r.PackageKey, pkgKey, StringComparison.OrdinalIgnoreCase)).ToList();
            var boundary = BoundaryOf(pkgKey, out var anomaly);
            var pkg = new AuditPackage { PackageId = SpellingOf(pkgKey), Boundary = boundary, Anomaly = anomaly };
            foreach (var adv in advGroup)
            {
                var finding = new AuditFinding { AdvisoryId = adv.AdvisoryId, Severity = adv.Severity };
                var isExportGap = string.Equals(adv.Severity, ExportGapSeverity, StringComparison.Ordinal);
                var gapHasPayload = isExportGap && ((adv.VulnerableVersions is { Count: > 0 }) || adv.FixedIn is not null);
                foreach (var grp in pkgRows.GroupBy(r => (r.Repo, r.Version)).OrderBy(g => g.Key.Repo, StringComparer.Ordinal).ThenBy(g => g.Key.Version, StringComparer.Ordinal))
                {
                    var r = grp.OrderBy(x => x.Evidence == "lockfile" ? 0 : x.Evidence == "deps.json" ? 1 : 2).First();
                    var inst = new AuditInstallation { Repo = r.Repo, Version = r.Version, Evidence = r.Evidence };
                    if (isExportGap && !gapHasPayload) { inst.Status = "unclassified"; inst.Reason = ExportGapReason; }
                    else
                    {
                        var listed = adv.VulnerableVersions?.FirstOrDefault(v => ListMatches(v, r.Version));
                        if (listed is not null) inst.Status = "affected";
                        else if (adv.FixedIn is not null)
                        {
                            var cmp = Apply.CompareCore(r.Version, adv.FixedIn);
                            if (cmp < 0) inst.Status = "affected";
                            else if (cmp >= 0) inst.Status = "not-affected";
                            else inst.Status = "unclassified";
                        }
                        else inst.Status = "not-affected"; // list-only, no match — the exact list is exhaustive
                        if (inst.Status == "unclassified")
                        {
                            bool badInstalled = Apply.CompareCore(r.Version, r.Version) is null;
                            bool badFixed = adv.FixedIn is not null && Apply.CompareCore(adv.FixedIn, adv.FixedIn) is null;
                            inst.Reason = (badInstalled, badFixed) switch
                            {
                                (true, true) => $"both installed {r.Version} and advisory fixedIn {adv.FixedIn} not plain numeric pins",
                                (true, false) => $"installed {r.Version} not a plain numeric pin",
                                (false, true) => $"advisory fixedIn {adv.FixedIn} not a plain numeric pin",
                                _ => null,
                            };
                            if (isExportGap && inst.Reason is null) inst.Reason = ExportGapReason;
                        }
                    }
                    finding.Installations.Add(inst);
                }
                if (finding.Installations.Count > 0)
                    pkg.Findings.Add(finding);
            }
            if (pkg.Findings.Count > 0) byPackage[pkg.PackageId] = pkg;
        }

        return new AuditReport { Source = file.Source, AsOf = file.AsOf, AdvisoryCount = validated.AdvisoryCount, ConsideredCount = validated.ConsideredCount, PackagesObserved = observed, Packages = byPackage.Values.OrderBy(p => p.PackageId, StringComparer.Ordinal).ToList() };
    }

    static bool ListMatches(string entry, string installed)
    {
        if (Apply.ParseCorePublic(entry) is not null && Apply.ParseCorePublic(installed) is not null)
            return Apply.CompareCore(installed, entry) == 0;
        return string.Equals(entry, installed, StringComparison.Ordinal);
    }

    public static string Write(AuditReport r)
    {
        var affected = new Dictionary<string, int>();
        var notAffected = new Dictionary<string, int>();
        var unclassified = new Dictionary<string, int>();
        var byBoundary = new Dictionary<string, int> { ["internal"] = 0, ["external"] = 0, ["unknown"] = 0 };
        var withAffected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pkg in r.Packages)
        {
            var worst = new Dictionary<(string, string), int>();
            foreach (var f in pkg.Findings)
                foreach (var i in f.Installations)
                {
                    var rank = i.Status == "affected" ? 2 : i.Status == "unclassified" ? 1 : 0;
                    var key = (i.Repo, i.Version);
                    if (!worst.TryGetValue(key, out var prev) || rank > prev) worst[key] = rank;
                    if (rank == 2) withAffected.Add(f.AdvisoryId);
                }
            affected[pkg.PackageId] = worst.Values.Count(v => v == 2);
            unclassified[pkg.PackageId] = worst.Values.Count(v => v == 1);
            notAffected[pkg.PackageId] = worst.Values.Count(v => v == 0);
            byBoundary[pkg.Boundary] += worst.Values.Count(v => v == 2);
        }

        var nAffected = affected.Values.Sum();
        var nNot = notAffected.Values.Sum();
        var nUnc = unclassified.Values.Sum();
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"schemaVersion\": \"audit.v1\",\n");
        sb.Append("  \"advisories\": {\n");
        sb.Append($"    \"source\": {Q(r.Source)},\n");
        if (r.AsOf is not null) sb.Append($"    \"asOf\": {Q(r.AsOf)},\n");
        sb.Append($"    \"advisoryCount\": {r.AdvisoryCount},\n");
        sb.Append($"    \"consideredCount\": {r.ConsideredCount}\n");
        sb.Append("  },\n");
        sb.Append("  \"summary\": {\n");
        sb.Append($"    \"packagesObserved\": {r.PackagesObserved},\n");
        sb.Append($"    \"packagesListed\": {r.Packages.Count},\n");
        sb.Append($"    \"affectedInstallations\": {nAffected},\n");
        sb.Append($"    \"notAffectedInstallations\": {nNot},\n");
        sb.Append($"    \"unclassifiedInstallations\": {nUnc},\n");
        sb.Append("    \"byBoundaryAffected\": {\n");
        sb.Append($"      \"internal\": {byBoundary["internal"]},\n");
        sb.Append($"      \"external\": {byBoundary["external"]},\n");
        sb.Append($"      \"unknown\": {byBoundary["unknown"]}\n");
        sb.Append("    },\n");
        sb.Append($"    \"advisoriesWithAffected\": {withAffected.Count}\n");
        sb.Append("  },\n");
        sb.Append("  \"packages\": ");
        if (r.Packages.Count == 0) sb.Append("[]");
        else
        {
            sb.Append("[\n");
            for (int p = 0; p < r.Packages.Count; p++)
            {
                var pkg = r.Packages[p];
                sb.Append("    {\n");
                sb.Append($"      \"packageId\": {Q(pkg.PackageId)},\n");
                sb.Append($"      \"boundary\": {Q(pkg.Boundary)},\n");
                if (pkg.Anomaly is not null) sb.Append($"      \"anomaly\": {Q(pkg.Anomaly)},\n");
                sb.Append("      \"findings\": [\n");
                for (int f = 0; f < pkg.Findings.Count; f++)
                {
                    var fd = pkg.Findings[f];
                    sb.Append("        {\n");
                    sb.Append($"          \"advisoryId\": {Q(fd.AdvisoryId)}");
                    if (fd.Severity is not null) { sb.Append(",\n"); sb.Append($"          \"severity\": {Q(fd.Severity)}"); }
                    sb.Append(",\n");
                    sb.Append("          \"installations\": [\n");
                    for (int i = 0; i < fd.Installations.Count; i++)
                    {
                        var inst = fd.Installations[i];
                        sb.Append("            {\n");
                        sb.Append($"              \"repo\": {Q(inst.Repo)},\n");
                        sb.Append($"              \"version\": {Q(inst.Version)},\n");
                        sb.Append($"              \"evidence\": {Q(inst.Evidence)},\n");
                        sb.Append($"              \"status\": {Q(inst.Status)}");
                        if (inst.Reason is not null) { sb.Append(",\n"); sb.Append($"              \"reason\": {Q(inst.Reason)}"); }
                        sb.Append("\n");
                        sb.Append("            }").Append(i < fd.Installations.Count - 1 ? "," : "").Append("\n");
                    }
                    sb.Append("          ]\n");
                    sb.Append("        }").Append(f < pkg.Findings.Count - 1 ? "," : "").Append("\n");
                }
                sb.Append("      ]\n");
                sb.Append("    }").Append(p < r.Packages.Count - 1 ? "," : "").Append("\n");
            }
            sb.Append("  ]");
        }
        sb.Append("\n}\n");
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
}

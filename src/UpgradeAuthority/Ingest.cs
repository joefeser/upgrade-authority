using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// SPEC-005: converts REAL tracemap scan output to planner fixture format.
// Input model verified against actual tracemap scan output (not source-code reading):
//   - CPM/Directory.Packages.props constraints arrive as csproj facts with version: ""
//   - dependencyNames is a comma-joined string, not an array
//   - buildStatus/analysisLevel/knownGaps/AnalysisGap facts determine coverage honesty
//   - lockfile rows are per-(lockfile, TFM, package) — versions can differ across TFMs
//   - one repo per scan output directory
public static class Ingest
{
    sealed class TmFact
    {
        [JsonPropertyName("factId")] public string? FactId { get; set; }
        [JsonPropertyName("scanId")] public string? ScanId { get; set; }
        [JsonPropertyName("repo")] public string? Repo { get; set; }
        [JsonPropertyName("commitSha")] public string? CommitSha { get; set; }
        [JsonPropertyName("factType")] public string? FactType { get; set; }
        [JsonPropertyName("ruleId")] public string? RuleIdProp { get; set; }
        [JsonPropertyName("evidenceTier")] public string? EvidenceTier { get; set; }
        [JsonPropertyName("evidence")] public TmEvidence? Evidence { get; set; }
        [JsonPropertyName("properties")] public Dictionary<string, JsonElement>? Properties { get; set; }
    }

    sealed class TmEvidence
    {
        [JsonPropertyName("filePath")] public string? FilePath { get; set; }
        [JsonPropertyName("startLine")] public int? StartLine { get; set; }
        [JsonPropertyName("endLine")] public int? EndLine { get; set; }
    }

    sealed class TmManifest
    {
        [JsonPropertyName("repoName")] public string? RepoName { get; set; }
        [JsonPropertyName("remoteUrl")] public string? RemoteUrl { get; set; }
        [JsonPropertyName("commitSha")] public string? CommitSha { get; set; }
        [JsonPropertyName("buildStatus")] public string? BuildStatus { get; set; }
        [JsonPropertyName("analysisLevel")] public string? AnalysisLevel { get; set; }
        [JsonPropertyName("knownGaps")] public List<string>? KnownGaps { get; set; }
    }

    internal static string NormalizeRepoBasic(string repo) => NormBasic(repo); // SPEC-014: shared normalization
    static string NormBasic(string repo)
    {
        var r = repo.Trim();
        if (r.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) r = r[7..];
        if (r.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) r = r[8..];
        r = r.TrimEnd('/');
        if (r.EndsWith(".git")) r = r[..^4];
        return r.TrimEnd('/');
    }

    static string? Prop(TmFact f, params string[] keys)
    {
        if (f.Properties is null) return null;
        foreach (var k in keys)
            if (f.Properties.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String)
            { var s = v.GetString(); if (!string.IsNullOrEmpty(s)) return s; }
        return null;
    }

    static string RepoKey(TmFact f, TmManifest? manifest) // SPEC-014 identity stays private; EnumerateRepoKeys is the shared surface
    {
        // Identity: remoteUrl when present (full path, org-repo disambiguated); fallback repoName
        var url = manifest?.RemoteUrl;
        if (!string.IsNullOrEmpty(url)) return NormBasic(url);
        return f.Repo ?? manifest?.RepoName ?? "unknown";
    }

    public static int Run(string[] scanDirs, string outDir, string? producerPath, string? ownershipPath, string? deltaPath, string? scansRoot = null, string? scopePath = null)
    {
        var ingestGaps = new List<string>(); // stderr warnings (flat)
        var repoIngestGaps = new List<(string Key, string Value)>(); // per-repo coverage gaps
        var estateIngestGaps = new List<string>(); // estate-wide (missing sidecars, etc.)
        var repoIngestNotes = new List<(string Key, string Value)>(); // per-repo coverage NOTES (SPEC-015: informational, never affect status)
        // SPEC-017 §3: --scope — validated by the one shared validator; explicit dirs are deliberate
        // acts and are never filtered (warned instead), discovery children are skipped but still
        // register their repo name for alternate-snapshot detection (excluding svc must not let
        // svc-net48 sneak in as the "first" snapshot).
        var (scope, scopeRc) = Scope.LoadForCli(scopePath);
        if (scopeRc != 0) return scopeRc;
        if (scope is not null)
            foreach (var d in scanDirs)
                if (!Scope.IsInScope(Path.GetFileName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), scope))
                    Console.Error.WriteLine($"warning: {Path.GetFileName(d)} is out of scope by config — included because explicit");
        // SPEC-011 --scans-root: depth-1 discovery of child scan dirs (facts.ndjson present), name-ordered;
        // manifest-only children warn and are skipped; union with explicit dirs deduped by resolved path.
        if (scansRoot is not null)
        {
            if (!Directory.Exists(scansRoot))
            { Console.Error.WriteLine($"error: --scans-root directory not found: {scansRoot}"); return 1; }
            var root = Path.GetFullPath(scansRoot);
            var seenRepoNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in scanDirs) { var rn0 = TryReadRepoName(d); if (rn0 is not null) seenRepoNames.Add(rn0); } // explicit dirs count too — an alternate snapshot skips even when its twin was explicit
            // Normalize for dedup via the FILESYSTEM's own casing (volumes differ; the OS is not the truth):
            // resolve each existing path to its on-disk spelling, then compare ordinally. Symlinks stay
            // distinct (documented limitation) — resolution follows spelling, not links.
            var explicitSet = new HashSet<string>(scanDirs.Select(CanonicalDirPath), StringComparer.Ordinal);
            foreach (var child in Directory.GetDirectories(root).OrderBy(d => Path.GetFileName(d), StringComparer.Ordinal))
            {
                if (explicitSet.Contains(CanonicalDirPath(child))) continue;
                var childName = Path.GetFileName(child);
                if (IsSwapDir(childName)) { Console.Error.WriteLine($"note: {childName} ignored (scan-estate swap leftover, not a scan)"); continue; } // SPEC-017: .name.tmp-*/.name.old-* are never discovered; arbitrary dot-named scans stay discoverable (SPEC-011 contract, PR #2 Baz round 1)
                if (scope is not null && !Scope.IsInScope(childName, scope))
                {
                    var rnX = TryReadRepoName(child);
                    if (rnX is not null) seenRepoNames.Add(rnX); // excluded children still register for alternate detection
                    Console.Error.WriteLine($"note: {childName} skipped by discovery (out of scope by config)");
                    continue;
                }
                // An alternate snapshot of an already-discovered repo is not silently combinable —
                // combining two commits of one repo is a deliberate act (explicit dirs), never discovery.
                var rn = TryReadRepoName(child);
                if (rn is not null && seenRepoNames.Contains(rn))
                { Console.Error.WriteLine($"note: {Path.GetFileName(child)} skipped by discovery (alternate snapshot of '{rn}' — pass it explicitly to combine deliberately)"); continue; } // run-level discovery decision — stderr only (routing it into coverage notes would break SPEC-011 byte-equality: the explicit-dirs equivalent run has no such event)
                if (File.Exists(Path.Combine(child, "facts.ndjson")))
                {
                    if (File.Exists(Path.Combine(child, "scope.v0.json")))
                        Console.Error.WriteLine($"note: scope.v0.json found in scan dir {Path.GetFileName(child)} — ignored (pass --scope <file> explicitly; a stray file in a scan dir must not speak for the run)");
                    scanDirs = scanDirs.Append(child).ToArray();
                    var repoName = TryReadRepoName(child);
                    if (repoName is not null) seenRepoNames.Add(repoName);
                    continue;
                }
                if (File.Exists(Path.Combine(child, "scan-manifest.json")))
                    Console.Error.WriteLine($"warning: {Path.GetFileName(child)} looks like a scan (scan-manifest.json) but has no facts.ndjson — skipped");
            }
        }
        if (scanDirs.Length == 0) { Console.Error.WriteLine("error: at least one tracemap scan directory required"); return 1; }

        // SPEC-011 §3: sidecar auto-discovery is single-dir-only. With 2+ scan dirs, run-wide sidecars
        // are estate-level decisions — first-match discovery would let one repo's leftover file speak
        // for the rest. Each missing explicit flag is a typed error naming it.
        var multiDirMissing = new List<string>(); // deferred multi-dir sidecar check (producer may be satisfied by PackageProduced facts)
        var producerSatisfiedByScan = true;
        if (scanDirs.Length > 1)
        {
            if (producerPath is null) producerSatisfiedByScan = false; // re-checked after facts parse (B1(b))
            if (ownershipPath is null) multiDirMissing.Add("--ownership");
            if (deltaPath is null) multiDirMissing.Add("--delta");
        }

        // Parse all scans (multi-dir: one per repo)
        var manifests = new List<(string dir, TmManifest? manifest)>();
        var allFacts = new List<(TmFact fact, TmManifest? manifest, string dir)>();
                                
        foreach (var dir in scanDirs)
        {
            var factsPath = Path.Combine(dir, "facts.ndjson");
            if (!File.Exists(factsPath)) { Console.Error.WriteLine($"error: missing {factsPath}"); return 1; }
            var manifestPath = Path.Combine(dir, "scan-manifest.json");
            TmManifest? manifest = null;
            if (File.Exists(manifestPath))
                manifest = JsonSerializer.Deserialize<TmManifest>(File.ReadAllText(manifestPath), JsonOpts);
            manifests.Add((dir, manifest));

            foreach (var line in File.ReadLines(factsPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var f = JsonSerializer.Deserialize<TmFact>(line, JsonOpts);
                if (f is not null) allFacts.Add((f, manifest, dir));
            }
        }

        // Determine identity keys
        var repoKeys = new SortedDictionary<string, (string label, TmManifest? manifest)>(StringComparer.Ordinal);
        foreach (var (f, manifest, dir) in allFacts)
        {
            var key = RepoKey(f, manifest);
            var label = f.Repo ?? manifest?.RepoName ?? key;
            if (!repoKeys.ContainsKey(key)) repoKeys[key] = (label, manifest);
        }
        // Repos from manifests with zero facts
        foreach (var (dir, manifest) in manifests)
        {
            if (manifest?.RepoName is { } rn)
            {
                var key = RepoKey(new TmFact { Repo = rn }, manifest);
                if (!repoKeys.ContainsKey(key)) repoKeys[key] = (rn, manifest);
            }
        }

        // ---- consumer facts (PackageReferenced, non-lockfile) ----
        var evidence = new Dictionary<string, List<Fact>>(StringComparer.Ordinal);
        foreach (var (f, manifest, dir) in allFacts)
        {
            if (f.FactType != "PackageReferenced") continue;
            var mk = Prop(f, "manifestKind");
            if (mk == "packages.lock.json" || mk == "deps.json") continue; // resolution evidence, handled as lockfile-class rows (SPEC-019 §4: build output is NEVER a consumer declaration — it would fabricate rule-b edges, CPM correlations, and apply sites inside bin/)

            var key = RepoKey(f, manifest);
            var pkg = Prop(f, "packageName", "package", "name");
            if (string.IsNullOrEmpty(pkg)) { ingestGaps.Add($"skipped fact {f.FactId}: no packageName"); repoIngestGaps.Add((key, $"skipped fact {f.FactId}: no packageName")); continue; }

            var ecosystem = Prop(f, "ecosystem", "packageManager");
            if (ecosystem is not null && ecosystem != "nuget")
            { ingestGaps.Add($"skipped fact {f.FactId}: ecosystem '{ecosystem}' not supported in V0 (nuget only)"); repoIngestGaps.Add((key, $"ecosystem '{ecosystem}' not supported")); continue; }

            var version = Prop(f, "version");
            var versionOverrideEarly = Prop(f, "versionOverride");
            string constraint;
            if (versionOverrideEarly is not null)
            {
                // An explicit override IS the constraint for this project — NuGet semantics: the
                // override outranks both the central pin and any in-file Version (PR14 Q2).
                constraint = versionOverrideEarly;
            }
            else if (version is null || version == "")
            {
                // CPM central version or redacted — constraint not evidenced
                var vh = Prop(f, "versionHash");
                constraint = vh is not null ? $"redacted:{vh.Replace("version-hash:", "")}" : "";
                if (constraint == "") { ingestGaps.Add($"fact {f.FactId}: constraint not evidenced (CPM/central version or redacted) — {pkg}"); repoIngestGaps.Add((key, $"constraint not evidenced: {pkg}")); }
            }
            else constraint = version;

            var format = mk switch
            {
                "packages.config" => "packages.config",
                _ => "packagereference", // csproj is the only remaining kind for NuGet
            };
            var constraintSource = mk == "packages.config" ? "packages.config" : "Project";
            // SPEC-009 §3a: an explicit per-project override becomes its own constraint source; the
            // override value replaces the (empty under CPM) declared constraint.
            if (versionOverrideEarly is not null)
            {
                constraintSource = "VersionOverride"; // constraint already set to the override above
            }
            var tfm = Prop(f, "targetFramework");
            var path = f.Evidence?.FilePath ?? "unknown-path";
            var sha = f.CommitSha == "unknown" ? null : f.CommitSha;
            var startLine = f.Evidence?.StartLine;
            var endLine = f.Evidence?.EndLine;
            if (startLine is not null && endLine is not null && endLine == startLine) endLine = null; // one-line span ⇒ omitted

            var fact = new Fact
            {
                Repo = key, PackageId = pkg!, DeclaredConstraint = constraint,
                Format = format, ConstraintSource = constraintSource,
                Tfm = tfm, CommitSha = sha, Path = path,
                Line = startLine, EndLine = endLine,
                Projects = mk == "packages.config" ? new List<string>() : new List<string> { path }, // the reference lives in this project file
            };
            if (!evidence.TryGetValue(key, out var list)) evidence[key] = list = new();
            // SPEC-009 §3a occurrence-preserving dedup: same package twice in one project
            // (conditional duplicates, same-line cases) must both survive to apply.
            if (!list.Any(x => x.PackageId == pkg && x.Path == path && x.Line == startLine
                && x.DeclaredConstraint == constraint && x.ConstraintSource == constraintSource)) list.Add(fact);
        }
        foreach (var kv in evidence) kv.Value.Sort((a, b) => string.CompareOrdinal(a.PackageId + "\0" + a.Path + "\0" + Ord2(a.Line) + "\0" + Ord2(a.EndLine), b.PackageId + "\0" + b.Path + "\0" + Ord2(b.Line) + "\0" + Ord2(b.EndLine)));
        static string Ord2(int? n) => n?.ToString() ?? "";

        // ---- central package management pins (SPEC-009 §3a) ----
        // CentralPackageVersionDeclared facts map onto the CPM fact shape the planner already speaks (F7).
        // Correlation: a pin is emitted ONLY when the same repo carries a NON-LOCKFILE reference fact for
        // the same package — lockfile rows are resolutions, not consumer declarations, and an unused pin
        // must never fabricate a consumer edge (rule b) or an unnecessary edit.
        var unusedPins = 0;
        foreach (var (f, manifest, dir) in allFacts)
        {
            if (f.FactType != "CentralPackageVersionDeclared") continue;
            var key = RepoKey(f, manifest);
            var pkg = Prop(f, "packageName", "package", "name");
            if (string.IsNullOrEmpty(pkg)) { ingestGaps.Add($"skipped central pin fact {f.FactId}: no packageName"); repoIngestGaps.Add((key, $"skipped central pin fact: no packageName")); continue; }
            var version = Prop(f, "version");
            if (version is not null && f.Properties is not null && f.Properties.TryGetValue("version", out var vEl) && vEl.ValueKind != JsonValueKind.String)
                version = null; // hashed/unsafe versions never become constraints
            if (string.IsNullOrEmpty(version)) { ingestGaps.Add($"central pin fact {f.FactId}: pin version not evidenced — {pkg}"); repoIngestGaps.Add((key, $"central pin version not evidenced: {pkg}")); continue; }

            var referenced = evidence.TryGetValue(key, out var repoFacts)
                && repoFacts.Any(x => x.PackageId == pkg && x.ConstraintSource != "Directory.Packages.props");
            if (!referenced) { unusedPins++; ingestGaps.Add($"central pin {pkg} {version} in {key}: no non-lockfile reference consumes it — not emitted (an unused pin is not consumer evidence)"); repoIngestNotes.Add((key, $"unused central pin skipped: {pkg} {version} (no reference consumes it)")); continue; }

            var path = f.Evidence?.FilePath ?? "unknown-path";
            var startLine = f.Evidence?.StartLine;
            var endLine = f.Evidence?.EndLine;
            if (startLine is not null && endLine is not null && endLine == startLine) endLine = null;
            var referencing = evidence[key].Where(x => x.PackageId == pkg && x.ConstraintSource != "Directory.Packages.props").Select(x => x.Path).Distinct().OrderBy(p2 => p2, StringComparer.Ordinal).ToList();
            var fact = new Fact
            {
                Repo = key, PackageId = pkg!, DeclaredConstraint = version,
                Format = "cpm", ConstraintSource = "Directory.Packages.props",
                CommitSha = f.CommitSha == "unknown" ? null : f.CommitSha, Path = path,
                Line = startLine, EndLine = endLine,
                Projects = referencing, // fan-out: the projects whose references this pin feeds
            };
            if (!evidence.TryGetValue(key, out var list)) evidence[key] = list = new();
            if (!list.Any(x => x.PackageId == pkg && x.Path == path && x.Line == startLine && x.DeclaredConstraint == version)) list.Add(fact); // occurrence-preserving (conditional duplicate pins)
        }
        foreach (var kv in evidence) kv.Value.Sort((a, b) => string.CompareOrdinal(a.PackageId + "\0" + a.Path + "\0" + Ord2(a.Line) + "\0" + Ord2(a.EndLine), b.PackageId + "\0" + b.Path + "\0" + Ord2(b.Line) + "\0" + Ord2(b.EndLine)));
        if (unusedPins > 0) Console.Error.WriteLine($"note: {unusedPins} central pin(s) skipped — no non-lockfile reference consumes them (not consumer evidence; no edit needed for the delta)");

        // ---- lockfile rows (SPEC-007: keyed by (lockfile, tfm, packageId), emitted as v1) ----
        var lockfiles = new Dictionary<string, List<LockRow>>(StringComparer.Ordinal);
        foreach (var (f, manifest, dir) in allFacts)
        {
            if (f.FactType != "PackageReferenced") continue;
            var factKind = Prop(f, "manifestKind");
            if (factKind != "packages.lock.json" && factKind != "deps.json") continue;
            var key = RepoKey(f, manifest);
            var pkg = Prop(f, "packageName", "package", "name");
            if (string.IsNullOrEmpty(pkg)) { ingestGaps.Add($"skipped lockfile fact {f.FactId}: no packageName"); repoIngestGaps.Add((key, $"skipped lockfile fact: no packageName")); continue; }
            var relation = Prop(f, "dependencyRelation") ?? "unknown";
            var resolved = Prop(f, "resolvedVersion") ?? "";
            var namesStr = Prop(f, "dependencyNames"); // comma-joined string, or null/empty
            var tfm = Prop(f, "targetFramework") ?? "";
            var lockfilePath = factKind == "deps.json"
                ? Prop(f, "manifestPath") ?? f.Evidence?.FilePath ?? "" // SPEC-019 §4: deps.json rows key on the build-output manifest path
                : Prop(f, "lockfilePath") ?? f.Evidence?.FilePath ?? "";
            if (lockfilePath == "")
            { ingestGaps.Add($"skipped lockfile fact {f.FactId}: no lockfilePath — {pkg}"); repoIngestGaps.Add((key, $"skipped lockfile fact: no lockfilePath — {pkg}")); continue; }
            if (resolved == "")
            { ingestGaps.Add($"lockfile fact {f.FactId}: resolved version not evidenced — {pkg}"); repoIngestGaps.Add((key, $"resolved version not evidenced: {pkg}")); }

            var row = new LockRow
            {
                PackageId = pkg!,
                Type = relation switch { "direct" => "direct", "transitive" => "transitive", _ => "unknown" },
                Version = string.IsNullOrEmpty(resolved) ? null : resolved,
                Via = null, // derived below from parent rows' dependencyNames (same lockfile+tfm group only)
                Names = namesStr, // preserve the comma-joined string; planner treats it as child detail
                Lockfile = lockfilePath,
                Tfm = tfm,
                Provenance = factKind == "deps.json"
                    ? new RowProvenance
                    {
                        // SPEC-019 §4: upstream's reserved placeholders pass through UNTOUCHED — ua never overwrites them
                        ManifestSha256 = Prop(f, "manifestSha256") ?? "",
                        Freshness = Prop(f, "freshness") ?? "unknown",
                        BuildCommitSha = Prop(f, "buildCommitSha") ?? "unknown",
                    }
                    : null,
            };
            if (!lockfiles.TryGetValue(key, out var rows)) lockfiles[key] = rows = new();
            // Row identity = (lockfile, tfm, packageId). Cross-lockfile/cross-TFM disagreement is legal
            // (findings, not errors); two rows for ONE resolution group must be identical in every field.
            // packageId compares OrdinalIgnoreCase: NuGet ids are case-insensitive, so case variants of one
            // id in one resolution group are the SAME key (Kiro post-merge verification, 2026-10-03) — and
            // since the fields then differ (case included), SameRow rejects them as conflicting evidence.
            var existing = rows.FirstOrDefault(r => string.Equals(r.PackageId, pkg, StringComparison.OrdinalIgnoreCase) && Ord(r.Lockfile) == Ord(lockfilePath) && Ord(r.Tfm) == Ord(tfm));
            if (existing is null)
                rows.Add(row);
            else if (!SameRow(existing, row))
            {
                Console.Error.WriteLine($"error: package {pkg} in repo {key} has conflicting facts for one resolution group ({lockfilePath}, {tfm}): {DescribeRow(existing)} vs {DescribeRow(row)}; a single (lockfile, TFM, package) resolution cannot disagree with itself — same repo passed to ingest twice (two scans / two commits) is the usual cause; otherwise check for malformed scan facts");
                return 4;
            }
        }
        // Sort to canonical order FIRST (SPEC-007 §8.1) so every downstream FirstOrDefault —
        // including via-parent selection below — is a function of canonical order, never input fact order.
        foreach (var kv in lockfiles) kv.Value.Sort(RowOrder);
        // Derive via: a TRANSITIVE row's parent is a direct row in the SAME (lockfile, tfm) group whose
        // dependencyNames contains the child. Cross-lockfile parenting is never inferred (SPEC-007 §4).
        // When several direct parents name the same child, the canonically-first (packageId-ordinal) parent wins.
        foreach (var (repo, rows) in lockfiles)
        {
            foreach (var row in rows.Where(r => r.Type == "transitive"))
            {
                var parent = rows.FirstOrDefault(r => r.Type == "direct" && r.PackageId != row.PackageId
                    && Ord(r.Lockfile) == Ord(row.Lockfile) && Ord(r.Tfm) == Ord(row.Tfm)
                    && (r.Names as string ?? "").Split(',').Select(n => n.Trim()).Contains(row.PackageId));
                if (parent is not null) row.Via = parent.PackageId;
            }
        }

        // SPEC-008: the multi-repo typed error (PR #6 B2's exit 2) is retired —
        // every repo with lockfile facts flows into one lockfile-rows.v2 output.

        // ---- coverage: SPEC-015 — package-relevant gaps vs compile-health notes ----
        // One classifier, one place. Structural (gapKind/diagnosticKind/ruleId); unknown => Gap.
        var coverage = new List<ScanCoverage>();
        foreach (var (key, (label, manifest)) in repoKeys)
        {
            var gaps = new List<string>();
            var notes = new List<string>();

            void AddGap(string g) { if (!gaps.Contains(g)) gaps.Add(g); }
            void AddNote(string n) { if (!notes.Contains(n)) notes.Add(n); }

            // Fact-level first (so manifest knownGaps can join against classified facts)
            var factClassifications = new List<(string Message, bool IsNote)>();
            foreach (var (f, manifest2, dir) in allFacts)
            {
                if (f.FactType != "AnalysisGap" || RepoKey(f, manifest2) != key) continue;
                var msg = Prop(f, "message") ?? Prop(f, "gapKind") ?? "analysis gap";
                var gapKind = Prop(f, "gapKind");
                var diagnosticKind = Prop(f, "diagnosticKind");
                var ruleId = f.RuleIdProp ?? "";
                bool isNote;
                if (gapKind is not null && gapKind.StartsWith("deps-json-", StringComparison.Ordinal))
                    isNote = gapKind == "deps-json-not-found"; // SPEC-019 §4: no build output = the expected never-built state (Note); every other deps-json-* = evidence lost (Gap)
                else if (gapKind is not null && gapKind.Contains("packages-lock", StringComparison.Ordinal))
                    isNote = false; // lockfile evidence loss
                else if (gapKind is "CompilationDiagnostic" or "WorkspaceDiagnostic" or "SdkResolutionFailed")
                    isNote = true; // compile/build health
                else if (diagnosticKind is "workspace" or "compilation")
                    isNote = true; // the fact declares its own kind (e.g. MSBuildRegistrationFailed)
                else if (gapKind is null && diagnosticKind is null && ruleId.StartsWith("repo.manifest.v1", StringComparison.Ordinal)
                         && (msg.StartsWith("Compiler diagnostic category:", StringComparison.Ordinal) || msg.StartsWith("Workspace diagnostic category:", StringComparison.Ordinal)))
                    isNote = true; // manifest-extractor echo of compile/build health
                else if (gapKind is null && diagnosticKind is null && ruleId.StartsWith("csharp.syntax.", StringComparison.Ordinal))
                    isNote = true; // syntax-level diagnostic — the broken-.cs-file case
                else
                    isNote = false; // conservative default: unknown problems block
                factClassifications.Add((msg, isNote));
                if (isNote) AddNote(msg); else AddGap(msg);
            }

            // Manifest-level honesty — EVERY manifest for this repo key (explicitly combined
            // snapshots each carry their own; a later snapshot's problems must not vanish)
            foreach (var m in manifests.Select(x => x.manifest).Where(m2 => m2 is not null && RepoKey(new TmFact { Repo = m2.RepoName ?? "" }, m2) == key))
                {
                    if (m.BuildStatus is { } bs && bs != "Succeeded")
                        AddNote($"scan buildStatus: {bs}"); // compile/build health (SPEC-015)
                    if (m.AnalysisLevel is { } al && al != "Level1SemanticAnalysis")
                        AddNote($"analysisLevel: {al}"); // semantic depth — V0 never consumes semantic tiers
                    if (m.KnownGaps is { } kg)
                        foreach (var g in kg)
                        {
                            // structural join: the manifest RESTATES a fact we already classified
                            var join = factClassifications.FirstOrDefault(fc => fc.Message == g);
                            if (join != default && join.IsNote) AddNote(g);
                            else AddGap(g); // opaque to us — conservative
                        }
                }
            // No facts at all?
            if (!evidence.ContainsKey(key) && !lockfiles.ContainsKey(key) && !allFacts.Any(x3 => x3.fact.FactType == "PackageProduced" && RepoKey(x3.fact, x3.manifest) == key))
                AddGap("no facts emitted by tracemap");

            coverage.Add(new ScanCoverage
            {
                Repo = key,
                Status = gaps.Count == 0 ? "complete" : "gaps", // notes never affect status
                Gaps = gaps,
                Notes = (notes.Concat(repoIngestNotes.Where(g => g.Key == key).Select(g => g.Value)).Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList() is { Count: > 0 } listed) ? listed : null,
            });
        }

        // ---- validate delta before writing anything (M1) ----
        var deltaFile = deltaPath ?? Path.Combine(scanDirs[0], "delta.json");
        if (!File.Exists(deltaFile))
        {
            Console.Error.WriteLine($"error: required delta not found at {deltaFile}");
            return 3;
        }

        // B1(b): PackageProduced facts from the scan become producer entries — the scan joins the
        // sidecar as a producer source. Sidecar entries are preserved verbatim; scan entries are
        // added only when the sidecar doesn't already claim that (repo, package). publicationStatus
        // is unknown from a declaration (no publish proof) — never invented.
        var producedProducers = new List<ProducerEntry>();
        foreach (var (f, manifest, dir) in allFacts)
        {
            if (f.FactType != "PackageProduced") continue;
            var key = RepoKey(f, manifest);
            var pkg = Prop(f, "packageName", "package", "name");
            if (string.IsNullOrEmpty(pkg)) { ingestGaps.Add($"skipped PackageProduced fact {f.FactId}: no packageName"); repoIngestGaps.Add((key, $"skipped PackageProduced fact: no packageName")); continue; }
            var ecosystem = Prop(f, "ecosystem", "packageManager");
            if (ecosystem is not null && ecosystem != "nuget")
            { ingestGaps.Add($"skipped PackageProduced fact {f.FactId}: ecosystem '{ecosystem}' not supported in V0 (nuget only)"); repoIngestGaps.Add((key, $"PackageProduced ecosystem '{ecosystem}' not supported")); continue; }
            var pv = Prop(f, "version");
            if (f.Properties is not null && f.Properties.TryGetValue("version", out var pvEl) && pvEl.ValueKind != JsonValueKind.String)
            { pv = null; ingestGaps.Add($"PackageProduced fact {f.FactId}: version unevidenced (hashed/unsafe) — {pkg}"); repoIngestGaps.Add((key, $"producer version unevidenced: {pkg}")); }
            var srcName = Prop(f, "projectPath") ?? f.Evidence?.FilePath ?? "";
            producedProducers.Add(new ProducerEntry { Repo = key, PackageId = pkg!, ProducedVersion = string.IsNullOrEmpty(pv) ? null : pv, EvidenceNote = $"tracemap PackageProduced ({srcName})" });
        }

        if (multiDirMissing.Count > 0 || !producerSatisfiedByScan)
        {
            var missing2 = new List<string>(multiDirMissing);
            if (!producerSatisfiedByScan && producedProducers.Count == 0) missing2.Insert(0, "--producer"); // B1(b): PackageProduced facts satisfy the producer side
            if (missing2.Count > 0)
            {
                var hint = missing2.Contains("--producer") ? "" : " (--producer satisfied by PackageProduced facts)";
                Console.Error.WriteLine($"error: {scanDirs.Length} scan dirs in this run ({string.Join(", ", scanDirs.Select(Path.GetFileName))}) — sidecars are run-wide: pass {string.Join(" ", missing2)} explicitly (auto-discovery is single-dir only){hint}");
                return 1;
            }
        }


        // ---- atomic output: temp dir as SIBLING of outDir (same volume, so Directory.Move works);
        //      safe swap = move old aside, move new in, delete old; rollback on failure ----
        var outParent = Path.GetDirectoryName(Path.GetFullPath(outDir)) ?? ".";
        var outName = Path.GetFileName(Path.GetFullPath(outDir));
        var tempDir = Path.Combine(outParent, "." + outName + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        var inputDir = Path.Combine(tempDir, "input");
        try
        {
        Directory.CreateDirectory(inputDir);

        // Ingest gaps reach the plan (B4): attach to the RIGHT repo's coverage, before writing
        // R3-2: store as (repoKey, message) at creation time; attach by exact key.
        // Estate-wide gaps (missing sidecar) attach to every repo deliberately.
        foreach (var cov in coverage)
        {
            cov.Gaps.AddRange(repoIngestGaps.Where(g => g.Key == cov.Repo).Select(g => g.Value));
            cov.Gaps.AddRange(estateIngestGaps);
            if (cov.Gaps.Count > 0) cov.Status = "gaps";
        }
        foreach (var g in ingestGaps) Console.Error.WriteLine($"warning: {g}");

        // Package evidence
        var pkgEvidence = new PackageEvidence
        {
            SchemaVersion = "package-evidence.v0", Source = "tracemap-ingest",
            Facts = evidence.Values.SelectMany(v => v).ToList(), ScanCoverage = coverage,
        };
        File.WriteAllText(Path.Combine(inputDir, "package-evidence.v0.json"), JsonSerializer.Serialize(pkgEvidence, WriteOpts));

        // SPEC-019 §5.3: assemble the freshness channel — scan-estate recorded per-scan buildFreshness
        // next to the facts (folder-name keyed); translate to REPO keys here (the one place that knows both).
        var freshnessEntries = new List<BuildFreshnessEntry>();
        foreach (var dir in scanDirs)
        {
            var sePath = Path.Combine(dir, "scan-estate.json");
            if (!File.Exists(sePath)) continue;
            try
            {
                using var seDoc = JsonDocument.Parse(File.ReadAllText(sePath));
                if (seDoc.RootElement.ValueKind == JsonValueKind.Object
                    && seDoc.RootElement.TryGetProperty("buildFreshness", out var bf) && bf.ValueKind == JsonValueKind.String)
                {
                    var dirName = Path.GetFileName(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar));
                    var value = bf.GetString()!;
                    foreach (var (key, (label, _m)) in repoKeys)
                        if (label == dirName || key == dirName)
                            freshnessEntries.Add(new BuildFreshnessEntry { Repo = key, Freshness = value });
                }
            }
            catch (JsonException) { /* broken metadata is the scan's problem (warned at scan time) */ }
        }
        if (freshnessEntries.Count > 0)
            File.WriteAllText(Path.Combine(inputDir, "build-freshness.v0.json"), JsonSerializer.Serialize(
                new BuildFreshnessFile { SchemaVersion = "build-freshness.v0", Repos = freshnessEntries }, WriteOpts));

        // Lockfile rows — SPEC-008: one v2 file for ALL repos with lockfile facts (repos sorted by name);
        // no lockfile file at all when there are none (unchanged for lockfile-less scans)
        if (lockfiles.Count > 0)
        {
            var v2 = new LockfileRowsV2
            {
                SchemaVersion = "lockfile-rows.v2",
                Repos = lockfiles.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new LockfileRepo { Repo = kv.Key, Rows = kv.Value }).ToList(),
            };
            File.WriteAllText(Path.Combine(inputDir, "lockfile-rows.v2.json"), JsonSerializer.Serialize(v2, WriteOpts));
        }

        // Sidecars (validated)
        var producerSource = producerPath ?? (scanDirs.Length == 1 ? FindSidecar(scanDirs, "producer-evidence.v0.json") : null); // multi-dir: never auto-discover (SPEC-011 §3) — scan-discovered producers + explicit flag are the only sources
        var pResult = CopyValidatedProducer(producerSource,
            Path.Combine(inputDir, "producer-evidence.v0.json"), "producer-evidence.v0", ingestGaps, producedProducers);
        if (pResult != 0) return pResult;
        var oResult = CopyValidated(ownershipPath ?? FindSidecar(scanDirs, "ownership.v0.json"),
            Path.Combine(inputDir, "ownership.v0.json"), "ownership.v0", ingestGaps);
        if (oResult != 0) return oResult;
        File.Copy(deltaFile, Path.Combine(inputDir, "delta.json"), true);
        if (scopePath is not null) File.Copy(scopePath, Path.Combine(inputDir, "scope.v0.json"), true); // SPEC-017: verbatim copy; the flag's bytes win



        }
        catch
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            throw;
        }

        // Safe swap: move old aside, move new in, delete old (rollback on failure)
        var backupDir = Path.Combine(outParent, "." + outName + ".old-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (Directory.Exists(outDir)) Directory.Move(outDir, backupDir);
            try { Directory.Move(tempDir, outDir); }
            catch { if (Directory.Exists(backupDir)) Directory.Move(backupDir, outDir); throw; }
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
        Console.Error.WriteLine($"ingested: {evidence.Values.Sum(v => v.Count)} consumer facts, {lockfiles.Values.Sum(v => v.Count)} lockfile rows, {repoKeys.Count} repos → {outDir}");
        return 0;
    }

    // SPEC-017: scan-estate's atomic-swap siblings (.name.tmp-xxxxxxxx / .name.old-xxxxxxxx — 8 hex).
    // Deliberately NARROW: the rule exists to hide swap leftovers, not to redefine which children are
    // scans — a dot-named child with facts.ndjson is still a scan per SPEC-011 (PR #2 Baz round 1).
    // Each marker is tested INDEPENDENTLY: a repo named foo.tmp-copy leaves .foo.tmp-copy.old-deadbeef,
    // and stopping at the embedded .tmp- would miss the real .old- suffix (PR #2 Codex P2).
    internal static bool IsSwapDir(string name)
    {
        if (!name.StartsWith('.')) return false;
        return MatchesSwapMarker(name, ".tmp-") || MatchesSwapMarker(name, ".old-");
        static bool MatchesSwapMarker(string name, string marker)
        {
            var idx = name.LastIndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return false;
            var suffix = name[(idx + marker.Length)..];
            return suffix.Length == 8 && suffix.All(Uri.IsHexDigit);
        }
    }

    // repoName from a scan dir's manifest, if readable — for alternate-snapshot detection during discovery.
    static string? TryReadRepoName(string scanDir)
    {
        try
        {
            var manifestPath = Path.Combine(scanDir, "scan-manifest.json");
            if (!File.Exists(manifestPath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            return doc.RootElement.TryGetProperty("repoName", out var rn) ? rn.GetString() : null;
        }
        catch { return null; }
    }

    // The directory's on-disk spelling (parent enumeration, case-insensitive probe) — canonical for dedup.
    static string CanonicalDirPath(string path)
    {
        var full = Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!Directory.Exists(full)) return full;
        var parent = Path.GetDirectoryName(full);
        var name = Path.GetFileName(full);
        if (parent is null || name.Length == 0) return full;
        var onDisk = Directory.GetDirectories(parent).FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
        return onDisk ?? full;
    }

    // SPEC-014: enumerate distinct repo keys across scan dirs exactly as ingest would see them.
    // Malformed scan data is a TYPED error (a file ingest cannot process must not quietly yield a
    // partial ownership file); a facts file with zero facts still yields the manifest's repo when
    // one exists (ingest parity), and missing files.ndjson is a typed error for explicit dirs.
    internal static (List<string> Keys, string? Error) EnumerateRepoKeysChecked(string[] scanDirs)
    {
        var keys = new SortedDictionary<string, string>(StringComparer.Ordinal); // key -> label (first seen)
        foreach (var dir in scanDirs)
        {
            var factsPath = Path.Combine(dir, "facts.ndjson");
            var manifestPath = Path.Combine(dir, "scan-manifest.json");
            TmManifest? manifest = null;
            if (File.Exists(manifestPath))
            {
                try { manifest = JsonSerializer.Deserialize<TmManifest>(File.ReadAllText(manifestPath), JsonOpts); }
                catch (JsonException ex) { return (new List<string>(), $"malformed scan-manifest.json in {dir}: {ex.Message}"); }
            }
            var sawFact = false;
            if (File.Exists(factsPath))
            {
                foreach (var line in File.ReadLines(factsPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    TmFact? f;
                    try { f = JsonSerializer.Deserialize<TmFact>(line, JsonOpts); }
                    catch (JsonException ex) { return (new List<string>(), $"malformed facts.ndjson in {dir}: {ex.Message}"); }
                    if (f is null) continue;
                    sawFact = true;
                    var key = RepoKey(f, manifest);
                    if (!keys.ContainsKey(key)) keys[key] = f.Repo ?? manifest?.RepoName ?? key;
                }
            }
            else if (!File.Exists(manifestPath))
            {
                return (new List<string>(), $"{dir} is not a scan (no facts.ndjson, no scan-manifest.json)");
            }
            else
            {
                return (new List<string>(), $"{dir} has scan-manifest.json but no facts.ndjson — ingest rejects it as an incomplete scan"); // explicit dirs: no silent manifest fallback (discovery already warns+skips)
            }
            if (!sawFact && manifest?.RepoName is { } rn) // zero-facts scan: ingest still registers the repo
            {
                var key = RepoKey(new TmFact { Repo = rn }, manifest);
                if (!keys.ContainsKey(key)) keys[key] = rn;
            }
        }
        return (keys.Keys.ToList(), null);
    }
    internal static List<string> EnumerateRepoKeys(string[] scanDirs) => EnumerateRepoKeysChecked(scanDirs).Keys; // legacy surface

    static string? FindSidecar(string[] dirs, string name)
    {
        foreach (var d in dirs) { var p = Path.Combine(d, name); if (File.Exists(p)) return p; }
        return null;
    }

    // SPEC-007 helpers: row identity = (lockfile, tfm, packageId); canonical order for deterministic output.
    static string Ord(string? s) => s ?? "";
    // Names is object-typed: a string on the ingest path, a JsonElement when deserialized from a file.
    // Unwrap only — never normalize: null vs "" are DISTINCT field values, and rows differing in ANY
    // field of one resolution group are a typed conflict (SPEC-007 §3).
    internal static string? NamesText(object? names) => names switch
    {
        null => null,
        string s => s,
        JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
        JsonElement { ValueKind: JsonValueKind.Null } => null,
        JsonElement je => je.GetRawText(), // non-string payload (never emitted by ingest); compare verbatim
        _ => names.ToString(),
    };
    internal static bool SameRow(LockRow a, LockRow b) => // also used by Program's load-time validation (SPEC-007 §7)
        a.PackageId == b.PackageId // packageId is a field too: case variants share a key (compared case-insensitively) but are never identical (SPEC-007 §3)
        && Ord(a.Version) == Ord(b.Version) && Ord(a.Type) == Ord(b.Type) && Ord(a.Via) == Ord(b.Via)
        && NamesText(a.Names) == NamesText(b.Names) // no Ord(): null and "" are distinct field values (SPEC-007 §3)
        && ProvText(a.Provenance) == ProvText(b.Provenance); // SPEC-019 §4: provenance joins row identity — rows from one manifest share it by construction
    static string ProvText(RowProvenance? p) => p is null ? "" : $"{p.ManifestSha256}\0{p.Freshness}\0{p.BuildCommitSha}";
    static string DescribeRow(LockRow r) => $"{r.Type} {r.PackageId} {r.Version ?? "no-version"}";
    internal static int RowOrder(LockRow a, LockRow b) =>
        string.CompareOrdinal(Ord(a.PackageId) + "\0" + Ord(a.Lockfile) + "\0" + Ord(a.Tfm) + "\0" + Ord(a.Version),
                               Ord(b.PackageId) + "\0" + Ord(b.Lockfile) + "\0" + Ord(b.Tfm) + "\0" + Ord(b.Version));

    // CopyValidated + scan-produced producer merge: sidecar wins on (repo, package); scan entries
    // append (ordinal by repo then package). Emitting is deterministic.
    static int CopyValidatedProducer(string? src, string dst, string schemaVersion, List<string> gaps, List<ProducerEntry> produced)
    {
        var rc = CopyValidated(src, dst, schemaVersion, gaps);
        if (rc != 0 || produced.Count == 0) return rc;
        ProducerEvidence existing;
        try { existing = JsonSerializer.Deserialize<ProducerEvidence>(File.ReadAllText(dst), JsonOpts) ?? new ProducerEvidence(); }
        catch (JsonException) { return rc; } // placeholder written by CopyValidated — parseable by construction
        existing.Producers ??= new List<ProducerEntry>();
        // NuGet ids are case-insensitive: repo key ordinal, package id ignore-case
        var byKey = existing.Producers
            .GroupBy(p => (NormBasic(p.Repo), p.PackageId.ToLowerInvariant()))
            .ToDictionary(g => g.Key, g => g.First());
        // case-variant repo spellings (same ignore-case form) merge with a warning — planner identity is exact, so the human resolves
        var added = 0;
        foreach (var p in produced.OrderBy(p => NormBasic(p.Repo), StringComparer.Ordinal).ThenBy(p => p.PackageId, StringComparer.Ordinal))
        {
            var k = (NormBasic(p.Repo), p.PackageId.ToLowerInvariant());
            if (byKey.TryGetValue(k, out var dup))
            {
                if (dup.ProducedVersion != p.ProducedVersion && p.ProducedVersion is not null)
                    Console.Error.WriteLine($"warning: producer conflict for {k.Item1}/{p.PackageId} — sidecar '{dup.ProducedVersion ?? "unevidenced"}' vs scan '{p.ProducedVersion}'; sidecar kept (scan evidence is declaration-only)");
                continue;
            }
            var caseVariantEntry = existing.Producers.FirstOrDefault(x2 => string.Equals(NormBasic(x2.Repo), k.Item1, StringComparison.OrdinalIgnoreCase) && string.Equals(x2.PackageId, p.PackageId, StringComparison.OrdinalIgnoreCase));
            if (caseVariantEntry is not null)
            { Console.Error.WriteLine($"warning: scan producer '{k.Item1}/{p.PackageId}' differs from existing '{NormBasic(caseVariantEntry.Repo)}/{caseVariantEntry.PackageId}' only by spelling — kept BOTH (planner identity is case-sensitive; merge deliberately via one spelling)"); }
            existing.Producers.Add(p);
            byKey[k] = p;
            added++;
        }
        if (existing.Source == "empty") existing.Source = "tracemap-ingest"; // a placeholder became real
        File.WriteAllText(dst, JsonSerializer.Serialize(existing, WriteOpts));
        if (added > 0) Console.Error.WriteLine($"note: {added} producer(s) from PackageProduced facts merged into producer-evidence.v0 (sidecar entries preserved; scan evidence is declaration-only — publicationStatus unknown)");
        return 0;
    }

    static int CopyValidated(string? src, string dst, string schemaVersion, List<string> gaps)
    {
        if (src is not null && File.Exists(src))
        {
            var content = File.ReadAllText(src);
            try
            {
                var doc = JsonDocument.Parse(content);
                if (!doc.RootElement.TryGetProperty("schemaVersion", out var sv) || sv.GetString() != schemaVersion)
                {
                    Console.Error.WriteLine($"error: sidecar {Path.GetFileName(src)}: schemaVersion mismatch — expected '{schemaVersion}', got '{(doc.RootElement.TryGetProperty("schemaVersion", out var sv2) ? sv2.GetString() : "missing")}'");
                    return 5;
                }
            }
            catch (JsonException)
            {
                Console.Error.WriteLine($"error: sidecar {Path.GetFileName(src)}: invalid JSON");
                return 5;
            }
            File.Copy(src, dst, true);
            return 0;
        }
        else
        {
            File.WriteAllText(dst, schemaVersion.Contains("producer")
                ? "{\"schemaVersion\":\"producer-evidence.v0\",\"source\":\"empty\",\"producers\":[]}"
                : "{\"schemaVersion\":\"ownership.v0\",\"selfTeamId\":\"__unset__\",\"ownerships\":[]}");
            gaps.Add($"no {schemaVersion} sidecar found; emitted empty placeholder (ownership will be unknown for all repos)");
        }
        return 0;
    }

    static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    static readonly JsonSerializerOptions WriteOpts = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}

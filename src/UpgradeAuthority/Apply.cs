using System.Text;

namespace UpgradeAuthority;

// SPEC-009: ua apply v1 — dry-run edit generation. Derives evidence-gated edit sites from a plan's
// facts (never invents), applies the upgrade-only policy, emits the canonical apply.v1 manifest and,
// with --repo, verifies each site against the checkout before emitting per-wave unified diffs.
// Read-only: no git, no branches, no PRs, no network (SPEC-010).
public static class Apply
{
    public const int RefusalExit = 6;

    // ---- manifest model (apply.v1) ----
    public sealed class ApplyManifest { public string SchemaVersion = "apply.v1"; public ApplyDelta Delta = new(); public List<ApplyWave> Waves = new(); }
    public sealed class ApplyDelta { public string PackageName = "", Ecosystem = "", ChangeType = "", OldVersion = "", NewVersion = ""; }
    public sealed class ApplyWave { public int Index; public string Status = ""; public List<string> Prerequisites = new(); public string? Condition; public string? BlockedOn; public List<ApplyUnit> Units = new(); }
    public sealed class ApplyUnit { public string Repo = ""; public string Action = ""; public List<ApplyEdit>? Edits; public string? Note; }
    public sealed class ApplyEdit { public string Kind = "", Path = "", Attribute = "", OldVersion = "", NewVersion = ""; public int Line; public int? EndLine; public string EvidenceKind = ""; public bool Shared; public string PackageId = ""; } // PackageId: the SITE's evidenced spelling (SPEC-021 §2(d)) — emitted only when it differs from delta.packageName

    public static int Run(string fixtureDir, string? repoDir, string? outDir)
    {
        var engine = Program.LoadEngine(fixtureDir);
        var plan = engine.BuildPlan();
        var manifest = BuildManifest(engine, plan);

        if (outDir is null)
        {
            Console.Write(WriteManifest(manifest));
            return 0;
        }

        if (repoDir is not null)
        {
            // --repo maps ONE repo checkout (SPEC-009 §5): a manifest with edits in more than one repo
            // cannot be verified against a single --repo — refuse rather than mix checkouts (PR14 Q6).
            var editedRepos = manifest.Waves.SelectMany(w => w.Units).Where(u => u.Edits is { Count: > 0 }).Select(u => u.Repo).Distinct().ToList();
            if (editedRepos.Count > 1)
            {
                Console.Error.WriteLine($"error: manifest carries edits for {editedRepos.Count} repos ({string.Join(", ", editedRepos)}) — --repo maps ONE checkout; run apply per-repo (v1: per-repo fixtures are the estate path)");
                return 1;
            }
        }

        // Output is atomic (house rule): EVERYTHING lands in a sibling temp dir and swaps in only on
        // success, so a refusal never leaves partial patches or a half-written dir behind (PR14 Q5).
        var outParent = Path.GetDirectoryName(Path.GetFullPath(outDir)) ?? ".";
        var outName = Path.GetFileName(Path.GetFullPath(outDir));
        var realOutDir = outDir;
        outDir = Path.Combine(outParent, "." + outName + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "apply.v1.json"), WriteManifest(manifest));

            if (repoDir is not null)
            {
                var rcPatches = WriteVerifiedPatches(manifest, repoDir, outDir);
                if (rcPatches != 0) return rcPatches;
            }

            // safe swap: move old aside, move new in, delete old; rollback on failure
            var backupDir = Path.Combine(outParent, "." + outName + ".old-" + Guid.NewGuid().ToString("N")[..8]);
            if (Directory.Exists(realOutDir)) Directory.Move(realOutDir, backupDir);
            try { Directory.Move(outDir, realOutDir); }
            catch { if (Directory.Exists(backupDir)) Directory.Move(backupDir, realOutDir); throw; }
            if (Directory.Exists(backupDir)) Directory.Delete(backupDir, true);
        }
        finally { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); }

        Console.Error.WriteLine($"apply: {manifest.Waves.Sum(w => w.Units.Sum(u => u.Edits?.Count ?? 0))} verified edit(s) across {manifest.Waves.Count} wave(s) -> {realOutDir}");
        return 0;
    }

    // ---- edit derivation (SPEC-009 §3) ----

    internal static ApplyManifest BuildManifest(Engine engine, Plan plan) // also used by Push (SPEC-010)
    {
        var target = plan.Delta.PackageName;
        var m = new ApplyManifest
        {
            Delta = new ApplyDelta { PackageName = target, Ecosystem = plan.Delta.Ecosystem, ChangeType = plan.Delta.ChangeType, OldVersion = plan.Delta.OldVersion, NewVersion = plan.Delta.NewVersion },
        };
        var planRepo = plan.Repos.ToDictionary(r => r.Repo, StringComparer.Ordinal);
        foreach (var w in plan.Waves)
        {
            var aw = new ApplyWave { Index = w.Index, Status = w.Status, Prerequisites = w.Prerequisites.ToList(), Condition = w.Condition, BlockedOn = w.BlockedOn };
            foreach (var u in w.ReleaseUnits)
            {
                var repo = u.Repo;
                var pr = planRepo.GetValueOrDefault(repo);
                if (pr?.Ownership == "external")
                {
                    aw.Units.Add(new ApplyUnit { Repo = repo, Action = "external-request-await" }); // ownership outranks producer semantics (PR14 Q4)
                    continue;
                }
                if (engine.ProducesTarget(repo))
                {
                    aw.Units.Add(new ApplyUnit { Repo = repo, Action = "republish-required", Note = N2() });
                    continue;
                }

                var edits = new List<ApplyEdit>();
                string? highestSatisfied = null;
                var unresolved = 0; // PR6 Codex r4: pin-bearing sites that could not be evaluated — N4 must not hide them
                foreach (var f in engine.ApplyFacts(repo).Where(f => Engine.PkgEq(f.PackageId, target))) // SPEC-021 §2(c)
                {
                    var (kind, attr, shared) = f.ConstraintSource switch
                    {
                        "Directory.Packages.props" => ("E2", "PackageVersion/Version", true),
                        "VersionOverride" => ("E3", "VersionOverride", false),
                        _ => f.Format == "packages.config" ? ("E4", "version", false) : ("E1", "Version", false),
                    };
                    var old = f.DeclaredConstraint;
                    if (old.Length == 0) continue; // no local pin at this site (CPM reference inheriting the central version — expected shape, not unresolved)
                    if (old.StartsWith("redacted:")) { unresolved++; continue; } // a real pin ua cannot read — N3 territory, never hidden by N4
                    if (f.Line is null) { unresolved++; continue; } // unlocated: no evidenced line, no edit site — never invent a location (PR14 Q8/C3)
                    var cmp = CompareCore(old, plan.Delta.NewVersion);
                    if (cmp is null) { unresolved++; continue; } // prerelease/floating — a range site could still admit the new version; N3 names it
                    if (cmp >= 0) { if (highestSatisfied is null || CompareCore(old, highestSatisfied) > 0) highestSatisfied = old; continue; } // already-satisfied: never a downgrade
                    edits.Add(new ApplyEdit { Kind = kind, Path = f.Path, Attribute = attr, OldVersion = old, NewVersion = plan.Delta.NewVersion, Line = f.Line ?? 0, EndLine = f.EndLine, EvidenceKind = f.ConstraintSource == "Directory.Packages.props" ? "central-package-version.v0" : "package-evidence.v0", Shared = shared, PackageId = f.PackageId }); // SPEC-021 §2(d): the file's own spelling
                }
                edits = edits.OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.Line).ToList();
                // dedup identical (path, line, attribute, old) sites from overlapping facts
                edits = edits.GroupBy(e => (e.Path, e.Line, e.Attribute, e.OldVersion, e.PackageId)).Select(g => g.First()).ToList(); // SPEC-021 R2-3: the evidenced spelling joins the key — a variant element sharing the line is never silently dropped

                if (edits.Count > 0) aw.Units.Add(new ApplyUnit { Repo = repo, Action = "edit", Edits = edits });
                else if (highestSatisfied is not null && unresolved == 0) aw.Units.Add(new ApplyUnit { Repo = repo, Action = "no-edit-site", Note = N4(repo, target, highestSatisfied, plan.Delta.NewVersion) }); // satisfied AND every pin-bearing site evaluated — otherwise the constraint note is the honest one
                else if (engine.ApplyFacts(repo).Any(f => Engine.PkgEq(f.PackageId, target))) aw.Units.Add(new ApplyUnit { Repo = repo, Action = "no-edit-site", Note = N3(repo, target, ConstraintKind(engine, repo, target)) });
                else aw.Units.Add(new ApplyUnit { Repo = repo, Action = "no-edit-site", Note = N1(repo, target) });
            }
            m.Waves.Add(aw);
        }
        return m;
    }

    static string ConstraintKind(Engine engine, string repo, string target)
    {
        var all = engine.ApplyFacts(repo).Where(f => Engine.PkgEq(f.PackageId, target)).ToList();
        var constraints = all.Select(f => f.DeclaredConstraint).ToList();
        if (constraints.All(c => c.Length == 0)) return "not-evidenced";
        if (constraints.Any(c => c.StartsWith("redacted:"))) return "redacted";
        // PR6 Codex r5: an EXACT pin without line evidence is an evidence gap, not a constraint problem —
        // diagnosing it as ranged-or-prerelease would point the operator at rewriting a valid constraint
        if (all.Any(f => f.DeclaredConstraint.Length > 0 && f.Line is null && IsExactPin(f.DeclaredConstraint)))
            return "an exact pin with no evidenced location";
        if (constraints.Any(c => c.Length == 0)) return "not-evidenced"; // mixed: at least one unconstrained site
        return "ranged-or-prerelease";
    }

    // Numeric core compare: null when either side is not a plain numeric pin (prerelease etc.).
    internal static int? CompareCore(string a, string b) // shared with Planner findings (SPEC-012)
    {
        var pa = ParseCore(a); var pb = ParseCore(b);
        if (pa is null || pb is null) return null;
        for (int i = 0; i < 4; i++)
        {
            var x = i < pa.Length ? pa[i] : 0; var y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
    // SPEC-018 §4: exact-pin test extracted for sharing with drift. Same semantics as the edit-site
    // gate above (ranges/floating/prerelease/redacted fail it) with two deliberate divergences
    // stated by the spec: no line-evidence requirement and no comparison target.
    internal static bool IsExactPin(string constraint) =>
        constraint.Length > 0 && !constraint.StartsWith("redacted:") && ParseCore(constraint) is not null;
    static int[]? ParseCore(string v)
    {
        var parts = v.Split('.');
        if (parts.Length is < 1 or > 4) return null;
        var nums = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i], out nums[i]) || nums[i] < 0 || (nums[i] == 0 && parts[i].Length > 1)) return null;
        return nums;
    }

    // ---- note templates (SPEC-009 §6 — exhaustive) ----
    static string N1(string repo, string pkg) => $"no evidenced direct edit site for {pkg} in {repo} — exposure is transitive or the constraint is not an exact pin; bump the direct chain or pin explicitly (human decision)";
    static string N2() => $"unit must republish before downstream edits take effect; its own version-bump site is not indexed in V0";
    static string N3(string repo, string pkg, string kind) => $"constraint for {pkg} in {repo} is {kind}; v1 rewrites exact pins only";
    static string N4(string repo, string pkg, string version, string newVersion) => $"{repo} pins {pkg} at {version}, already at or above {newVersion} — no edit, never a downgrade";

    // SPEC-010: the verification + per-wave patch core, shared by `ua apply --repo` and `ua push`
    // (push runs it against a temporary WORKTREE of the base, so verification always targets the
    // tree being committed). Writes wave-N.patch files into patchDir; refuses (exit 6) on any site.
    internal static int WriteVerifiedPatches(ApplyManifest manifest, string repoDir, string patchDir)
    {
        foreach (var w in manifest.Waves)
        {
            var anyUnit = w.Units.Count > 0;
            var edited = new List<(string Path, string Resolved, List<(int Line, string Old, string New, int Col, string Path2)> Changes)>();
            foreach (var u in w.Units)
            {
                foreach (var e in u.Edits ?? new List<ApplyEdit>())
                {
                    var (ok, err, change) = VerifySite(repoDir, e);
                    if (!ok)
                    {
                        Console.Error.WriteLine($"error: {err}");
                        return RefusalExit;
                    }
                    if (change is null) continue;
                    var file = edited.FirstOrDefault(x => x.Path == e.Path);
                    if (file.Path is null) edited.Add((e.Path, change!.Value.Resolved, new List<(int, string, string, int, string)> { change!.Value }));
                    else file.Changes.Add(change!.Value);
                }
            }
            if (edited.Count == 0)
            {
                if (anyUnit) File.WriteAllText(Path.Combine(patchDir, $"wave-{w.Index}.patch"), ""); // honest empty patch (acceptance 2a)
                continue;
            }
            var sb = new StringBuilder();
            foreach (var (path, resolvedPath, changes) in edited.OrderBy(x => x.Path, StringComparer.Ordinal))
            {
                var full = resolvedPath; // the containment-cleared target the sites were verified against — never a fresh resolve (PR6 Codex r9)
                var text = File.ReadAllText(full);
                var crlf = text.Contains("\r\n");
                var hadFinalNewline = text.EndsWith("\n");
                var raw = text.Split('\n');
                var before = raw.Select(l => l.TrimEnd('\r')).ToList();
                if (before.Count > 0 && before[^1] == "" && hadFinalNewline) before.RemoveAt(before.Count - 1); // trailing split artifact
                var after = before.ToList();
                // PR6 Baz r1: replace EXACTLY the verified occurrence (line + column), bottom-up and
                // right-to-left within a line — a same-line neighbor carrying the identical version
                // attribute is never rewritten by a site that did not verify it.
                foreach (var g in changes.GroupBy(c => c.Line).OrderByDescending(g => g.Key))
                    foreach (var (line, old, @new, col, _) in g.OrderByDescending(c => c.Col))
                        after[line - 1] = after[line - 1].Remove(col, old.Length).Insert(col, @new);
                var nl = crlf ? "\r\n" : "\n";
                sb.Append(UnifiedDiff(path, before.ToArray(), after.ToArray(), nl, hadFinalNewline));
            }
            File.WriteAllText(Path.Combine(patchDir, $"wave-{w.Index}.patch"), sb.ToString());
        }

        return 0;
    }

    // ---- site verification (SPEC-009 §5): exactly one full-selector match in the window ----

    // Resolve EVERY path component through its symlinks (chained links included), top-down: an
    // ancestor symlinked directory escapes FileInfo.LinkTarget checks on the final file (PR6 Codex r8).
    static string ResolveSymlinks(string absolute)
    {
        var root = Path.GetPathRoot(absolute) ?? string.Empty;
        var cur = root.Length > 0 ? root : absolute[..absolute.IndexOf(Path.DirectorySeparatorChar)];
        foreach (var seg in absolute[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Path.Combine(cur, seg);
            string? target = null;
            if (File.Exists(next)) target = new FileInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            else if (Directory.Exists(next)) target = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
            cur = target ?? next;
        }
        return cur;
    }

    static (bool Ok, string Error, (int Line, string Old, string New, int Col, string Resolved)? Change) VerifySite(string repoDir, ApplyEdit e)
    {
        // PR6 Baz r7/Codex r8: the edit's Path is untrusted evidence — it must resolve INSIDE the
        // canonical checkout with EVERY symlink component (and chained links) resolved (an ancestor
        // symlinked directory escapes FileInfo.LinkTarget checks), and carry no control characters
        // (a newline forges patch headers; a tab corrupts unified-diff filename delimiters).
        if (e.Path.Any(char.IsControl)) // C0, DEL, and the Unicode C1 range alike (PR6 Codex r9)
            return (false, "stale evidence: edit path contains control characters — refused", null);
        var checkoutRoot = ResolveSymlinks(Path.GetFullPath(repoDir));
        var full = Path.GetFullPath(Path.Combine(Path.GetFullPath(repoDir), e.Path));
        var resolved = ResolveSymlinks(full);
        if (!resolved.StartsWith(checkoutRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) && resolved != checkoutRoot)
            return (false, $"stale evidence: {e.Path} resolves outside the checkout — refused", null);
        if (!File.Exists(resolved)) return (false, $"stale evidence: {e.Path} does not exist in the checkout", null);
        var lines = File.ReadAllLines(resolved); // the containment-cleared resolved path
        // window: ±2 around the COMPLETE evidence span (PR14 Q1/C2) — a multiline element's value
        // can sit several lines below its opening tag.
        var start = Math.Max(1, e.Line - 2);
        var end = Math.Min(lines.Length, (e.EndLine ?? e.Line) + 2);
        var patterns = AttrPatterns(e.Attribute, e.OldVersion, e.NewVersion);
        var matches = new List<(int Line, string Old, string New, int Col, string Resolved)>();
        for (var i = start; i <= end; i++)
        {
            var line = lines[i - 1];
            // PR14 Q3/C-P1: the line must NAME THE TARGET PACKAGE. R2-1 tightens further: the version
            // match must live in the SAME XML ELEMENT as the package id — minified files put several
            // elements on one line, and a neighbor's attribute must never satisfy this site.
            foreach (var (spanStart, spanEnd) in ElementSpansNaming(line, e)) // SPEC-021 §2(d): the site's OWN evidenced spelling; the file matcher itself stays Ordinal
            {
                var span = line[spanStart..(spanEnd + 1)];
                foreach (var (oldText, newText) in patterns)
                    for (var idx = span.IndexOf(oldText, StringComparison.Ordinal); idx >= 0; idx = span.IndexOf(oldText, idx + 1, StringComparison.Ordinal))
                        matches.Add((i, oldText, newText, spanStart + idx, resolved)); // ABSOLUTE column of the verified occurrence (PR6 Baz r1) + the RESOLVED path that passed containment (PR6 Codex r9: patch generation reads THIS target, never a re-resolve that a retargeted symlink could divert)
            }
        }
        if (matches.Count == 0)
            return (false, $"stale evidence: {e.Path} line ~{e.Line}: no occurrence of {e.Attribute}=\"{e.OldVersion}\" naming {e.PackageId} within the evidence window (expected {e.OldVersion})", null);
        if (matches.Count > 1)
            return (false, $"ambiguous site: {e.Path} line ~{e.Line}: {matches.Count} identical occurrences of {e.PackageId} {e.Attribute}=\"{e.OldVersion}\" — refusing to guess", null);
        return (true, "", matches[0]);
    }

    // Fragments of the line, one per OCCURRENCE of an element naming the target package: from the '<'
    // before the naming attribute to the next '>' after it. Ranges — never deduped by content: two
    // identical duplicate elements are two occurrences (ambiguity), not one.
    static List<(int Start, int End)> ElementSpansNaming(string line, ApplyEdit e)
    {
        var spans = new List<(int Start, int End)>();
        var packageId = e.PackageId; // SPEC-021 §2(d): the SITE's evidenced spelling — matched Ordinal (never folded)
        var namers = e.Kind == "E4"
            ? new[] { $"id=\"{packageId}\"", $"id='{packageId}'" }
            : new[] { $"Include=\"{packageId}\"", $"Include='{packageId}'", $"Update=\"{packageId}\"", $"Update='{packageId}'" };
        foreach (var namer in namers)
        {
            for (var idx = line.IndexOf(namer, StringComparison.Ordinal); idx >= 0; idx = line.IndexOf(namer, idx + 1, StringComparison.Ordinal))
            {
                var open = line.LastIndexOf('<', idx);
                if (open < 0) open = 0;
                var close = line.IndexOf('>', idx);
                if (close < 0) close = line.Length - 1;
                if (!spans.Contains((open, close))) spans.Add((open, close));
            }
        }
        return spans;
    }

    static List<(string Old, string New)> AttrPatterns(string attribute, string oldVersion, string newVersion)
    {
        // E2's PackageVersion/Version resolves to the item's Version attribute
        var attr = attribute == "PackageVersion/Version" ? "Version" : attribute;
        return new List<(string, string)>
        {
            ($"{attr}=\"{oldVersion}\"", $"{attr}=\"{newVersion}\""),
            ($"{attr}='{oldVersion}'", $"{attr}='{newVersion}'"),
            ($"<{attr}>{oldVersion}</{attr}>", $"<{attr}>{newVersion}</{attr}>"),
        };
    }

    // ---- unified diff (minimal, deterministic: 3 context lines, one hunk run per changed region) ----

    static string UnifiedDiff(string path, string[] before, string[] after, string nl, bool finalNewline)
    {
        // Line-level diff (apply edits are in-place line rewrites): equal lines are context,
        // changed positions emit -old then +new. Hunks carry 3 context lines, deterministic order.
        var changed = new List<int>();
        for (int i = 0; i < Math.Max(before.Length, after.Length); i++)
        {
            var b = i < before.Length ? before[i] : null;
            var a = i < after.Length ? after[i] : null;
            if (b != a) changed.Add(i);
        }
        if (changed.Count == 0) return "";
        var sb = new StringBuilder();
        sb.Append("--- a/").Append(path).Append('\n');
        sb.Append("+++ b/").Append(path).Append('\n');
        int k = 0;
        while (k < changed.Count)
        {
            // group changes whose contexts would overlap or touch
            int groupStart = changed[k];
            int groupEnd = changed[k];
            while (k + 1 < changed.Count && changed[k + 1] <= groupEnd + 6) { k++; groupEnd = changed[k]; }
            int ctxStart = Math.Max(0, groupStart - 3);
            int ctxEnd = Math.Min(Math.Max(before.Length, after.Length) - 1, groupEnd + 3);
            int bLen = Math.Min(before.Length, ctxEnd + 1) - Math.Min(before.Length, ctxStart);
            int aLen = Math.Min(after.Length, ctxEnd + 1) - Math.Min(after.Length, ctxStart);
            if (bLen <= 0 && aLen <= 0) { k++; continue; }
            sb.Append("@@ -").Append(Math.Min(before.Length, ctxStart) + 1).Append(',').Append(bLen)
              .Append(" +").Append(Math.Min(after.Length, ctxStart) + 1).Append(',').Append(aLen).Append(" @@").Append(nl);
            bool Last(int i) => i == Math.Max(before.Length, after.Length) - 1 && !finalNewline;
            for (int i = ctxStart; i <= ctxEnd; i++)
            {
                var b = i < before.Length ? before[i] : null;
                var a = i < after.Length ? after[i] : null;
                if (b == a) { sb.Append(' ').Append(b); if (!Last(i)) sb.Append(nl); else sb.Append(nl).Append("\\ No newline at end of file").Append(nl); }
                else
                {
                    if (b is not null) { sb.Append('-').Append(b); if (!Last(i)) sb.Append(nl); else sb.Append(nl).Append("\\ No newline at end of file").Append(nl); }
                    if (a is not null) { sb.Append('+').Append(a); if (!Last(i)) sb.Append(nl); else sb.Append(nl).Append("\\ No newline at end of file").Append(nl); }
                }
            }
            k++;
        }
        return sb.ToString();
    }

    // ---- canonical manifest writer (SPEC-009 §6: 2-space, LF, trailing newline) ----

    public static string WriteManifest(ApplyManifest m)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        Str(sb, 1, "schemaVersion"); sb.Append(": "); Str(sb, m.SchemaVersion); sb.Append(",\n");
        Str(sb, 1, "delta"); sb.Append(": {\n");
        Str(sb, 2, "packageName"); sb.Append(": "); Str(sb, m.Delta.PackageName); sb.Append(",\n");
        Str(sb, 2, "ecosystem"); sb.Append(": "); Str(sb, m.Delta.Ecosystem); sb.Append(",\n");
        Str(sb, 2, "changeType"); sb.Append(": "); Str(sb, m.Delta.ChangeType); sb.Append(",\n");
        Str(sb, 2, "oldVersion"); sb.Append(": "); Str(sb, m.Delta.OldVersion); sb.Append(",\n");
        Str(sb, 2, "newVersion"); sb.Append(": "); Str(sb, m.Delta.NewVersion); sb.Append("\n");
        sb.Append("  },\n");
        Str(sb, 1, "waves"); sb.Append(": [\n");
        for (int w = 0; w < m.Waves.Count; w++)
        {
            var wave = m.Waves[w];
            sb.Append("    {\n");
            Str(sb, 3, "index"); sb.Append(": ").Append(wave.Index).Append(",\n");
            Str(sb, 3, "status"); sb.Append(": "); Str(sb, wave.Status); sb.Append(",\n");
            if (wave.Condition is not null) { Str(sb, 3, "condition"); sb.Append(": "); Str(sb, wave.Condition); sb.Append(",\n"); }
            if (wave.BlockedOn is not null) { Str(sb, 3, "blockedOn"); sb.Append(": "); Str(sb, wave.BlockedOn); sb.Append(",\n"); }
            Str(sb, 3, "prerequisites"); sb.Append(": ");
            AppendStrArray(sb, wave.Prerequisites, 3);
            sb.Append(",\n");
            Str(sb, 3, "units"); sb.Append(": [\n");
            for (int u = 0; u < wave.Units.Count; u++)
            {
                var unit = wave.Units[u];
                sb.Append("        {\n");
                Str(sb, 5, "repo"); sb.Append(": "); Str(sb, unit.Repo); sb.Append(",\n");
                Str(sb, 5, "action"); sb.Append(": "); Str(sb, unit.Action); sb.Append(",\n");
                if (unit.Note is not null) { Str(sb, 5, "note"); sb.Append(": "); Str(sb, unit.Note); sb.Append(",\n"); }
                if (unit.Edits is { Count: > 0 })
                {
                    Str(sb, 5, "edits"); sb.Append(": [\n");
                    for (int e = 0; e < unit.Edits.Count; e++)
                    {
                        var ed = unit.Edits[e];
                        sb.Append("            {\n"); // edit object at pad 12; fields at 14
                        Str(sb, 7, "kind"); sb.Append(": "); Str(sb, ed.Kind); sb.Append(",\n");
                        Str(sb, 7, "path"); sb.Append(": "); Str(sb, ed.Path); sb.Append(",\n");
                        Str(sb, 7, "attribute"); sb.Append(": "); Str(sb, ed.Attribute); sb.Append(",\n");
                        Str(sb, 7, "oldVersion"); sb.Append(": "); Str(sb, ed.OldVersion); sb.Append(",\n");
                        Str(sb, 7, "newVersion"); sb.Append(": "); Str(sb, ed.NewVersion); sb.Append(",\n");
                        Str(sb, 7, "line"); sb.Append(": ").Append(ed.Line); sb.Append(",\n");
                        Str(sb, 7, "evidenceKind"); sb.Append(": "); Str(sb, ed.EvidenceKind);
                        // SPEC-021 §2 (R1-2): the site's evidenced spelling rides the manifest ONLY when it
                        // differs from delta.packageName — zero churn on the variant-free corpus, replayable where it matters
                        if (!string.Equals(ed.PackageId, m.Delta.PackageName, StringComparison.Ordinal)) { sb.Append(",\n"); Str(sb, 7, "packageId"); sb.Append(": "); Str(sb, ed.PackageId); }
                        if (ed.Shared) { sb.Append(",\n"); Str(sb, 7, "shared"); sb.Append(": true"); }
                        sb.Append("\n            }").Append(e < unit.Edits.Count - 1 ? "," : "").Append("\n");
                    }
                    sb.Append("          ]\n");
                }
                else sb.Append("        \"edits\": []\n");
                sb.Append("        }").Append(u < wave.Units.Count - 1 ? "," : "").Append("\n");
            }
            sb.Append("    ]\n");
            sb.Append("    }").Append(w < m.Waves.Count - 1 ? "," : "").Append("\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }

    static void AppendStrArray(StringBuilder sb, List<string> items, int ind)
    {
        if (items.Count == 0) { sb.Append("[]"); return; }
        sb.Append("[\n");
        for (int i = 0; i < items.Count; i++) { Pad(sb, ind + 1); Str(sb, items[i]); sb.Append(i < items.Count - 1 ? "," : "").Append('\n'); }
        Pad(sb, ind); sb.Append(']');
    }
    static void Pad(StringBuilder sb, int ind) { for (int i = 0; i < ind * 2; i++) sb.Append(' '); }
    static void Str(StringBuilder sb, int ind, string key) { Pad(sb, ind); Str(sb, key); }
    static void Str(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                default: if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4")); else sb.Append(ch); break;
            }
        }
        sb.Append('"');
    }
}

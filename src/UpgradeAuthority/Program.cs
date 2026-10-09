using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// ua — V0 impact-plan CLI (SPEC-000..003). Local-first, offline, zero packages.
//   ua plan <fixture-dir>          emit canonical plan.json to stdout
//   ua selftest [fixtures-root]    run the golden corpus (byte-exact), the
//                                  input-permutation case, and the multi-change
//                                  rejection case. Exit 1 on any failure.
public static class Program
{
    static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public static int Main(string[] args)
    {
        Redact.RedactingWriter? outWriter = null;
        Redact.RedactingWriter? errWriter = null;
        try
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);

            // SPEC-016: --sanitized (global, usable anywhere) — redacts console streams for shareable
            // diagnostics; canonical artifact writes (plan/report stdout) bypass the redactor.
            if (Array.IndexOf(args, "--sanitized") >= 0)
            {
                args = args.Where(a => a != "--sanitized").ToArray();
                Redact.Enable(Environment.GetEnvironmentVariable("UA_REDACT"));
                var originalOut = Console.Out;
                var originalErr = Console.Error;
                outWriter = new Redact.RedactingWriter(originalOut, () => false);
                errWriter = new Redact.RedactingWriter(originalErr, () => false);
                Console.SetOut(outWriter);
                Console.SetError(errWriter);
                Console.Error.WriteLine("note: sanitized output enabled — paths→[path:…], non-allowlisted URLs/hosts→[url#/host#…]; file artifacts are NOT transformed");
            }

            if (args.Length >= 2 && args[0] == "plan")
            {
                // SPEC-017 §6: --out writes canonical bytes directly (PS 5.1 redirection mangles UTF-8)
                int? badFlag = ValueFlagError(args, "--out", "-o");
                if (badFlag is not null) { Console.Error.WriteLine($"error: {args[badFlag.Value]} requires a value"); return 1; }
                var outFile = GetOpt(args, "--out") ?? GetOpt(args, "-o");
                var text = Canonical.Write(LoadEngine(args[1]).BuildPlan());
                if (outFile is null) WriteCanonical(() => Console.Write(text), outWriter);
                else WriteArtifactFile(outFile, text);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "report")
            {
                int? badFlagR = ValueFlagError(args, "--out", "-o");
                if (badFlagR is not null) { Console.Error.WriteLine($"error: {args[badFlagR.Value]} requires a value"); return 1; }
                var outFileR = GetOpt(args, "--out") ?? GetOpt(args, "-o");
                var planR = LoadEngine(args[1]).BuildPlan();
                var textR = Report.Render(planR);
                if (outFileR is null) WriteCanonical(() => Console.Write(textR), outWriter);
                else WriteArtifactFile(outFileR, textR);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "apply")
            {
                // SPEC-016: apply's stdout (without --out) IS the canonical manifest — same bypass as plan/report
                if (GetOpt(args, "--out") is null && GetOpt(args, "-o") is null && outWriter is not null)
                    outWriter.CanonicalArtifact = true;
                var repoOpt = GetOpt(args, "--repo");
                var outOpt = GetOpt(args, "--out") ?? GetOpt(args, "-o");
                if (repoOpt is not null && outOpt is null) { outWriter?.GetType(); outWriter = outWriter; Console.Error.WriteLine("error: --out <dir> is required with --repo (patches are files)"); return 1; }
                var rcApply = Apply.Run(args[1], repoOpt, outOpt);
                if (outWriter is not null) outWriter.CanonicalArtifact = false;
                return rcApply;
            }
            if (args.Length >= 2 && args[0] == "ownership")
            {
                var sub = args[1];
                var rest = args.Skip(2).ToArray();
                var dirs = rest.TakeWhile(a => !a.StartsWith("--")).ToArray();
                bool Has(string f) => Array.IndexOf(rest, f) >= 0; // position-independent boolean flags
                string? Val(string f) // value flags: next token must exist and not look like an option (any leading hyphen)
                {
                    var i = Array.IndexOf(rest, f);
                    if (i < 0) return null;
                    if (i + 1 >= rest.Length || rest[i + 1].StartsWith('-')) { Console.Error.WriteLine($"error: {f} requires a value"); Environment.Exit(1); }
                    return rest[i + 1];
                }
                var scansRoot = Val("--scans-root");
                var scopeOpt = Val("--scope");
                var outFile = Val("--out") ?? Val("-o");
                if (outFile is null) { Console.Error.WriteLine("error: --out <file> is required"); return 1; }
                if (sub == "init")
                {
                    if (Has("--new-self") || Has("--new-unassigned") || Has("--new-team")) { Console.Error.WriteLine("error: init takes --all-self | --team <t> | --unassigned (not update's --new-* flags)"); return 1; }
                    var self = Val("--self");
                    if (self is null) { Console.Error.WriteLine("error: --self <teamId> is required for init"); return 1; }
                    var modes = new[] { Has("--all-self"), Has("--unassigned"), Val("--team") is not null }.Count(b => b);
                    if (modes != 1) { Console.Error.WriteLine(modes == 0 ? "error: exactly one assignment mode required: --all-self | --team <t> | --unassigned" : "error: contradictory assignment modes — pass exactly one of --all-self | --team <t> | --unassigned"); return 1; }
                    var mode = Has("--all-self") ? "all-self" : Has("--unassigned") ? "unassigned" : "team";
                    return Ownership.Init(dirs, scansRoot, self, mode, Val("--team"), outFile, scopeOpt);
                }
                if (sub == "update")
                {
                    if (Has("--all-self") || Has("--unassigned") || Has("--team") || Has("--self")) { Console.Error.WriteLine("error: update takes --new-self | --new-team <t> | --new-unassigned (not init's flags; selfTeamId comes from --existing and is preserved)"); return 1; }
                    var existing = Val("--existing");
                    if (existing is null) { Console.Error.WriteLine("error: --existing <file> is required for update"); return 1; }
                    var nModes = new[] { Has("--new-self"), Has("--new-unassigned"), Val("--new-team") is not null }.Count(b => b);
                    if (nModes != 1) { Console.Error.WriteLine(nModes == 0 ? "error: exactly one new-repo mode required: --new-self | --new-team <t> | --new-unassigned" : "error: contradictory new-repo modes — pass exactly one"); return 1; }
                    var nmode = Has("--new-self") ? "new-self" : Has("--new-unassigned") ? "new-unassigned" : "new-team";
                    return Ownership.Update(dirs, scansRoot, existing, nmode, Val("--new-team"), "", outFile, scopeOpt); // selfTeamId preserved from --existing
                }
                Console.Error.WriteLine("usage: ua ownership init|update …");
                return 2;
            }
            if (args.Length >= 2 && args[0] == "push")
            {
                var repoOpt = GetOpt(args, "--repo");
                var baseOpt = GetOpt(args, "--base");
                var outOpt = GetOpt(args, "--out") ?? GetOpt(args, "-o");
                if (outOpt is null) { Console.Error.WriteLine("error: --out <dir> is required (the push.v1 report is a file artifact)"); return 1; }
                var dry = Array.IndexOf(args, "--dry-run") >= 0;
                var pr = Array.IndexOf(args, "--pr") >= 0;
                var apiOpt = GetOpt(args, "--github-api");
                if (apiOpt is null && Array.IndexOf(args, "--github-api") >= 0)
                { Console.Error.WriteLine("error: --github-api requires a value (<base-url>)"); return 1; }
                return Push.Run(args[1], repoOpt, baseOpt, outOpt, dry, pr, apiOpt);
            }
            if (args.Length >= 3 && args[0] == "ingest")
            {
                var outDir = GetOpt(args, "--out") ?? GetOpt(args, "-o");
                if (outDir is null) { Console.Error.WriteLine("error: --out <dir> is required"); return 1; }
                var scanDirs = args.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToArray();
                var scansRootOpt = GetOpt(args, "--scans-root");
                if (scansRootOpt is null && Array.IndexOf(args, "--scans-root") >= 0)
                { Console.Error.WriteLine("error: --scans-root requires a value (<dir>)"); return 1; }
                var scopeOpt = GetOpt(args, "--scope");
                if (scopeOpt is null && Array.IndexOf(args, "--scope") >= 0)
                { Console.Error.WriteLine("error: --scope requires a value (<file>)"); return 1; }
                return Ingest.Run(scanDirs, outDir, GetOpt(args, "--producer"), GetOpt(args, "--ownership"), GetOpt(args, "--delta"), scansRootOpt, scopeOpt);
            }
            if (args.Length >= 2 && args[0] == "drift")
            {
                // SPEC-018: outdated discovery — installed inventory vs feed truth → drift.v1 (+ delta candidates)
                foreach (var f in new[] { "--feed", "--folder-feed", "--out", "-o", "--emit-deltas" })
                {
                    int? bad = ValueFlagError(args, f);
                    if (bad is not null) { Console.Error.WriteLine($"error: {args[bad.Value]} requires a value"); return 1; }
                }
                var outFileD = GetOpt(args, "--out") ?? GetOpt(args, "-o");
                var (_, canonical, rcD) = Drift.Run(args[1], GetOpt(args, "--feed"), GetOpt(args, "--folder-feed"), GetOpt(args, "--emit-deltas"));
                if (rcD != 0) return rcD;
                if (outFileD is null) WriteCanonical(() => Console.Write(canonical), outWriter);
                else WriteArtifactFile(outFileD, canonical);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "registry")
            {
                // SPEC-020: estate producer registry — the us-vs-the-internet boundary (registry.v1)
                int? badFlagR2 = ValueFlagError(args, "--out", "-o");
                if (badFlagR2 is not null) { Console.Error.WriteLine($"error: {args[badFlagR2.Value]} requires a value"); return 1; }
                var outFileR2 = GetOpt(args, "--out") ?? GetOpt(args, "-o");
                var (canonicalR2, rcR2) = Registry.Run(args[1]);
                if (rcR2 != 0) return rcR2;
                if (outFileR2 is null) WriteCanonical(() => Console.Write(canonicalR2), outWriter);
                else WriteArtifactFile(outFileR2, canonicalR2);
                return 0;
            }
            if (args.Length >= 1 && args[0] == "scan-estate")
            {
                // SPEC-017: one command — repos folder → freshness → cached scans → sidecars → plan → report
                foreach (var f in new[] { "--repos-root", "--out", "-o", "--tracemap", "--scope", "--self", "--delta-package", "--delta-old", "--delta-new", "--worktree-root", "--trunk", "--parallel" })
                {
                    int? bad = ValueFlagError(args, f);
                    if (bad is not null) { Console.Error.WriteLine($"error: {args[bad.Value]} requires a value"); return 1; }
                }
                var reposRoot = GetOpt(args, "--repos-root");
                var outDirE = GetOpt(args, "--out") ?? GetOpt(args, "-o");
                var tracemap = GetOpt(args, "--tracemap");
                var scopeOpt = GetOpt(args, "--scope");
                var selfOpt = GetOpt(args, "--self");
                var dP = GetOpt(args, "--delta-package");
                var dO = GetOpt(args, "--delta-old");
                var dN = GetOpt(args, "--delta-new");
                var allowStale = Array.IndexOf(args, "--allow-stale") >= 0;
                var updateCheckouts = Array.IndexOf(args, "--update-checkouts") >= 0;
                var worktreeMode = Array.IndexOf(args, "--worktree") >= 0;
                var buildFlag = Array.IndexOf(args, "--build") >= 0;
                var indexDepsJson = Array.IndexOf(args, "--index-deps-json") >= 0;
                var parallelOpt = GetOpt(args, "--parallel");
                int parallel = 2;
                if (parallelOpt is not null && (!int.TryParse(parallelOpt, out parallel) || parallel < 1 || parallel > 64))
                { Console.Error.WriteLine("error: --parallel requires a value in 1..64"); return 1; }
                var excludes = new List<string>();
                for (int i = 0; i < args.Length; i++)
                    if (args[i] == "--exclude") // repeatable tracemap pass-through (folder globs, NOT scope exclusion)
                    {
                        if (i + 1 >= args.Length || args[i + 1].StartsWith('-')) { Console.Error.WriteLine("error: --exclude requires a value (<glob> — a tracemap scan pass-through)"); return 1; }
                        excludes.Add(args[i + 1]); i++;
                    }
                if (reposRoot is null) { Console.Error.WriteLine("error: --repos-root <dir> is required"); return 1; }
                if (outDirE is null) { Console.Error.WriteLine("error: --out <dir> is required (the estate working dir: scans/, sidecars, fixture/, plan.json, report.md)"); return 1; }
                return ScanEstate.Run(reposRoot, outDirE, tracemap, scopeOpt, selfOpt, allowStale, excludes.ToArray(), dP, dO, dN, null, updateCheckouts, worktreeMode, GetOpt(args, "--worktree-root"), GetOpt(args, "--trunk"), parallel, buildFlag, indexDepsJson);
            }
            if (args.Length >= 1 && args[0] == "selftest") return SelfTest(args.Length > 1 ? args[1] : FindRepoRoot("fixtures"));
                Console.Error.WriteLine("usage: ua plan|report <fixture-dir> [--out <file>] | ua registry <fixture-dir> [--out <file>] | ua scan-estate --repos-root <dir> --out <dir> [--tracemap <dll>] [--self <teamId>] [--scope <file>] [--allow-stale] [--exclude <glob>]... [--delta-package <id> --delta-old <v> --delta-new <v>] | ua ingest <tracemap-dir>... --out <fixture-dir> [--scans-root <dir>] [--scope <file>] | ua ownership init|update ... [--scope <file>] | ua apply <fixture-dir> [--repo <path>] [--out <dir>] | ua push <fixture-dir> --base <branch> [--repo <path>] [--out <dir>] [--pr] [--dry-run] | ua selftest [fixtures-root]");
            return 2;
        }
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return 3; }
        catch (Exception ex) { Console.Error.WriteLine($"internal error: {ex.Message}"); return 4; }
        finally
        {
            if (outWriter is not null && errWriter is not null)
            {
                Console.Out.Flush(); Console.Error.Flush();
                Console.SetOut(outWriter.Original);
                Console.SetError(errWriter.Original);
            }
        }
    }

    static void WriteCanonical(Action write, Redact.RedactingWriter? outWriter)
    {
        if (outWriter is not null) outWriter.CanonicalArtifact = true;
        try { write(); }
        finally { if (outWriter is not null) outWriter.CanonicalArtifact = false; }
    }


    static void ForceDelete(string dir)
    {
        if (!Directory.Exists(dir)) return;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                // git marks object files (and some dirs) read-only on Windows — clear bottom-up, then delete
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { /* racing cleanup */ }
                foreach (var d in Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(x => x.Length))
                    try { File.SetAttributes(d, FileAttributes.Directory); } catch { /* racing cleanup */ }
                Directory.Delete(dir, true);
                return;
            }
            catch (UnauthorizedAccessException) when (attempt == 0) { System.Threading.Thread.Sleep(300); }
            catch { break; /* fall through to the leftover report below */ }
        }
        if (Directory.Exists(dir)) Console.Error.WriteLine($"warning: selftest scratch dir left behind (OS will scrub): {dir}"); // never silent
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    // tracked files under root when git is available (the EOL guard checks the checkout, not artifacts);
    // otherwise walk the tree skipping build-output directories
    static List<string> SelfTestTreeFiles(string root)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            psi.ArgumentList.Add("-C"); psi.ArgumentList.Add(root); psi.ArgumentList.Add("ls-files");
            using var p = System.Diagnostics.Process.Start(psi)!;
            var listed = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);
            if (p.ExitCode == 0)
            {
                var tracked = listed.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0)
                    .Select(l => Path.Combine(root, l)).Where(File.Exists).ToList();
                if (tracked.Count > 0) return tracked;
            }
        }
        catch { /* no git here — fall through to the walk */ }
        var skip = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}.vs{Path.DirectorySeparatorChar}" };
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => { var full = Path.GetFullPath(f); return !skip.Any(s => full.Contains(s)); }).ToList();
    }

    static string? GetOpt(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
        return null;
    }

    // SPEC-017 §2: a value flag without a value is a typed error — never a silent fallback
    // (a trailing bare --out must not print the artifact to stdout instead of the file).
    // Returns the index of the first offending flag, or null when every occurrence has a value.
    static int? ValueFlagError(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length; i++)
            if (names.Contains(args[i]) && (i + 1 >= args.Length || args[i + 1].StartsWith('-')))
                return i;
        return null;
    }

    // SPEC-017 §6: canonical artifact bytes straight to a file — exactly the string stdout
    // carries (UTF-8 no BOM, LF), which is what makes --out files byte-identical to redirected
    // stdout on every shell, PowerShell 5.1 included.
    internal static void WriteArtifactFile(string path, string text)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        Console.Error.WriteLine($"wrote {path}");
    }

    static string FindRepoRoot(string marker)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, marker))) dir = dir.Parent!;
        if (dir is null) throw new UaException($"could not locate '{marker}' above {AppContext.BaseDirectory}");
        return Path.Combine(dir.FullName, marker);
    }

    internal static Engine LoadEngine(string fixtureDir)
    {
        T Load<T>(string name) where T : class
        {
            var path = Path.Combine(fixtureDir, "input", name);
            if (!File.Exists(path)) throw new UaException($"missing input: {path}");
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new UaException($"unparseable input: {path}"); }
            catch (JsonException ex) { throw new UaException($"malformed input {name}: {ex.Message}"); }
        }
        var pe = Load<ProducerEvidence>("producer-evidence.v0.json");
        var ow = Load<OwnershipFile>("ownership.v0.json");
        var px = Load<PackageEvidence>("package-evidence.v0.json");
        var delta = Load<DeltaFile>("delta.json");
        // SPEC-008: exactly one lockfile input file, any of v0/v1/v2
        var lockFiles = new[] { "lockfile-rows.v2.json", "lockfile-rows.v1.json", "lockfile-rows.v0.json" }
            .Select(n => (name: n, path: Path.Combine(fixtureDir, "input", n))).Where(t => File.Exists(t.path)).ToList();
        if (lockFiles.Count > 1)
            throw new UaException($"malformed input: multiple lockfile inputs present ({string.Join(", ", lockFiles.Select(t => t.name))}) — exactly one is allowed (which one wins must never be implicit)");
        var lockRepos = new List<LockfileRows>();
        if (lockFiles.Count == 1)
        {
            var lf = lockFiles[0];
            string text;
            try { text = File.ReadAllText(lf.path); }
            catch (Exception ex) { throw new UaException($"malformed input {lf.name}: {ex.Message}"); }
            if (lf.name == "lockfile-rows.v2.json")
            {
                LockfileRowsV2? v2;
                try { v2 = JsonSerializer.Deserialize<LockfileRowsV2>(text, Json); }
                catch (JsonException ex) { throw new UaException($"malformed input {lf.name}: {ex.Message}"); }
                if (v2?.SchemaVersion != "lockfile-rows.v2")
                    throw new UaException($"malformed input {lf.name}: unsupported schemaVersion '{v2?.SchemaVersion}' (expected lockfile-rows.v2)");
                if (v2.Repos is null)
                    throw new UaException($"malformed input {lf.name}: repos is null — the repos[] collection is required");
                if (v2.Repos.Count == 0)
                    throw new UaException($"malformed input {lf.name}: empty repos[] — a v2 file with no lockfile rows must not exist at all");
                var seenRepos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in v2.Repos)
                {
                    if (r is null)
                        throw new UaException($"malformed input {lf.name}: null repos[] element — each entry must be an object");
                    if (r.Rows is null)
                        throw new UaException($"malformed input {lf.name}: repos[] entry '{r.Repo}' has rows: null — use an empty array or omit the entry");
                    if (string.IsNullOrEmpty(r.Repo))
                        throw new UaException($"malformed input {lf.name}: repos[] entry with empty repo — identity is required");
                    if (!seenRepos.Add(r.Repo))
                        throw new UaException($"malformed input {lf.name}: duplicate repos[] entry '{r.Repo}' — one entry per repo (same-repo scan dirs merge at ingest)");
                    var one = new LockfileRows { SchemaVersion = "lockfile-rows.v2", Repo = r.Repo, Rows = r.Rows };
                    ValidateRows(lf.name, one, v1: true);
                    lockRepos.Add(one);
                }
            }
            else
            {
                LockfileRows? parsed;
                try { parsed = JsonSerializer.Deserialize<LockfileRows>(text, Json); }
                catch (JsonException ex) { throw new UaException($"malformed input {lf.name}: {ex.Message}"); }
                lockRepos.Add(ValidateLockfileRows(lf.path, parsed));
            }
        }
        // SPEC-017 §3: optional estate scope — one shared validator (same message as ingest --scope)
        EstateScope? scopeFile = null;
        var scopeInputPath = Path.Combine(fixtureDir, "input", "scope.v0.json");
        if (File.Exists(scopeInputPath))
        {
            var (parsedScope, scopeErr) = Scope.ParseAndValidate(File.ReadAllText(scopeInputPath), "scope.v0.json");
            if (scopeErr is not null) throw new UaException(scopeErr);
            scopeFile = parsedScope;
        }
        // SPEC-019 §5.3: optional per-repo build freshness (the staleness channel). ABSENT = conservative —
        // a deps.json closure alone can never prove not-affected without it.
        BuildFreshnessFile? freshnessFile = null;
        var freshnessInputPath = Path.Combine(fixtureDir, "input", "build-freshness.v0.json");
        if (File.Exists(freshnessInputPath))
        {
            BuildFreshnessFile? parsed;
            try { parsed = JsonSerializer.Deserialize<BuildFreshnessFile>(File.ReadAllText(freshnessInputPath), Json); }
            catch (JsonException ex) { throw new UaException($"malformed input build-freshness.v0.json: {ex.Message}"); }
            if (parsed?.SchemaVersion != "build-freshness.v0")
                throw new UaException($"malformed input build-freshness.v0.json: unsupported schemaVersion '{parsed?.SchemaVersion}' (expected build-freshness.v0)");
            if (parsed.Repos is null)
                throw new UaException("malformed input build-freshness.v0.json: repos is null — a file with no entries must not exist at all");
            var seenFreshness = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var fe in parsed.Repos)
            {
                if (fe is null)
                    throw new UaException("malformed input build-freshness.v0.json: null repos[] element — every entry must be an object");
                if (string.IsNullOrEmpty(fe.Repo))
                    throw new UaException("malformed input build-freshness.v0.json: empty or null repo — every entry needs one"); // C44 r14: null reaches TryGetValue BEFORE the old check and crashes
                if (seenFreshness.TryGetValue(fe.Repo, out var prior))
                {
                    if (prior != fe.Freshness)
                        throw new UaException($"malformed input build-freshness.v0.json: repo '{fe.Repo}' appears twice with conflicting freshness ({prior} and {fe.Freshness}) — the file contradicts itself");
                    continue; // identical duplicate collapses
                }
                seenFreshness[fe.Repo] = fe.Freshness;
                if (fe.Freshness is not ("fresh" or "stale" or "none" or "fresh-by-build"))
                    throw new UaException($"malformed input build-freshness.v0.json: repo '{fe.Repo}' freshness '{fe.Freshness}' not in fresh|stale|none|fresh-by-build");
            }
            freshnessFile = parsed;
        }
        return new Engine(pe, ow, px, delta, lockRepos, scopeFile, freshnessFile);
    }

    // SPEC-007 §7: load-time typed errors — one row per resolution group; v1 rows carry their key fields.
    static LockfileRows ValidateLockfileRows(string path, LockfileRows? lf)
    {
        if (lf is null) throw new UaException($"unparseable input: {path}");
        if (lf.SchemaVersion != "lockfile-rows.v0" && lf.SchemaVersion != "lockfile-rows.v1")
            throw new UaException($"malformed input {Path.GetFileName(path)}: unsupported schemaVersion '{lf.SchemaVersion}' (expected lockfile-rows.v0 or lockfile-rows.v1)");
        bool v1 = lf.SchemaVersion == "lockfile-rows.v1";
        ValidateRows(Path.GetFileName(path), lf, v1);
        return lf;
    }

    // Row rules shared by v1 files and v2 repos[] entries (SPEC-008 §3: row shape and rules are exactly v1).
    static void ValidateRows(string name, LockfileRows lf, bool v1)
    {
        lf.Rows ??= new List<LockRow>();
        var seen = new Dictionary<string, LockRow>();
        foreach (var r in lf.Rows.ToList())
        {
            if (r is null)
                throw new UaException($"malformed input {name}: null rows[] element — each row must be an object");
            if (string.IsNullOrEmpty(r.PackageId))
                throw new UaException($"malformed input {name}: row with empty packageId — a resolution group needs its package identity");
            if (v1 && (r.Lockfile is null || r.Tfm is null))
                throw new UaException($"malformed input {name}: row for '{r.PackageId}' is missing its '{(r.Lockfile is null ? "lockfile" : "tfm")}' key — row identity is (repo, lockfile, tfm, packageId)");
            if (v1 && r.Lockfile == "")
                throw new UaException($"malformed input {name}: row for '{r.PackageId}' has an empty lockfile path — a resolution group needs its lockfile identity (empty tfm is legal, empty lockfile is not)");
            if (r.Provenance is not null && string.IsNullOrEmpty(r.Provenance.ManifestSha256))
                throw new UaException($"malformed input {name}: row for '{r.PackageId}' carries an incomplete provenance (manifestSha256 null/empty) — build-output evidence is all-or-nothing; remove the provenance or supply it fully"); // Baz r14: fail-closed at load, never mid-plan
            var key = v1 ? $"{r.PackageId.ToLowerInvariant()}\0{r.Lockfile}\0{r.Tfm}" : r.PackageId.ToLowerInvariant(); // NuGet ids are case-insensitive (Kiro post-merge hardening): case variants are the same key, and SameRow then rejects them (fields differ)
            if (seen.TryGetValue(key, out var prev))
            {
                if (Ingest.SameRow(prev, r)) { lf.Rows.Remove(r); continue; } // exact duplicate collapses (idempotent)
                throw new UaException($"malformed input {name}: conflicting rows for one resolution group ('{r.PackageId}'{(v1 ? $" in {r.Lockfile} ({r.Tfm})" : "")}) — a single (lockfile, TFM, package) resolution cannot disagree with itself");
            }
            seen[key] = r;
        }
    }

    static int SelfTest(string fixturesRoot)
    {
        int pass = 0, fail = 0;
        foreach (var dir in Directory.GetDirectories(fixturesRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            var goldenPath = Path.Combine(dir, "golden", "plan.json");
            if (!File.Exists(goldenPath)) continue;
            var name = Path.GetFileName(dir);
            try
            {
                var actual = Canonical.Write(LoadEngine(dir).BuildPlan());
                var golden = File.ReadAllText(goldenPath);
                var reportPath = Path.Combine(dir, "golden", "report.md");
                if (!File.Exists(reportPath)) { fail++; Console.WriteLine($"FAIL {name}: missing golden report.md"); continue; }
                var plan = LoadEngine(dir).BuildPlan();
                var reportActual = Report.Render(plan);
                var reportGolden = File.ReadAllText(reportPath);
                var planOk = actual == golden;
                var reportOk = new System.Text.UTF8Encoding(false).GetBytes(reportActual).SequenceEqual(File.ReadAllBytes(reportPath));
                if (planOk && reportOk) { Console.WriteLine($"ok   {name}"); pass++; }
                else
                {
                    fail++;
                    if (!planOk) { Console.WriteLine($"FAIL {name} (plan)"); DumpDiff(golden, actual); }
                    if (!reportOk) { Console.WriteLine($"FAIL {name} (report.md)"); DumpDiff(reportGolden, reportActual); }
                }
            }
            catch (Exception ex) { fail++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
        }

        // permutation case (SPEC-003 §4.2): reversed input declaration order -> identical bytes, for EVERY fixture
        foreach (var dir2 in Directory.GetDirectories(fixturesRoot).OrderBy(d => d, StringComparer.Ordinal))
        {
            if (!File.Exists(Path.Combine(dir2, "golden", "plan.json"))) continue;
            var name2 = "permutation:" + Path.GetFileName(dir2);
            try
            {
                var baseline = Canonical.Write(LoadEngine(dir2).BuildPlan());
                if (baseline.Contains('\r')) { fail++; Console.WriteLine($"FAIL {name2}: output contains CR"); continue; }
                var scratch = Path.Combine(Path.GetTempPath(), "ua-perm-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(Path.Combine(scratch, "input"));
                foreach (var f in Directory.GetFiles(Path.Combine(dir2, "input"))) File.Copy(f, Path.Combine(scratch, "input", Path.GetFileName(f)));
                foreach (var f in Directory.GetFiles(Path.Combine(scratch, "input")))
                {
                    var doc = JsonDocument.Parse(File.ReadAllText(f));
                    File.WriteAllText(f, CanonicalJson(doc.RootElement));
                }
                var permuted = Canonical.Write(LoadEngine(scratch).BuildPlan());
                Directory.Delete(scratch, true);
                if (permuted == baseline) { Console.WriteLine($"ok   {name2}"); pass++; }
                else { fail++; Console.WriteLine($"FAIL {name2}"); DumpDiff(baseline, permuted); }
            }
            catch (Exception ex) { fail++; Console.WriteLine($"FAIL {name2}: {ex.Message}"); }
        }

        // multi-change rejection case (SPEC-003 §1a)
        try
        {
            var any = Directory.GetDirectories(fixturesRoot).First(d => File.Exists(Path.Combine(d, "golden", "plan.json")));
            var scratch = Path.Combine(Path.GetTempPath(), "ua-multi-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(scratch, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(any, "input"))) File.Copy(f, Path.Combine(scratch, "input", Path.GetFileName(f)));
            var dpath = Path.Combine(scratch, "input", "delta.json");
            var delta = JsonSerializer.Deserialize<DeltaFile>(File.ReadAllText(dpath), Json)!;
            delta.Changes.Add(delta.Changes[0]);
            File.WriteAllText(dpath, JsonSerializer.Serialize(delta, Json));
            try { LoadEngine(scratch).BuildPlan(); fail++; Console.WriteLine("FAIL multi-change-rejection (no exception)"); }
            catch (UaException ex) when (ex.Message.Contains("exactly one change"))
            { Console.WriteLine("ok   multi-change-rejection"); pass++; }
            Directory.Delete(scratch, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL multi-change-rejection: {ex.Message}"); }

        // lockfile duplicate/conflict rejections (SPEC-007 §7)
        try
        {
            var withLock = Directory.GetDirectories(fixturesRoot).First(d => File.Exists(Path.Combine(d, "input", "lockfile-rows.v0.json")));
            var s1 = Path.Combine(Path.GetTempPath(), "ua-v0dup-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(s1, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(withLock, "input"))) File.Copy(f, Path.Combine(s1, "input", Path.GetFileName(f)));
            var l0 = JsonSerializer.Deserialize<LockfileRows>(File.ReadAllText(Path.Combine(s1, "input", "lockfile-rows.v0.json")), Json)!;
            l0.Rows.Add(new LockRow { PackageId = l0.Rows[0].PackageId, Type = l0.Rows[0].Type, Version = "9.9.9" });
            File.WriteAllText(Path.Combine(s1, "input", "lockfile-rows.v0.json"), JsonSerializer.Serialize(l0, Json));
            try { LoadEngine(s1).BuildPlan(); fail++; Console.WriteLine("FAIL v0-duplicate-rejection (no exception)"); }
            catch (UaException ex) when (ex.Message.Contains("conflicting rows"))
            { Console.WriteLine("ok   v0-duplicate-rejection"); pass++; }
            Directory.Delete(s1, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL v0-duplicate-rejection: {ex.Message}"); }
        try
        {
            var withLock2 = Directory.GetDirectories(fixturesRoot).First(d => File.Exists(Path.Combine(d, "input", "lockfile-rows.v0.json")));
            var s2 = Path.Combine(Path.GetTempPath(), "ua-v1conf-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(s2, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(withLock2, "input"))) File.Copy(f, Path.Combine(s2, "input", Path.GetFileName(f)));
            var l1 = JsonSerializer.Deserialize<LockfileRows>(File.ReadAllText(Path.Combine(s2, "input", "lockfile-rows.v0.json")), Json)!;
            l1.SchemaVersion = "lockfile-rows.v1";
            foreach (var r in l1.Rows) { r.Lockfile = "src/App/packages.lock.json"; r.Tfm = "net8.0"; }
            l1.Rows.Add(new LockRow { PackageId = l1.Rows[0].PackageId, Type = l1.Rows[0].Type, Version = "9.9.9", Lockfile = "src/App/packages.lock.json", Tfm = "net8.0" });
            File.WriteAllText(Path.Combine(s2, "input", "lockfile-rows.v1.json"), JsonSerializer.Serialize(l1, Json));
            File.Delete(Path.Combine(s2, "input", "lockfile-rows.v0.json"));
            try { LoadEngine(s2).BuildPlan(); fail++; Console.WriteLine("FAIL v1-conflict-rejection (no exception)"); }
            catch (UaException ex) when (ex.Message.Contains("conflicting rows"))
            { Console.WriteLine("ok   v1-conflict-rejection"); pass++; }
            Directory.Delete(s2, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL v1-conflict-rejection: {ex.Message}"); }
        // case-variant package ids in one resolution group (Kiro post-merge verification): NuGet ids are
        // case-insensitive — same key, differing fields -> typed conflict, never two rows or a silent collapse.
        // Exercised on BOTH paths: Ingest.Run (the detection this hardening guards) and the loader.
        try
        {
            string FactJson(string id) => JsonSerializer.Serialize(new
            {
                factId = "fact-" + id, scanId = "s", repo = "R-cv", commitSha = "c",
                factType = "PackageReferenced", ruleId = "project.file.v1", evidenceTier = "Tier2Structural",
                evidence = new { filePath = "src/A/packages.lock.json", startLine = 1, endLine = 1, snippetHash = (string?)null, extractorId = "NuGetLockfileExtractor", extractorVersion = "nuget-lockfile/0.1.0" },
                properties = new Dictionary<string, object?>
                {
                    ["dependencyGroup"] = "lockfile", ["dependencyRelation"] = "direct", ["ecosystem"] = "nuget",
                    ["lockfilePath"] = "src/A/packages.lock.json", ["manifestKind"] = "packages.lock.json",
                    ["package"] = id, ["packageManager"] = "nuget", ["packageName"] = id,
                    ["resolvedVersion"] = "3.1.1", ["sourceKind"] = "lockfile", ["surfaceKind"] = "package-config",
                    ["targetFramework"] = "net8.0", ["version"] = "3.1.1",
                },
            }, Json);
            var scanDir = Path.Combine(Path.GetTempPath(), "ua-casevar-scan-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(scanDir);
            File.WriteAllText(Path.Combine(scanDir, "facts.ndjson"), FactJson("Serilog") + "\n" + FactJson("serilog") + "\n");
            var ingestOut = Path.Combine(Path.GetTempPath(), "ua-casevar-out-" + Guid.NewGuid().ToString("N")[..8]);
            var rc = Ingest.Run(new[] { scanDir }, ingestOut, null, null, null);
            if (rc == 4) { Console.WriteLine("ok   case-variant-rejection (ingest)"); pass++; }
            else { fail++; Console.WriteLine($"FAIL case-variant-rejection (ingest): exit {rc}, expected 4"); }
            if (Directory.Exists(ingestOut)) Directory.Delete(ingestOut, true);
            Directory.Delete(scanDir, true);

            var withLock3 = Directory.GetDirectories(fixturesRoot).First(d => File.Exists(Path.Combine(d, "input", "lockfile-rows.v0.json")));
            var s3 = Path.Combine(Path.GetTempPath(), "ua-casevar-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(s3, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(withLock3, "input"))) File.Copy(f, Path.Combine(s3, "input", Path.GetFileName(f)));
            var l2 = new LockfileRows { SchemaVersion = "lockfile-rows.v1", Repo = "R-cv", Rows = new() };
            l2.Rows.Add(new LockRow { PackageId = "Serilog", Type = "direct", Version = "3.1.1", Lockfile = "src/A/packages.lock.json", Tfm = "net8.0" });
            l2.Rows.Add(new LockRow { PackageId = "serilog", Type = "direct", Version = "3.1.1", Lockfile = "src/A/packages.lock.json", Tfm = "net8.0" });
            File.WriteAllText(Path.Combine(s3, "input", "lockfile-rows.v1.json"), JsonSerializer.Serialize(l2, Json));
            File.Delete(Path.Combine(s3, "input", "lockfile-rows.v0.json"));
            try { LoadEngine(s3).BuildPlan(); fail++; Console.WriteLine("FAIL case-variant-rejection (load) (no exception)"); }
            catch (UaException ex) when (ex.Message.Contains("conflicting rows"))
            { Console.WriteLine("ok   case-variant-rejection (load)"); pass++; }
            Directory.Delete(s3, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL case-variant-rejection (load): {ex.Message}"); }

        // end-to-end: real tracemap scan -> ingest -> plan -> report (SPEC-007 §9.7; Kiro's committed svc scan)
        var e2eTmp = Path.Combine(Path.GetTempPath(), "ua-svc-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var rich = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var scan = Path.Combine(rich, "scans", "svc");
            var sidecars = Path.Combine(rich, "sidecars");
            if (!Directory.Exists(scan)) throw new Exception($"committed scan missing: {scan}");
            var tmp = e2eTmp;
            var rc = Ingest.Run(new[] { scan }, tmp,
                Path.Combine(sidecars, "producer-evidence.v0.json"), Path.Combine(sidecars, "ownership.v0.json"), Path.Combine(sidecars, "delta.json"));
            if (rc != 0) throw new Exception($"ingest exited {rc}");
            var plan = LoadEngine(tmp).BuildPlan();
            var svcRepo = plan.Repos.FirstOrDefault(r => r.Repo == "svc") ?? throw new Exception("svc missing from plan");
            if (svcRepo.Classification != "affected") throw new Exception($"svc classification {svcRepo.Classification}");
            if (!svcRepo.EvidenceKinds.Contains("lockfile-rows.v2")) throw new Exception("svc evidence kinds missing lockfile-rows.v2"); // SPEC-008: ingest emits v2 (acceptance 7)
            var subjects = (plan.Uncertainty.Findings ?? new()).Select(f => f.Subject).ToList();
            if (!subjects.Contains("svc version disagreement: Contoso.Core")) throw new Exception("missing Contoso.Core finding");
            if (!subjects.Contains("svc version disagreement: Newtonsoft.Json")) throw new Exception("missing Newtonsoft.Json finding");
            if (subjects.Contains("svc version disagreement: Serilog")) throw new Exception("unexpected Serilog finding");
            var reportText = Report.Render(plan);
            if (!reportText.Contains("### Findings")) throw new Exception("report missing Findings section");
            if (!reportText.Contains("- Version disagreements: 2 — see Uncertainty")) throw new Exception("report missing summary bullet");
            Console.WriteLine("ok   ingest-svc-e2e (real scan, findings + provenance + report)"); pass++;
            Directory.Delete(tmp, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL ingest-svc-e2e: {ex.Message}"); }
        finally { if (Directory.Exists(e2eTmp)) Directory.Delete(e2eTmp, true); }

        // SPEC-008 §7.4: two-repo estate end-to-end on committed real scans — machine-checked forever
        var estateTmp = Path.Combine(Path.GetTempPath(), "ua-estate2-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var rich2 = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var side2 = Path.Combine(rich2, "sidecars-estate2");
            var rc2 = Ingest.Run(new[] { Path.Combine(rich2, "scans", "billing"), Path.Combine(rich2, "scans", "shipping") }, estateTmp,
                Path.Combine(side2, "producer-evidence.v0.json"), Path.Combine(side2, "ownership.v0.json"), Path.Combine(side2, "delta.json"));
            if (rc2 != 0) throw new Exception($"ingest exited {rc2} (exit 2 must be retired)");
            var plan2 = LoadEngine(estateTmp).BuildPlan();
            var affected2 = plan2.Repos.Where(r => r.Classification == "affected").Select(r => r.Repo).ToList();
            if (!affected2.Contains("billing") || !affected2.Contains("shipping")) throw new Exception($"affected repos: {string.Join(",", affected2)}");
            if (plan2.Repos.Any(r => r.Classification == "affected" && !r.EvidenceKinds.Contains("lockfile-rows.v2"))) throw new Exception("missing lockfile-rows.v2 provenance");
            if (plan2.Waves.Count != 1 || plan2.Waves[0].ReleaseUnits.Count != 2) throw new Exception($"waves: {plan2.Waves.Count}, units: {plan2.Waves.FirstOrDefault()?.ReleaseUnits.Count}");
            if (plan2.Uncertainty.Findings is { Count: > 0 }) throw new Exception("cross-repo version difference must not be a finding");
            var report2 = Report.Render(plan2);
            if (report2.Contains("### Findings")) throw new Exception("no Findings section expected");
            Console.WriteLine("ok   ingest-estate2-e2e (two-repo real scans, v2 provenance, one wave two units, zero findings)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL ingest-estate2-e2e: {ex.Message}"); }
        finally { if (Directory.Exists(estateTmp)) Directory.Delete(estateTmp, true); }

        // SPEC-008 §7.4a: mixed lockfile/no-lockfile run — lockfile-less repo contributes no repos[] entry
        try
        {
            var rich3 = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var side3 = Path.Combine(rich3, "sidecars-estate2");
            var mixedTmp = Path.Combine(Path.GetTempPath(), "ua-mixed-" + Guid.NewGuid().ToString("N")[..8]);
            var rc3 = Ingest.Run(new[] { Path.Combine(rich3, "scans", "billing"), Path.Combine(rich3, "scans", "shipping"), Path.Combine(rich3, "scans", "cleanrepo") }, mixedTmp,
                Path.Combine(side3, "producer-evidence.v0.json"), Path.Combine(side3, "ownership.v0.json"), Path.Combine(side3, "delta.json"));
            if (rc3 != 0) throw new Exception($"ingest exited {rc3}");
            var v2path = Path.Combine(mixedTmp, "input", "lockfile-rows.v2.json");
            if (!File.Exists(v2path)) throw new Exception("lockfile-rows.v2.json missing");
            var v2doc = JsonDocument.Parse(File.ReadAllText(v2path)).RootElement;
            var reposN = v2doc.GetProperty("repos").GetArrayLength();
            if (reposN != 2) throw new Exception($"repos[] has {reposN} entries, expected exactly 2 (cleanrepo contributes none)");
            Console.WriteLine("ok   ingest-mixed-run (lockfile-less repo adds no repos[] entry)"); pass++;
            Directory.Delete(mixedTmp, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL ingest-mixed-run: {ex.Message}"); }

        // SPEC-008 §7.5: v2 duplicate repos[] entry -> typed rejection
        try
        {
            var anyF = Directory.GetDirectories(fixturesRoot).First(d => File.Exists(Path.Combine(d, "golden", "plan.json")));
            var s4 = Path.Combine(Path.GetTempPath(), "ua-v2dup-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(s4, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(anyF, "input"))) File.Copy(f, Path.Combine(s4, "input", Path.GetFileName(f)));
            foreach (var stale in new[] { "lockfile-rows.v0.json", "lockfile-rows.v1.json" }) File.Delete(Path.Combine(s4, "input", stale));
            File.WriteAllText(Path.Combine(s4, "input", "lockfile-rows.v2.json"),
                "{\"schemaVersion\":\"lockfile-rows.v2\",\"repos\":[{\"repo\":\"R-dup\",\"rows\":[{\"packageId\":\"P\",\"type\":\"direct\",\"version\":\"1.0.0\",\"lockfile\":\"a\",\"tfm\":\"net8.0\"}]},{\"repo\":\"R-dup\",\"rows\":[{\"packageId\":\"Q\",\"type\":\"direct\",\"version\":\"1.0.0\",\"lockfile\":\"a\",\"tfm\":\"net8.0\"}]}]}");
            try { LoadEngine(s4).BuildPlan(); fail++; Console.WriteLine("FAIL v2-duplicate-repo-rejection (no exception)"); }
            catch (UaException ex) when (ex.Message.Contains("duplicate repos[] entry"))
            { Console.WriteLine("ok   v2-duplicate-repo-rejection"); pass++; }
            Directory.Delete(s4, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL v2-duplicate-repo-rejection: {ex.Message}"); }

        // SPEC-008 §7.5: v1 + v2 files both present -> typed rejection
        try
        {
            var withL = Directory.GetDirectories(fixturesRoot).First(d => File.Exists(Path.Combine(d, "input", "lockfile-rows.v1.json")));
            var s5 = Path.Combine(Path.GetTempPath(), "ua-bothfiles-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(s5, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(withL, "input"))) File.Copy(f, Path.Combine(s5, "input", Path.GetFileName(f)));
            File.Copy(Path.Combine(s5, "input", "lockfile-rows.v1.json"), Path.Combine(s5, "input", "lockfile-rows.v2.json"));
            try { LoadEngine(s5).BuildPlan(); fail++; Console.WriteLine("FAIL both-lockfile-files-rejection (no exception)"); }
            catch (UaException ex) when (ex.Message.Contains("multiple lockfile inputs"))
            { Console.WriteLine("ok   both-lockfile-files-rejection"); pass++; }
            Directory.Delete(s5, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL both-lockfile-files-rejection: {ex.Message}"); }

        // SPEC-009 acceptance: ua apply — manifest golden, verified patch, refusals, policies, gates
        try
        {
            var f13 = Path.Combine(fixturesRoot, "F13-apply-cpm-override");
            var out13 = Path.Combine(Path.GetTempPath(), "ua-apply13-" + Guid.NewGuid().ToString("N")[..8]);
            var rcA = Apply.Run(f13, Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "sources", "svc")), out13);
            if (rcA != 0) throw new Exception($"apply exited {rcA}");
            var manifestGolden = File.ReadAllText(Path.Combine(f13, "golden", "apply.v1.json"));
            var manifestActual = File.ReadAllText(Path.Combine(out13, "apply.v1.json"));
            if (manifestActual != manifestGolden) throw new Exception("apply.v1 manifest differs from golden");
            var patchGolden = File.ReadAllText(Path.Combine(f13, "golden", "patches", "wave-1.patch"));
            var patchActual = File.ReadAllText(Path.Combine(out13, "wave-1.patch"));
            if (patchActual != patchGolden) throw new Exception("wave-1.patch differs from golden");
            if (!patchActual.Contains("Version=\"1.1.0\"") || !patchActual.Contains("VersionOverride=\"1.1.0\"")) throw new Exception("patch misses an expected edit");
            Console.WriteLine("ok   apply-F13 (manifest + verified patch goldens)"); pass++;
            Directory.Delete(out13, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL apply-F13: {ex.Message}"); }

        // already-satisfied: pin at 2.0.0 under a 1.0.0→1.1.0 delta ⇒ N4, empty patch, never a downgrade
        try
        {
            var f13b = Path.Combine(fixturesRoot, "F13-apply-cpm-override");
            var scratch = Path.Combine(Path.GetTempPath(), "ua-ahead-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(scratch, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(f13b, "input"))) File.Copy(f, Path.Combine(scratch, "input", Path.GetFileName(f)));
            var pePath = Path.Combine(scratch, "input", "package-evidence.v0.json");
            File.WriteAllText(pePath, File.ReadAllText(pePath).Replace("\"1.0.0\"", "\"2.0.0\"").Replace("\"0.9.0\"", "\"2.0.0\""));
            var repoCopy = Path.Combine(Path.GetTempPath(), "ua-ahead-repo-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "sources", "svc"), repoCopy);
            File.WriteAllText(Path.Combine(repoCopy, "Directory.Packages.props"),
                File.ReadAllText(Path.Combine(repoCopy, "Directory.Packages.props")).Replace("Version=\"1.0.0\"", "Version=\"2.0.0\""));
            File.WriteAllText(Path.Combine(repoCopy, "src", "Worker", "Worker.csproj"),
                File.ReadAllText(Path.Combine(repoCopy, "src", "Worker", "Worker.csproj")).Replace("VersionOverride=\"0.9.0\"", "VersionOverride=\"2.0.0\""));
            var outA = Path.Combine(Path.GetTempPath(), "ua-ahead-out-" + Guid.NewGuid().ToString("N")[..8]);
            var rcB = Apply.Run(scratch, repoCopy, outA);
            var mText = File.ReadAllText(Path.Combine(outA, "apply.v1.json"));
            if (rcB != 0) throw new Exception($"exited {rcB}");
            if (!mText.Contains("already at or above")) throw new Exception("missing N4 already-satisfied note");
            if (mText.Contains("\"kind\": \"E2\"")) throw new Exception("E2 edit emitted for an already-satisfied pin (downgrade)");
            var patchPath = Path.Combine(outA, "wave-1.patch");
            if (!File.Exists(patchPath) || File.ReadAllText(patchPath) != "") throw new Exception("expected an EMPTY wave-1.patch");
            Console.WriteLine("ok   apply-already-satisfied (N4, empty patch, no downgrade)"); pass++;
            Directory.Delete(scratch, true); Directory.Delete(repoCopy, true); Directory.Delete(outA, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL apply-already-satisfied: {ex.Message}"); }

        // stale-evidence refusal: checkout pin drifted to 9.9.9 ⇒ exit 6
        try
        {
            var f13c = Path.Combine(fixturesRoot, "F13-apply-cpm-override");
            var repoCopy2 = Path.Combine(Path.GetTempPath(), "ua-stale-repo-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "sources", "svc"), repoCopy2);
            File.WriteAllText(Path.Combine(repoCopy2, "Directory.Packages.props"),
                File.ReadAllText(Path.Combine(repoCopy2, "Directory.Packages.props")).Replace("Version=\"1.0.0\"", "Version=\"9.9.9\""));
            var outS = Path.Combine(Path.GetTempPath(), "ua-stale-out-" + Guid.NewGuid().ToString("N")[..8]);
            var rcC = Apply.Run(f13c, repoCopy2, outS);
            if (rcC != 6) throw new Exception($"exited {rcC}, expected 6");
            Console.WriteLine("ok   apply-stale-refusal (exit 6)"); pass++;
            if (Directory.Exists(repoCopy2)) Directory.Delete(repoCopy2, true); if (Directory.Exists(outS)) Directory.Delete(outS, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL apply-stale-refusal: {ex.Message}"); }

        // ambiguous-site refusal: two identical pins in the window ⇒ exit 6
        try
        {
            var f13d = Path.Combine(fixturesRoot, "F13-apply-cpm-override");
            var repoCopy3 = Path.Combine(Path.GetTempPath(), "ua-amb-repo-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "sources", "svc"), repoCopy3);
            File.WriteAllText(Path.Combine(repoCopy3, "Directory.Packages.props"),
                "<Project>\n<ItemGroup><PackageVersion Include=\"Contoso.Core\" Version=\"1.0.0\" /><PackageVersion Include=\"Contoso.Core\" Version=\"1.0.0\" /></ItemGroup></Project>\n");
            var outM = Path.Combine(Path.GetTempPath(), "ua-amb-out-" + Guid.NewGuid().ToString("N")[..8]);
            var rcD = Apply.Run(f13d, repoCopy3, outM);
            if (rcD != 6) throw new Exception($"exited {rcD}, expected 6");
            Console.WriteLine("ok   apply-ambiguous-refusal (exit 6)"); pass++;
            if (Directory.Exists(repoCopy3)) Directory.Delete(repoCopy3, true); if (Directory.Exists(outM)) Directory.Delete(outM, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL apply-ambiguous-refusal: {ex.Message}"); }

        // transitive honesty: the Newtonsoft.Json delta on svc ⇒ all no-edit-site (N1)
        try
        {
            var tmpNJ = Path.Combine(Path.GetTempPath(), "ua-nj-" + Guid.NewGuid().ToString("N")[..8]);
            var rcE = Ingest.Run(new[] { Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "scans", "svc")) }, tmpNJ,
                Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "sidecars", "producer-evidence.v0.json")),
                Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "sidecars", "ownership.v0.json")),
                Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "sidecars", "delta.json")));
            if (rcE != 0) throw new Exception($"ingest exited {rcE}");
            var outNJ = Path.Combine(Path.GetTempPath(), "ua-nj-out-" + Guid.NewGuid().ToString("N")[..8]);
            var rcF = Apply.Run(tmpNJ, null, outNJ);
            if (rcF != 0) throw new Exception($"apply exited {rcF}");
            var t = File.ReadAllText(Path.Combine(outNJ, "apply.v1.json"));
            if (!t.Contains("\"action\": \"no-edit-site\"") || !t.Contains("no evidenced direct edit site")) throw new Exception("expected N1 no-edit-site");
            if (t.Contains("\"kind\": \"E")) throw new Exception("transitive exposure must not produce edits");
            Console.WriteLine("ok   apply-transitive-honesty (all no-edit-site, N1)"); pass++;
            Directory.Delete(tmpNJ, true); Directory.Delete(outNJ, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL apply-transitive-honesty: {ex.Message}"); }

        // gated waves: conditional (F2) and provisional (F3b) gates carried verbatim
        try
        {
            foreach (var (name, wantCond, wantBlocked) in new[] { ("F2-outside-team-producer", true, false), ("F3b-conflicting-producers", false, true) })
            {
                var dir = Path.Combine(fixturesRoot, name);
                var outG = Path.Combine(Path.GetTempPath(), "ua-gate-" + Guid.NewGuid().ToString("N")[..8]);
                var rcG = Apply.Run(dir, null, outG);
                if (rcG != 0) throw new Exception($"{name}: apply exited {rcG}");
                var t = File.ReadAllText(Path.Combine(outG, "apply.v1.json"));
                if (wantCond && !t.Contains("\"condition\"")) throw new Exception($"{name}: condition not carried");
                if (wantBlocked && !t.Contains("\"blockedOn\"")) throw new Exception($"{name}: blockedOn not carried");
                Directory.Delete(outG, true);
            }
            Console.WriteLine("ok   apply-gated-waves (condition + blockedOn verbatim)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL apply-gated-waves: {ex.Message}"); }

        // SPEC-010 acceptance: ua push — dry-run golden, local execution, gated commits, refusals, rollback
        string ScratchRepo(string name, string fixture = "F13-apply-cpm-override")
        {
            var dir = Path.Combine(Path.GetTempPath(), "ua-push-" + name + "-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "sources", "svc"), dir);
            Push.Git(dir, "init -q .", out _, out _);
            Push.Git(dir, "config user.email ua@test", out _, out _);
            Push.Git(dir, "config user.name ua-selftest", out _, out _);
            Push.Git(dir, "add -A", out _, out _);
            Push.Git(dir, "-c user.email=ua@test -c user.name=ua commit -qm base", out _, out _);
            Push.Git(dir, "branch -m main", out _, out _);
            return dir;
        }
        try
        {
            // dry-run golden (fixture-only, no git, no checkout)
            var dryOut = Path.Combine(Path.GetTempPath(), "ua-pd-" + Guid.NewGuid().ToString("N")[..8]);
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), null, null, dryOut, true, false) != 0) throw new Exception("dry-run exited nonzero");
            if (File.ReadAllText(Path.Combine(dryOut, "push.v1.json")) != File.ReadAllText(Path.Combine(fixturesRoot, "F13-apply-cpm-override", "golden", "push", "dry-run.v1.json")))
                throw new Exception("dry-run report differs from golden");
            Console.WriteLine("ok   push-dryrun-golden (fixture-only)"); pass++;
            Directory.Delete(dryOut, true);

            // local execution: branch + gated commit + clean tree + waves[] golden + observed sha
            var repo1 = ScratchRepo("local");
            var out1 = Path.Combine(Path.GetTempPath(), "ua-pl-" + Guid.NewGuid().ToString("N")[..8]);
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), repo1, "main", out1, false, false) != 0) throw new Exception("local push exited nonzero");
            Push.Git(repo1, "rev-parse --verify ua/wave-1/Contoso.Core-1.0.0-to-1.1.0", out var sha1, out _);
            if (sha1.Trim().Length != 40) throw new Exception("branch missing");
            Push.Git(repo1, "log --format=%s -1 ua/wave-1/Contoso.Core-1.0.0-to-1.1.0", out var subj1, out _);
            if (subj1.Trim() != "ua: Contoso.Core 1.0.0 -> 1.1.0 (wave 1)") throw new Exception($"commit subject: {subj1.Trim()}");
            Push.Git(repo1, "log --format=%b -1 ua/wave-1/Contoso.Core-1.0.0-to-1.1.0", out var body1, out _);
            if (!body1.Contains("wave-status: ready") || !body1.Contains("prerequisite: Contoso.Core 1.1.0 available")) throw new Exception("commit body missing gates/prereqs");
            Push.Git(repo1, "status --porcelain", out var st1, out _);
            if (st1.Trim().Length > 0) throw new Exception("working tree dirty after push");
            Push.Git(repo1, "rev-parse --abbrev-ref HEAD", out var head1, out _);
            if (head1.Trim() != "main") throw new Exception($"user branch changed: {head1.Trim()}");
            var rep1 = File.ReadAllText(Path.Combine(out1, "push.v1.json"));
            if (!rep1.Contains(sha1.Trim()[..10])) throw new Exception("observed sha missing from report");
            Console.WriteLine("ok   push-local-F13 (branch+commit+gates, clean tree, sha observed)"); pass++;
            // idempotency-by-refusal: re-run refuses, mutates nothing
            Push.Git(repo1, "for-each-ref", out var refsBefore, out _);
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), repo1, "main", out1, false, false) != 7) throw new Exception("re-run must refuse exit 7");
            Push.Git(repo1, "for-each-ref", out var refsAfter, out _);
            if (refsBefore != refsAfter) throw new Exception("refusing re-run mutated refs");
            Console.WriteLine("ok   push-idempotency (re-run refuses, nothing force-updated)"); pass++;
            ForceDelete(repo1); if (Directory.Exists(out1)) Directory.Delete(out1, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL push-F13-battery: {ex.Message}"); }

        try
        {
            // gated commits (SPEC-010 acceptance 3a): conditional (F14) and provisional (F15) commit bodies carry P3b/P3c
            string GateScratch(string fixture)
            {
                var dir = Path.Combine(Path.GetTempPath(), "ua-gate-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(Path.Combine(dir, "src"));
                File.WriteAllText(Path.Combine(dir, "src", "App.csproj"),
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup><PackageReference Include=\"Bogus.Pin\" Version=\"1.0.0\" /></ItemGroup>\n</Project>\n");
                Push.Git(dir, "init -q .", out _, out _);
            Push.Git(dir, "config user.email ua@test", out _, out _);
            Push.Git(dir, "config user.name ua-selftest", out _, out _);
                Push.Git(dir, "add -A", out _, out _);
                Push.Git(dir, "-c user.email=ua@test -c user.name=ua commit -qm base", out _, out _);
                Push.Git(dir, "branch -m main", out _, out _);
                return dir;
            }
            foreach (var (name, want) in new[] {
                ("F14-gated-apply-conditional", new[] { "wave-status: conditional", "condition: external team-b releases Bogus.Pin >= 2.0.0" }),
                ("F15-gated-apply-provisional", new[] { "wave-status: provisional", "blocked-on: C1" }),
            })
            {
                var repo = GateScratch(name);
                var og = Path.Combine(Path.GetTempPath(), "ua-pg-" + Guid.NewGuid().ToString("N")[..8]);
                if (Push.Run(Path.Combine(fixturesRoot, name), repo, "main", og, false, false) != 0) throw new Exception($"{name}: push exited nonzero");
                Push.Git(repo, "log --format=%b -1 ua/wave-2/Bogus.Pin-1.0.0-to-2.0.0", out var body, out _);
                foreach (var line in want)
                    if (!body.Contains(line)) throw new Exception($"{name}: commit body missing '{line}'");
                ForceDelete(repo); if (Directory.Exists(og)) Directory.Delete(og, true);
            }
            Console.WriteLine("ok   push-gated-commits (conditional + provisional bodies)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL push-gated-commits: {ex.Message}"); }

        try
        {
            // refusals: dirty tree, missing base, --pr without token, rollback on hook failure, base drift
            var repo2 = ScratchRepo("dirty");
            File.WriteAllText(Path.Combine(repo2, "uncommitted.txt"), "x");
            var o2 = Path.Combine(Path.GetTempPath(), "ua-pr2-" + Guid.NewGuid().ToString("N")[..8]);
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), repo2, "main", o2, false, false) != 7) throw new Exception("dirty tree must refuse");
            Console.WriteLine("ok   push-refusal-dirty-tree"); pass++;
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), repo2, "nope", o2, false, false) != 7) throw new Exception("missing base must refuse");
            Console.WriteLine("ok   push-refusal-missing-base"); pass++;
            var savedToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), repo2, "main", o2, false, true) != 7) throw new Exception("--pr without GITHUB_TOKEN must refuse");
            if (savedToken is not null) Environment.SetEnvironmentVariable("GITHUB_TOKEN", savedToken);
            Console.WriteLine("ok   push-refusal-pr-without-token"); pass++;
            File.Delete(Path.Combine(repo2, "uncommitted.txt"));
            ForceDelete(repo2); if (Directory.Exists(o2)) Directory.Delete(o2, true);

            // rollback: failing pre-commit hook -> refusal AND the branch never survives (SPEC-010 §3.3b)
            var repo3 = ScratchRepo("hook");
            var hookPath = Path.Combine(repo3, ".git", "hooks", "pre-commit");
            File.WriteAllText(hookPath, "#!/bin/sh\nexit 1\n");
            if (OperatingSystem.IsWindows())
                { /* Git for Windows runs hooks via its bundled sh without the exec bit */ }
                else File.SetUnixFileMode(hookPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var o3 = Path.Combine(Path.GetTempPath(), "ua-pr3-" + Guid.NewGuid().ToString("N")[..8]);
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), repo3, "main", o3, false, false) != 7) throw new Exception("hook failure must refuse exit 7");
            Push.Git(repo3, "rev-parse --verify ua/wave-1/Contoso.Core-1.0.0-to-1.1.0", out var gone, out _);
            if (gone.Trim().Length > 0) throw new Exception("branch survived a failed commit — rollback broken");
            Console.WriteLine("ok   push-rollback-on-hook-failure (branch absent)"); pass++;
            ForceDelete(repo3); if (Directory.Exists(o3)) Directory.Delete(o3, true);

            // base drift: base pin no longer matches evidence -> verification refusal even though the working tree is clean
            var repo4 = ScratchRepo("drift");
            File.WriteAllText(Path.Combine(repo4, "Directory.Packages.props"),
                File.ReadAllText(Path.Combine(repo4, "Directory.Packages.props")).Replace("Version=\"1.0.0\"", "Version=\"9.9.9\""));
            Push.Git(repo4, "add -A", out _, out _);
            Push.Git(repo4, "-c user.email=ua@test -c user.name=ua commit -qm drift", out _, out _);
            var o4 = Path.Combine(Path.GetTempPath(), "ua-pr4-" + Guid.NewGuid().ToString("N")[..8]);
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), repo4, "main", o4, false, false) != 6) throw new Exception("base drift must refuse via apply verification (exit 6)");
            Console.WriteLine("ok   push-refusal-base-drift (verification targets the base tree)"); pass++;
            ForceDelete(repo4); if (Directory.Exists(o4)) Directory.Delete(o4, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL push-refusals: {ex.Message}"); }

        // SPEC-011: --scans-root — equality, determinism, broken child, missing root, union, sidecar explicitness
        try
        {
            var rich = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var scansRoot = Path.Combine(rich, "scans");
            (string Name, string Text)[] OutputFiles(string dir) => Directory.GetFiles(Path.Combine(dir, "input")).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToArray();

            var a = Path.Combine(Path.GetTempPath(), "ua-sr-a-" + Guid.NewGuid().ToString("N")[..8]);
            var b = Path.Combine(Path.GetTempPath(), "ua-sr-b-" + Guid.NewGuid().ToString("N")[..8]);
            var side = Path.Combine(rich, "sidecars-estate2");
            var sideP = new[] { Path.Combine(rich, "sidecars", "producer-evidence.v0.json"), Path.Combine(rich, "sidecars", "ownership.v0.json"), Path.Combine(rich, "sidecars", "delta.json") };
            if (Ingest.Run(Array.Empty<string>(), a, sideP[0], sideP[1], sideP[2], scansRoot) != 0) throw new Exception("scans-root run failed");
            var explicitDirs = new[] { "billing", "cleanrepo", "corelib", "legacy", "shipping", "svc" }.Select(n => Path.Combine(scansRoot, n)).ToArray(); // discovery order — svc-net48 EXCLUDED (alternate svc snapshot; deliberate combination requires explicit dirs)
            if (Ingest.Run(explicitDirs, b, sideP[0], sideP[1], sideP[2]) != 0) throw new Exception("explicit run failed");
            if (!OutputFiles(a).SequenceEqual(OutputFiles(b))) throw new Exception("scans-root output differs from explicit-dirs output");
            Console.WriteLine("ok   ingest-scans-root-equality (byte-identical to explicit dirs)"); pass++;

            var c = Path.Combine(Path.GetTempPath(), "ua-sr-c-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(Array.Empty<string>(), c, sideP[0], sideP[1], sideP[2], scansRoot) != 0) throw new Exception("second run failed");
            if (!OutputFiles(a).SequenceEqual(OutputFiles(c))) throw new Exception("two scans-root runs differ");
            Console.WriteLine("ok   ingest-scans-root-determinism"); pass++;

            var u = Path.Combine(Path.GetTempPath(), "ua-sr-u-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(new[] { Path.Combine(scansRoot, "svc") }, u, sideP[0], sideP[1], sideP[2], scansRoot) != 0) throw new Exception("union run failed");
            var ub = Path.Combine(Path.GetTempPath(), "ua-sr-ub-" + Guid.NewGuid().ToString("N")[..8]);
            // reference: explicit-first order (svc, then discovery name order) — the union's effective order
            var unionOrder = new[] { "svc", "billing", "cleanrepo", "corelib", "legacy", "shipping" }.Select(n => Path.Combine(scansRoot, n)).ToArray();
            if (Ingest.Run(unionOrder, ub, sideP[0], sideP[1], sideP[2]) != 0) throw new Exception("union reference run failed");
            if (!OutputFiles(ub).SequenceEqual(OutputFiles(u))) throw new Exception("union output differs (explicit dir double-counted or dropped)");
            Console.WriteLine("ok   ingest-scans-root-union (explicit + discovered)"); pass++;
            foreach (var d in new[] { a, b, c, u, ub }) Directory.Delete(d, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL ingest-scans-root: {ex.Message}"); }

        try
        {
            var rich2 = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            // broken child: manifest without facts warns and is skipped; good child still ingests
            var root1 = Path.Combine(Path.GetTempPath(), "ua-sr-root-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root1, "broken")); Directory.CreateDirectory(Path.Combine(root1, "good"));
            File.WriteAllText(Path.Combine(root1, "broken", "scan-manifest.json"), "{}");
            File.Copy(Path.Combine(rich2, "scans", "cleanrepo", "facts.ndjson"), Path.Combine(root1, "good", "facts.ndjson"));
            File.Copy(Path.Combine(rich2, "scans", "cleanrepo", "scan-manifest.json"), Path.Combine(root1, "good", "scan-manifest.json"));
            var o1 = Path.Combine(Path.GetTempPath(), "ua-sr-o1-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(Array.Empty<string>(), o1, null, null, Path.Combine(rich2, "sidecars-estate2", "delta.json"), root1) != 0) throw new Exception("broken-child run failed");
            if (!File.Exists(Path.Combine(o1, "input", "package-evidence.v0.json"))) throw new Exception("good child not ingested");
            Console.WriteLine("ok   ingest-scans-root-broken-child (warn + skip, good child ingests)"); pass++;
            Directory.Delete(root1, true); Directory.Delete(o1, true);

            if (Ingest.Run(Array.Empty<string>(), Path.Combine(Path.GetTempPath(), "ua-x"), null, null, null, Path.Combine(Path.GetTempPath(), "ua-nope-" + Guid.NewGuid().ToString("N"))[0..^1] + "-absent") != 1)
                throw new Exception("missing root must exit 1");
            Console.WriteLine("ok   ingest-scans-root-missing-root (exit 1)"); pass++;

            // sidecar explicitness: two dirs, sidecar PRESENT in one dir but flag omitted => typed error (per kind)
            var d1 = Path.Combine(rich2, "scans", "svc"); var d2 = Path.Combine(rich2, "scans", "billing");
            var o2 = Path.Combine(Path.GetTempPath(), "ua-sr-o2-" + Guid.NewGuid().ToString("N")[..8]);
            var eP = Path.Combine(rich2, "sidecars-estate2", "producer-evidence.v0.json");
            var eO = Path.Combine(rich2, "sidecars-estate2", "ownership.v0.json");
            var eD = Path.Combine(rich2, "sidecars-estate2", "delta.json");
            foreach (var omit in new[] { "producer", "ownership", "delta" })
            {
                var tmpScan = Path.Combine(Path.GetTempPath(), "ua-sr-scan-" + Guid.NewGuid().ToString("N")[..8]);
                CopyDir(d1, tmpScan);
                // present-but-implicit: copy the omitted sidecar INTO a scan dir — old code would auto-find it
                var srcFile = omit switch { "producer" => eP, "ownership" => eO, _ => eD };
                File.Copy(srcFile, Path.Combine(tmpScan, Path.GetFileName(srcFile)), true);
                var rc = Ingest.Run(new[] { tmpScan, d2 }, o2,
                    omit == "producer" ? null : eP,
                    omit == "ownership" ? null : eO,
                    omit == "delta" ? null : eD);
                if (rc != 1) throw new Exception($"present-but-implicit {omit}: exit {rc}, expected 1");
                ForceDelete(tmpScan);
            }
            Console.WriteLine("ok   ingest-scans-root-sidecar-explicitness (present-but-implicit refused, per kind)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL ingest-scans-root-cases: {ex.Message}"); }

        // SPEC-012 acceptance 2: from-version markers, trailer once per repo, rule-c gaps
        try
        {
            string PlanText(string f) => File.ReadAllText(Path.Combine(fixturesRoot, f, "golden", "plan.json"));
            var f9 = PlanText("F9-multi-lockfile-disagreement");
            if (!f9.Contains("12.0.3 in src/Worker/packages.lock.json (net8.0) [delta from-version]")) throw new Exception("F9: Newtonsoft.Json 12.0.3 not marked");
            if (f9.Contains("0.9.0 in src/Worker/packages.lock.json (net8.0) [delta from-version]")) throw new Exception("F9: Contoso.Core wrongly marked");
            var t = f9.Split("could not parse and contributed no rows", StringSplitOptions.None).Length - 1;
            if (t != 1) throw new Exception($"F9: trailer appears {t}x, expected exactly 1");
            if (!f9.Contains("svc coverage (lockfile-evidenced)")) throw new Exception("F9: rule-c gap missing");
            var f11 = PlanText("F11-mixed-relation");
            if (!f11.Contains("1.0.0 in src/LibA/packages.lock.json (net8.0) [delta from-version]")) throw new Exception("F11: marker missing");
            if (!f11.Contains("R11 coverage (lockfile-evidenced)")) throw new Exception("F11: rule-c gap missing");
            var f12 = PlanText("F12-multi-repo-lockfiles");
            if (f12.Contains("coverage (lockfile-evidenced)")) throw new Exception("F12: billing/shipping are COMPLETE since SPEC-015 — no G2 gaps expected");
            if (!f12.Contains("scanNotes")) throw new Exception("F12: compile-health notes missing");
            if (f12.Contains("[delta from-version]")) throw new Exception("F12: cross-repo difference must not be marked");
            var f13 = PlanText("F13-apply-cpm-override");
            if (!f13.Contains("1.0.0 in src/Api/packages.lock.json (net8.0) [delta from-version]")) throw new Exception("F13: marker missing");
            if (f13.Split("[delta from-version]").Length - 1 != 1) throw new Exception("F13: exactly one marker expected (delta package only)");
            var f10 = PlanText("F10-multi-lockfile-agreement");
            if (f10.Contains("[delta from-version]") || f10.Contains("lockfile-evidenced")) throw new Exception("F10: agreement fixture must be untouched");
            Console.WriteLine("ok   findings-ux (markers, trailer-once, rule-c gaps)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL findings-ux: {ex.Message}"); }

        // SPEC-013: --github-api override — real-path loopback stub, dry-run endpoint, refusals, env precedence, ssh origin
        try
        {
            // 1b: the override is exercised on the REAL HTTP path via an in-process loopback stub
            System.Net.HttpListener? listener = null;
            var port = 0;
            for (var bindAttempt = 0; bindAttempt < 8 && listener is null; bindAttempt++)
            {
                var candidate = new System.Net.HttpListener();
                candidate.Prefixes.Add($"http://127.0.0.1:{18080 + Random.Shared.Next(2000)}/");
                try { candidate.Start(); listener = candidate; port = int.Parse(candidate.Prefixes.First().Split(':')[2].TrimEnd('/')); } // prefix already carries the offset — parse, don't re-add
                catch (System.Net.HttpListenerException) { /* port busy — try another */ }
            }
            if (listener is null) throw new Exception("could not bind a loopback listener after 8 attempts");
            System.Threading.Tasks.Task.Run(async () =>
            {
                var ctx = await listener.GetContextAsync();
                var ok = ctx.Request.HttpMethod == "POST"
                    && ctx.Request.Url!.AbsolutePath == "/repos/joefeser/glm-stub/pulls"
                    && (ctx.Request.Headers["Authorization"] ?? "").StartsWith("Bearer ");
                var resp = ctx.Response;
                if (!ok) { resp.StatusCode = 400; resp.Close(); return; } // a bad request must FAIL the test, never pass
                var payload = System.Text.Encoding.UTF8.GetBytes($"{{\"html_url\": \"http://127.0.0.1:{port}/stub-pr\"}}");
                resp.ContentType = "application/json";
                resp.OutputStream.Write(payload, 0, payload.Length);
                resp.Close();
            });
            var stubRepo = Path.Combine(Path.GetTempPath(), "ua-ghe-repo-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(stubRepo, "src"));
            File.WriteAllText(Path.Combine(stubRepo, "src", "App.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup><PackageReference Include=\"Bogus.Pin\" Version=\"1.0.0\" /></ItemGroup>\n</Project>\n");
            Push.Git(stubRepo, "init -q .", out _, out _);
            Push.Git(stubRepo, "config user.email ua@test", out _, out _);
            Push.Git(stubRepo, "config user.name ua", out _, out _);
            Push.Git(stubRepo, "add -A", out _, out _);
            Push.Git(stubRepo, "commit -qm base", out _, out _);
            Push.Git(stubRepo, "branch -m main", out _, out _);
            Push.Git(stubRepo, "remote add origin git@ghe.example.com:joefeser/glm-stub.git", out _, out _); // scp-style SSH origin (what validation parses)
            // the ghe host is unreachable offline — rewrite pushes to a local bare via insteadOf; validation
            // still reads the ghe-style origin URL, the actual push lands on the bare repo
            var bareRoot = Path.Combine(Path.GetTempPath(), "ua-ghe-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(bareRoot, "joefeser"));
            var bare = Path.Combine(bareRoot, "joefeser", "glm-stub.git");
            Push.Git(Path.GetTempPath(), $"clone --bare \"{stubRepo}\" \"{bare}\"", out _, out _);
            Push.Git(stubRepo, $"config url.\"{bareRoot}/\".insteadOf git@ghe.example.com:", out _, out _); // ghe:owner/repo -> <bareRoot>/owner/repo
            var gheOut = Path.Combine(Path.GetTempPath(), "ua-ghe-out-" + Guid.NewGuid().ToString("N")[..8]);
            var savedTok = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            var savedApi = Environment.GetEnvironmentVariable("GITHUB_API_URL");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", "stub-token");
            Environment.SetEnvironmentVariable("GITHUB_API_URL", null);
            var rcGhe = Push.Run(Path.Combine(fixturesRoot, "F14-gated-apply-conditional"), stubRepo, "main", gheOut, false, true, $"http://127.0.0.1:{port}");
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", savedTok);
            if (savedApi is not null) Environment.SetEnvironmentVariable("GITHUB_API_URL", savedApi);
            var stubHit = File.ReadAllText(Path.Combine(gheOut, "push.v1.json")).Contains("stub-pr");
            listener.Stop();
            if (rcGhe != 0 || !stubHit) throw new Exception($"loopback stub run: exit {rcGhe}, prUrl recorded: {stubHit}");
            Console.WriteLine("ok   push-ghe-override (real HTTP path via loopback stub; scp-style origin parsed)"); pass++;
            ForceDelete(stubRepo); ForceDelete(bare); Directory.Delete(gheOut, true);

            // dry-run renders the overridden endpoint (no network)
            var dr = Path.Combine(Path.GetTempPath(), "ua-ghe-dry-" + Guid.NewGuid().ToString("N")[..8]);
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), null, null, dr, true, true, "https://ghe.example.com/api/v3/") != 0) throw new Exception("dry-run failed"); // --pr dry-run: endpoint visible, no token needed, no network
            var drText = File.ReadAllText(Path.Combine(dr, "push.v1.json"));
            if (!drText.Contains("POST https://ghe.example.com/api/v3/repos/<owner>/<repo>/pulls")) throw new Exception("dry-run endpoint not rendered (trailing slash trimmed?)");
            Console.WriteLine("ok   push-ghe-dryrun-endpoint"); pass++;
            Directory.Delete(dr, true);

            // refusals: non-loopback http; userinfo in base
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), null, null, Path.Combine(Path.GetTempPath(), "ua-x1"), true, true, "http://ghe.example.com/api/v3") != 7) throw new Exception("non-loopback http must refuse");
            if (Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), null, null, Path.Combine(Path.GetTempPath(), "ua-x2"), true, true, "https://user:pass@ghe.example.com/api/v3") != 7) throw new Exception("userinfo base must refuse");
            Console.WriteLine("ok   push-ghe-refusals (plaintext non-loopback; embedded credentials)"); pass++;

            // env precedence: GITHUB_API_URL honored, flag wins
            Environment.SetEnvironmentVariable("GITHUB_API_URL", "https://env.example.com/api/v3");
            var drEnv = Path.Combine(Path.GetTempPath(), "ua-ghe-env-" + Guid.NewGuid().ToString("N")[..8]);
            Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), null, null, drEnv, true, true, null);
            var envText = File.ReadAllText(Path.Combine(drEnv, "push.v1.json"));
            Environment.SetEnvironmentVariable("GITHUB_API_URL", savedApi);
            if (!envText.Contains("https://env.example.com/api/v3")) throw new Exception("GITHUB_API_URL not honored");
            var drFlag = Path.Combine(Path.GetTempPath(), "ua-ghe-flag-" + Guid.NewGuid().ToString("N")[..8]);
            Push.Run(Path.Combine(fixturesRoot, "F13-apply-cpm-override"), null, null, drFlag, true, true, "https://flag.example.com/api/v3");
            var flagText = File.ReadAllText(Path.Combine(drFlag, "push.v1.json"));
            if (!flagText.Contains("https://flag.example.com/api/v3") || flagText.Contains("env.example.com")) throw new Exception("flag must override env");
            Console.WriteLine("ok   push-ghe-env-precedence (env honored; flag wins)"); pass++;
            Directory.Delete(drEnv, true); Directory.Delete(drFlag, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL push-ghe: {ex.Message}"); }

        // net48 short-form evidence (real scan): rows parse, no unsupported gap, both from-version markers
        try
        {
            var richN = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var tmpN = Path.Combine(Path.GetTempPath(), "ua-n48-" + Guid.NewGuid().ToString("N")[..8]);
            var rcN = Ingest.Run(new[] { Path.Combine(richN, "scans", "svc-net48") }, tmpN,
                Path.Combine(richN, "sidecars", "producer-evidence.v0.json"),
                Path.Combine(richN, "sidecars", "ownership.v0.json"),
                Path.Combine(richN, "sidecars", "delta.json"));
            if (rcN != 0) throw new Exception($"ingest exited {rcN}");
            var lockDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(tmpN, "input", "lockfile-rows.v2.json"))).RootElement;
            var allRows = lockDoc.GetProperty("repos")[0].GetProperty("rows");
            if (allRows.GetArrayLength() != 7) throw new Exception($"expected 7 lockfile rows (net48 parses), got {allRows.GetArrayLength()}");
            // exact net48 resolutions — not just a row count
            var net48Rows = allRows.EnumerateArray()
                .Where(r => r.GetProperty("tfm").GetString() == "net48")
                .Select(r => (r.GetProperty("packageId").GetString(), r.GetProperty("version").GetString()))
                .OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
            var want = new[] { ("Contoso.Core", "0.9.0"), ("Newtonsoft.Json", "12.0.3") }.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
            if (!net48Rows.SequenceEqual(want)) throw new Exception($"net48 rows wrong: {string.Join(",", net48Rows)}");
            var repN = Report.Render(LoadEngine(tmpN).BuildPlan());
            if (repN.Contains("could not parse and contributed no rows")) throw new Exception("T10a trailer present but nothing is unsupported");
            // exact marked resolutions — a wrongly-marked version fails, not just a wrong count
            if (!repN.Contains("12.0.3 in src/Worker/packages.lock.json (net48) [delta from-version]")) throw new Exception("net48 12.0.3 not marked");
            if (!repN.Contains("12.0.3 in src/Worker/packages.lock.json (net8.0) [delta from-version]")) throw new Exception("net8.0 12.0.3 not marked");
            if (repN.Contains("13.0.1 in src/Api/packages.lock.json (net8.0) [delta from-version]")) throw new Exception("13.0.1 wrongly marked");
            if (repN.Contains("0.9.0 in src/Worker/packages.lock.json (net48) [delta from-version]")) throw new Exception("Contoso.Core 0.9.0 wrongly marked (not the delta package)");
            var totalMarks = repN.Split("[delta from-version]").Length - 1; // the COMPLETE marked set: exactly the two above, report-wide
            if (totalMarks != 2) throw new Exception($"expected exactly 2 markers report-wide, found {totalMarks}");
            Console.WriteLine("ok   net48-shortform-evidence (7 rows, no unsupported gap, markers on both TFMs)"); pass++;
            Directory.Delete(tmpN, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL net48-shortform-evidence: {ex.Message}"); }

        // SPEC-014: ua ownership — init golden, modes, update preserves/extends, feed-the-plan
        try
        {
            var richO = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var scansO = Path.Combine(richO, "scans");
            var goldenO = File.ReadAllText(Path.Combine(fixturesRoot, "ownership-init.golden.json"));
            var oi = Path.Combine(Path.GetTempPath(), "ua-own-i-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ownership.Init(Array.Empty<string>(), scansO, "team-a", "all-self", null, oi) != 0) throw new Exception("init failed");
            var produced = File.ReadAllText(oi);
            if (produced != goldenO) throw new Exception("init output differs from golden");
            if (!produced.Contains("\"repo\": \"svc\"") || produced.Contains("svc-net48")) throw new Exception("svc-net48 must collapse into svc");
            Console.WriteLine("ok   ownership-init-golden (all-self, ordinal, alternate collapsed)"); pass++;

            var ou = Path.Combine(Path.GetTempPath(), "ua-own-u-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ownership.Init(Array.Empty<string>(), scansO, "team-a", "unassigned", null, ou) != 0) throw new Exception("unassigned init failed");
            var docU = JsonDocument.Parse(File.ReadAllText(ou)).RootElement;
            if (docU.GetProperty("ownerships").GetArrayLength() != 0) throw new Exception("unassigned must emit zero ownerships");
            Console.WriteLine("ok   ownership-init-unassigned (empty ownerships, checklist to stderr)"); pass++;

            if (Ownership.Init(Array.Empty<string>(), scansO, "team-a", "team", "team-a", ou) == 0) throw new Exception("--team == --self must refuse");
            Console.WriteLine("ok   ownership-init-team-equals-self-refused"); pass++;

            // update: manual team preserved verbatim; new repo appended; absent kept + warned; mirrors preserved
            var existing = Path.Combine(Path.GetTempPath(), "ua-own-e-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(existing, JsonSerializer.Serialize(new
            {
                schemaVersion = "ownership.v0",
                selfTeamId = "team-a",
                ownerships = new[] { new { repo = "svc", team = "team-z" }, new { repo = "legacy", team = "team-a" } },
                mirrors = new[] { new { canonical = "legacy", aliases = new[] { "github.com/org/legacy-alias" } } },
            }, Json));
            var sub = Path.Combine(Path.GetTempPath(), "ua-own-s-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(sub);
            foreach (var n in new[] { "svc", "billing" }) CopyDir(Path.Combine(scansO, n), Path.Combine(sub, n));
            var upd = Path.Combine(Path.GetTempPath(), "ua-own-upd-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ownership.Update(Array.Empty<string>(), sub, existing, "new-self", null, "", upd) != 0) throw new Exception("update failed");
            var updDoc = JsonDocument.Parse(File.ReadAllText(upd)).RootElement;
            var entries = updDoc.GetProperty("ownerships").EnumerateArray().ToDictionary(e => e.GetProperty("repo").GetString(), e => e.GetProperty("team").GetString());
            if (entries["svc"] != "team-z") throw new Exception("manual assignment not preserved verbatim");
            if (entries["billing"] != "team-a") throw new Exception("new repo not appended as self");
            if (entries["legacy"] != "team-a") throw new Exception("absent-but-assigned repo not kept");
            if (updDoc.GetProperty("mirrors").GetArrayLength() != 1) throw new Exception("mirrors not preserved");
            Console.WriteLine("ok   ownership-update (manual sacred, new appended, absent kept, mirrors preserved)"); pass++;

            // generated file feeds the plan: svc owned by team-z (external) => wave condition, not executable
            var fixtureO = Path.Combine(Path.GetTempPath(), "ua-own-fx-" + Guid.NewGuid().ToString("N")[..8]);
            var rcO = Ingest.Run(new[] { Path.Combine(scansO, "svc") }, fixtureO,
                Path.Combine(richO, "sidecars", "producer-evidence.v0.json"), upd, Path.Combine(richO, "sidecars", "delta.json"));
            if (rcO != 0) throw new Exception($"ingest with generated ownership exited {rcO}");
            var planO = LoadEngine(fixtureO).BuildPlan();
            var svcEntry = planO.Repos.First(r => r.Repo == "svc");
            if (svcEntry.Ownership != "external" || svcEntry.ActionType != "external-request-await") throw new Exception($"generated ownership did not flow into the plan ({svcEntry.Ownership}/{svcEntry.ActionType})");
            Console.WriteLine("ok   ownership-feeds-plan (generated file plans correctly)"); pass++;
            File.Delete(oi); File.Delete(ou); File.Delete(existing); File.Delete(upd); Directory.Delete(sub, true); Directory.Delete(fixtureO, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL ownership-battery: {ex.Message}"); }

        // B1(b) E2E: scan-discovered producer merges into producer-evidence and drives the plan
        try
        {
            var richP = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var fixtureP = Path.Combine(Path.GetTempPath(), "ua-b1b-" + Guid.NewGuid().ToString("N")[..8]);
            var ownP = Path.Combine(Path.GetTempPath(), "ua-b1b-own-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(ownP, "{\"schemaVersion\":\"ownership.v0\",\"selfTeamId\":\"team-a\",\"ownerships\":[{\"repo\":\"corelib\",\"team\":\"team-a\"},{\"repo\":\"svc\",\"team\":\"team-a\"}]}");
            var prodP = Path.Combine(Path.GetTempPath(), "ua-b1b-prod-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(prodP, "{\"schemaVersion\":\"producer-evidence.v0\",\"source\":\"fixture-declared\",\"externalPackages\":[{\"packageId\":\"Newtonsoft.Json\"}],\"producers\":[]}");
            var rcP = Ingest.Run(new[] { Path.Combine(richP, "scans", "corelib"), Path.Combine(richP, "scans", "svc") }, fixtureP, prodP, ownP, Path.Combine(richP, "sidecars-apply", "delta.json"));
            if (rcP != 0) throw new Exception($"ingest exited {rcP}");
            var pe = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureP, "input", "producer-evidence.v0.json"))).RootElement;
            var corelibProd = pe.GetProperty("producers").EnumerateArray()
                .FirstOrDefault(p2 => p2.GetProperty("repo").GetString() == "corelib" && p2.GetProperty("packageId").GetString() == "Contoso.Core");
            if (corelibProd.ValueKind == JsonValueKind.Undefined) throw new Exception("scan-discovered producer missing from merged producer-evidence");
            if (corelibProd.GetProperty("producedVersion").GetString() != "1.0.0") throw new Exception("producedVersion wrong");
            if (corelibProd.TryGetProperty("publicationStatus", out _)) throw new Exception("publicationStatus invented from a declaration");
            var planP = LoadEngine(fixtureP).BuildPlan();
            if (planP.Waves.Count != 2 || planP.Waves[0].ReleaseUnits[0].Repo != "corelib" || planP.Waves[1].ReleaseUnits[0].Repo != "svc")
                throw new Exception($"expected producer wave 1 (corelib) -> consumer wave 2 (svc); got {string.Join(",", planP.Waves.Select(w => w.Index + ":" + string.Join("/", w.ReleaseUnits.Select(u2 => u2.Repo))))}");
            Console.WriteLine("ok   b1b-producer-from-scan (PackageProduced -> producer-evidence -> producer wave, no hand-written sidecar)"); pass++;
            Directory.Delete(fixtureP, true); File.Delete(ownP); File.Delete(prodP);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL b1b-producer-from-scan: {ex.Message}"); }

        // unknown-repo visibility (2026-10-08 real-estate finding): repos with gaps and no observed edge
        // classify UNKNOWN and must still appear in repos[] and the summary — silence is not safety
        try
        {
            var richU = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var fixtureU = Path.Combine(Path.GetTempPath(), "ua-unk-" + Guid.NewGuid().ToString("N")[..8]);
            var ownU = Path.Combine(Path.GetTempPath(), "ua-unk-own-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(ownU, "{\"schemaVersion\":\"ownership.v0\",\"selfTeamId\":\"team-a\",\"ownerships\":[{\"repo\":\"svc\",\"team\":\"team-a\"},{\"repo\":\"legacy\",\"team\":\"team-a\"}]}");
            var prodU = Path.Combine(Path.GetTempPath(), "ua-unk-prod-" + Guid.NewGuid().ToString("N")[..8]);
            File.WriteAllText(prodU, "{\"schemaVersion\":\"producer-evidence.v0\",\"source\":\"fixture-declared\",\"externalPackages\":[],\"producers\":[]}");
            if (Ingest.Run(new[] { Path.Combine(richU, "scans", "svc"), Path.Combine(richU, "scans", "legacy") }, fixtureU, prodU, ownU, Path.Combine(richU, "sidecars-apply", "delta.json")) != 0) throw new Exception("svc/legacy ingest failed");
            var planU = LoadEngine(fixtureU).BuildPlan();
            var unk = planU.Repos.FirstOrDefault(r => r.Repo == "legacy");
            if (unk is null) throw new Exception("legacy (gaps, no edge) is missing from repos[] — unknown repos must be visible");
            if (unk.Classification != "unknown") throw new Exception($"legacy classification expected unknown, got {unk.Classification}");
            if (planU.Waves.SelectMany(w => w.ReleaseUnits).Any(u2 => u2.Repo == "legacy")) throw new Exception("unknown repos are never scheduled");
            var reportU = Report.Render(planU);
            if (!reportU.Contains("1 unknown")) throw new Exception("report summary must count the unknown repo");
            Console.WriteLine("ok   unknown-repo-visibility (gaps + no edge => repos[] and summary, never silence)"); pass++;
            Directory.Delete(fixtureU, true); File.Delete(ownU); File.Delete(prodU);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL unknown-repo-visibility: {ex.Message}"); }

        // rev3 corollary (Codex P2 on PR #39): a CYCLE-STOP plan must not hide unknown repos either
        try
        {
            var fcyc = Path.Combine(fixturesRoot, "F-cyc-cycle-refusal");
            var sCyc = Path.Combine(Path.GetTempPath(), "ua-cycunk-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(sCyc, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(fcyc, "input"))) File.Copy(f, Path.Combine(sCyc, "input", Path.GetFileName(f)));
            var ow = Path.Combine(sCyc, "input", "ownership.v0.json");
            var own = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ow))!;
            own["ownerships"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(@"{ ""repo"": ""R-unk"", ""team"": ""team-a"" }")!);
            File.WriteAllText(ow, own.ToJsonString());
            var pePath = Path.Combine(sCyc, "input", "package-evidence.v0.json");
            var pe = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(pePath))!;
            if (pe["scanCoverage"] is null) pe["scanCoverage"] = new System.Text.Json.Nodes.JsonArray();
            pe["scanCoverage"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse(@"{ ""repo"": ""R-unk"", ""status"": ""gaps"", ""gaps"": [""synthetic gap""], ""notes"": [""synthetic scan note""] }")!);
            File.WriteAllText(pePath, pe.ToJsonString());
            var planC = LoadEngine(sCyc).BuildPlan();
            if (planC.Stop is null || planC.Stop.Reason != "CYCLE_DETECTED") throw new Exception("expected cycle stop");
            var unkC = planC.Repos.FirstOrDefault(r => r.Repo == "R-unk");
            if (unkC is null || unkC.Classification != "unknown") throw new Exception("cycle-stop plan must still show the unknown repo (rev3 corollary)");
            if (unkC.ScanNotes is not { Count: > 0 } || !unkC.ScanNotes.Contains("synthetic scan note")) throw new Exception("cycle-stop unknown entries must carry scanNotes (SPEC-015 parity with BuildRepo)");
            var repC = Report.Render(planC);
            if (!repC.Contains("1 unknown")) throw new Exception("cycle-stop report summary must count the unknown repo");
            Console.WriteLine("ok   unknown-repo-visibility-cycle (stop plans tell the whole truth too)"); pass++;
            Directory.Delete(sCyc, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL unknown-repo-visibility-cycle: {ex.Message}"); }

        // SPEC-015: package-relevant gaps vs compile-health notes — classifier + committed-scan outcomes
        try
        {
            var richG = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            string CoverageOf(string outDir)
            {
                var cov = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "input", "package-evidence.v0.json"))).RootElement.GetProperty("scanCoverage");
                return string.Join("|", cov.EnumerateArray().Select(c => $"{c.GetProperty("repo")}:{c.GetProperty("status")}:gaps={c.GetProperty("gaps").GetArrayLength()}:notes={(c.TryGetProperty("notes", out var n) ? n.GetArrayLength() : 0)}"));
            }
            // committed billing+shipping: compile noise -> notes; complete (acceptance 1a/3)
            var bs = Path.Combine(Path.GetTempPath(), "ua-g15a-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(new[] { Path.Combine(richG, "scans", "billing"), Path.Combine(richG, "scans", "shipping") }, bs,
                Path.Combine(richG, "sidecars-estate2", "producer-evidence.v0.json"), Path.Combine(richG, "sidecars-estate2", "ownership.v0.json"), Path.Combine(richG, "sidecars-estate2", "delta.json")) != 0) throw new Exception("billing/shipping ingest failed");
            var covBS = CoverageOf(bs);
            if (!covBS.Contains("billing:complete:gaps=0") || !covBS.Contains("shipping:complete:gaps=0")) throw new Exception($"billing/shipping must be complete with zero gaps: {covBS}");
            if (!covBS.Contains("notes=3")) throw new Exception($"compile-health notes expected (compiler/buildStatus/analysisLevel): {covBS}");
            Console.WriteLine("ok   gap-scope-complete (billing/shipping complete; compile noise as notes)"); pass++;
            Directory.Delete(bs, true);

            // committed svc: package-relevant gaps REMAIN (lockfile group + constraints); compile noise noted
            var sv = Path.Combine(Path.GetTempPath(), "ua-g15b-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(new[] { Path.Combine(richG, "scans", "svc") }, sv,
                Path.Combine(richG, "sidecars", "producer-evidence.v0.json"), Path.Combine(richG, "sidecars", "ownership.v0.json"), Path.Combine(richG, "sidecars", "delta.json")) != 0) throw new Exception("svc ingest failed");
            var covSv = JsonDocument.Parse(File.ReadAllText(Path.Combine(sv, "input", "package-evidence.v0.json"))).RootElement.GetProperty("scanCoverage")[0];
            if (covSv.GetProperty("status").GetString() != "gaps") throw new Exception("svc must stay gaps (real package problems)");
            var gapTexts = string.Join("\n", covSv.GetProperty("gaps").EnumerateArray().Select(g2 => g2.GetString()));
            if (!gapTexts.Contains("packages-lock-group-unsupported") || !gapTexts.Contains("constraint not evidenced")) throw new Exception("package-relevant gaps missing");
            if (gapTexts.Contains("CompilerDiagnostic")) throw new Exception("compile noise leaked into gaps");
            var noteTexts = string.Join("\n", covSv.GetProperty("notes").EnumerateArray().Select(g2 => g2.GetString()));
            if (!noteTexts.Contains("Compiler diagnostic") || !noteTexts.Contains("buildStatus")) throw new Exception("compile health not in notes");
            Console.WriteLine("ok   gap-scope-package-relevant (svc keeps real gaps; compile health noted)"); pass++;
            Directory.Delete(sv, true);

            // classifier rows: MSBuildRegistrationFailed (diagnosticKind join) + csharp.syntax (broken .cs) => notes; unknown shape stays gap
            var syn = Path.Combine(Path.GetTempPath(), "ua-g15c-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(syn);
            string Fact(string kindBlock, string rule, string msg) =>
                $"{{\"factId\":\"f-{rule}-{msg.GetHashCode():x}\",\"scanId\":\"s\",\"repo\":\"synth\",\"commitSha\":\"c\",\"factType\":\"AnalysisGap\",\"ruleId\":\"{rule}\",\"evidenceTier\":\"Tier4Unknown\",\"evidence\":{{\"filePath\":\".\",\"startLine\":1,\"endLine\":1}},\"properties\":{{{kindBlock}\"message\":\"{msg}\"}}}}";
            File.WriteAllText(Path.Combine(syn, "facts.ndjson"), string.Join("\n", new[]
            {
                Fact("\"diagnosticKind\":\"workspace\",\"gapKind\":\"MSBuildRegistrationFailed\",", "csharp.semantic.workspace.v1", "msbuild failed to register"),
                Fact("", "csharp.syntax.declarations.v1", "broken cs file"),
                Fact("", "some.future.rule.v1", "an unknown shape"),
            }) + "\n");
            File.Copy(Path.Combine(richG, "sidecars-estate2", "delta.json"), Path.Combine(syn, "delta.json"));
            var synOut = Path.Combine(Path.GetTempPath(), "ua-g15d-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(new[] { syn }, synOut, null, null, null) != 0) throw new Exception("synthetic ingest failed");
            var covSyn = JsonDocument.Parse(File.ReadAllText(Path.Combine(synOut, "input", "package-evidence.v0.json"))).RootElement.GetProperty("scanCoverage")[0];
            var synGaps = string.Join("\n", covSyn.GetProperty("gaps").EnumerateArray().Select(g2 => g2.GetString()));
            var synNotes = string.Join("\n", covSyn.GetProperty("notes").EnumerateArray().Select(g2 => g2.GetString()));
            if (!synNotes.Contains("msbuild failed") || !synNotes.Contains("broken cs file")) throw new Exception("workspace-kind and syntax facts must be notes");
            if (!synGaps.Contains("an unknown shape")) throw new Exception("unknown shape must default to Gap");
            Console.WriteLine("ok   gap-scope-classifier (workspace-kind, syntax => notes; unknown => gap)"); pass++;
            Directory.Delete(syn, true); Directory.Delete(synOut, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL gap-scope: {ex.Message}"); }

        // SPEC-015 round-1: combined snapshots keep every manifest's problems; cycle plans carry notes
        try
        {
            var richR = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            // two snapshots of svc: second manifest carries its own knownGaps entry (opaque => Gap)
            var snap1 = Path.Combine(Path.GetTempPath(), "ua-g15-s1-" + Guid.NewGuid().ToString("N")[..8]);
            var snap2 = Path.Combine(Path.GetTempPath(), "ua-g15-s2-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(Path.Combine(richR, "scans", "svc"), snap1);
            CopyDir(Path.Combine(richR, "scans", "svc"), snap2);
            var m2 = Path.Combine(snap2, "scan-manifest.json");
            var manifest2 = JsonDocument.Parse(File.ReadAllText(m2)).RootElement.Clone();
            // append an opaque knownGap ONLY to the second snapshot's manifest
            File.WriteAllText(m2, File.ReadAllText(m2).TrimEnd().TrimEnd('}') + ",\"knownGaps\":[\"opaque scanner declaration from snapshot 2\"]}");
            var outR = Path.Combine(Path.GetTempPath(), "ua-g15-out-" + Guid.NewGuid().ToString("N")[..8]);
            var rcR = Ingest.Run(new[] { snap1, snap2 }, outR, Path.Combine(richR, "sidecars", "producer-evidence.v0.json"), Path.Combine(richR, "sidecars", "ownership.v0.json"), Path.Combine(richR, "sidecars", "delta.json"));
            if (rcR != 0) throw new Exception($"combined-snapshot ingest exited {rcR}");
            var covR = JsonDocument.Parse(File.ReadAllText(Path.Combine(outR, "input", "package-evidence.v0.json"))).RootElement.GetProperty("scanCoverage")[0];
            var gapTextsR = string.Join("\n", covR.GetProperty("gaps").EnumerateArray().Select(g2 => g2.GetString()));
            if (!gapTextsR.Contains("opaque scanner declaration from snapshot 2")) throw new Exception("second manifest's knownGaps entry vanished");
            Console.WriteLine("ok   gap-scope-combined-snapshots (every manifest classified)"); pass++;
            Directory.Delete(snap1, true); Directory.Delete(snap2, true); Directory.Delete(outR, true);

            // cycle-stop plan carries scanNotes (BuildRepo parity in CyclePlan)
            var cyc = Path.Combine(Path.GetTempPath(), "ua-g15-cyc-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(cyc, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(fixturesRoot, "F-cyc-cycle-refusal", "input"))) File.Copy(f, Path.Combine(cyc, "input", Path.GetFileName(f)));
            var cycCov = Path.Combine(cyc, "input", "package-evidence.v0.json");
            var cycNode = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(cycCov))!;
            foreach (var entry in cycNode!["scanCoverage"]!.AsArray())
                if (entry!["repo"]!.GetValue<string>() == "R1")
                { entry["notes"] = new System.Text.Json.Nodes.JsonArray("scan buildStatus: FailedOrPartial"); break; }
            File.WriteAllText(cycCov, cycNode.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            var planCyc = LoadEngine(cyc).BuildPlan();
            var r1 = planCyc.Repos.First(r => r.Repo == "R1");
            if (r1.ScanNotes is not { Count: 1 } || r1.ScanNotes![0] != "scan buildStatus: FailedOrPartial") throw new Exception("cycle plan lost coverage notes");
            Console.WriteLine("ok   gap-scope-cycle-notes (CyclePlan carries scanNotes)"); pass++;
            Directory.Delete(cyc, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL gap-scope-round1: {ex.Message}"); }

        // SPEC-016: --sanitized — adversarial redactor cases + e2e through Main
        try
        {
            Redact.Enable("Contoso"); // UA_REDACT for the adversarial set; disabled again after
            var samples = new (string In, Func<string, bool> Ok)[]
            {
                (@"C:\Users\jdoe\work\svc\Directory.Packages.props", o => o.Contains("[path:Directory.Packages.props#") && !o.Contains("jdoe")),
                (@"\\fileserver\drop\scan", o => o.Contains("[path:scan#") && !o.Contains("fileserver")),
                ("/home/jdoe/estate/scans", o => o.Contains("[path:scans#") && !o.Contains("jdoe")),
                ("https://git.internal.corp/org/repo/pulls", o => o.Contains("[url#") && !o.Contains("internal.corp")),
                ("https://user:token@github.com/org/repo", o => o.Contains("[url#") && !o.Contains("token")), // allowlisted host, credential parts -> redacted
                ("https://github.com/joefeser/upgrade-authority", o => o == "https://github.com/joefeser/upgrade-authority"), // clean allowlisted URL passes
                ("git@git.internal.corp:org/repo.git", o => o.Contains("[url#") && !o.Contains("internal.corp")),
                ("jdoe@company.internal", o => o.Contains("[email#") && !o.Contains("jdoe") && !o.Contains("company.internal")), // email whole, not partial
                ("Resolving Contoso.Core for ContosoCorp", o => !o.Contains("Contoso") && o.Contains("[tok#")), // UA_REDACT substring, case-insensitive
                ("src/Api/packages.lock.json", o => o == "src/Api/packages.lock.json"), // repo-relative passes
                ("ua/wave-1/Some.Package-1.0.0-to-2.0.0", o => !o.Contains('[')), // branch names untouched (no UA_REDACT hit)
                ("deadbeefdeadbeefdeadbeefdeadbeefdeadbeef", o => !o.Contains('[')), // hex passes
                ("origin host 'ghe' differs", o => o.Contains("[host#") || o.Contains("'ghe'") == false), // single-label host in quoted anchor redacts
                ("base host '10.0.0.5' set", o => !o.Contains("10.0.0.5")),
                // PR37 round-1 regressions
                (@"/tmp/scan jdoe/no-such-scan", o => o.Contains("[path:") && !o.Contains("jdoe")), // Q1: path with a SPACE
                (@"/tmp/scan jdoe/dir with spaces/facts.ndjson", o => o.Contains("[path:") && !o.Contains("jdoe") && !o.Contains("with spaces")),
                ("HTTPS://git.internal.corp/org/repo", o => o.Contains("[url#") && !o.Contains("internal")), // Q3: uppercase scheme
                ("unassigned: git.corp.example/org/repo", o => !o.Contains("corp.example")), // Q2: unquoted repo key
                ("conflicting facts in git.corp.example/org/repo", o => !o.Contains("corp.example")),
                ("origin host 'git.internal.corp:8443' differs", o => !o.Contains("8443") && !o.Contains("internal")), // Q5: dotted host + port in the real message shape
                ("API base host '10.0.0.5:8443' set", o => !o.Contains("10.0.0.5")),
            };
            foreach (var (input, ok) in samples)
            {
                var once = Redact.Apply(input);
                var twice = Redact.Apply(input);
                if (once != twice) throw new Exception($"hash instability for: {input[..Math.Min(30, input.Length)]}");
                if (!ok(once)) throw new Exception($"redaction failed for: {input[..Math.Min(40, input.Length)]} => {once}");
            }
            Redact.Enabled = false;
            Console.WriteLine("ok   sanitized-redactor (adversarial cases + hash stability)"); pass++;

            // e2e: failing ingest with a fake-username path -> stderr carries [path: marker, never the username
            var savedErr = Console.Error; var savedRedact = Redact.Enabled;
            var errCapture = new System.IO.StringWriter();
            Console.SetError(errCapture);
            Redact.Enable(null);
            var rcS = Main(new[] { "ingest", "/tmp/ua-fakeuser-jdoe/no-such-scan", "--out", "/tmp/ua-x9", "--sanitized" });
            Console.SetError(savedErr); Redact.Enabled = savedRedact;
            var cap = errCapture.ToString();
            if (rcS != 1) throw new Exception($"expected exit 1, got {rcS}");
            if (cap.Contains("jdoe")) throw new Exception("username leaked in sanitized e2e");
            if (!cap.Contains("[path:")) throw new Exception("path marker missing in sanitized e2e");
            // e2e: UA_REDACT substring never survives
            Console.SetError(errCapture = new System.IO.StringWriter());
            Redact.Enable("fakeuser");
            Main(new[] { "ingest", "/tmp/ua-fakeuser-jdoe/no-such-scan", "--out", "/tmp/ua-x9", "--sanitized" });
            Console.SetError(savedErr); Redact.Enabled = false;
            if (errCapture.ToString().Contains("fakeuser", StringComparison.OrdinalIgnoreCase)) throw new Exception("UA_REDACT token leaked in e2e");
            Console.WriteLine("ok   sanitized-e2e (Main-level: username + UA_REDACT never leak)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL sanitized: {ex.Message}"); }
        finally { Redact.Enabled = false; }

        // stdout encoding contract: report/plan must arrive as UTF-8 bytes even when stdout is a pipe
        // (Windows defaults redirected console output to the OEM codepage — a real work-machine
        // report.md came out garbled; Main forces UTF-8, this case pins it through a real child pipe)
        try
        {
            var exe = Environment.ProcessPath ?? throw new Exception("ProcessPath unavailable");
            var f9 = Path.Combine(fixturesRoot, "F9-multi-lockfile-disagreement");
            var psi = new System.Diagnostics.ProcessStartInfo(exe) { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add("report"); psi.ArgumentList.Add(f9);
            psi.StandardOutputEncoding = System.Text.Encoding.Latin1; // byte-transparent: what the pipe carried, as chars
            using var p = System.Diagnostics.Process.Start(psi)!;
            var raw = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30000);
            if (p.ExitCode != 0) throw new Exception($"child report exited {p.ExitCode}");
            var arrow = "\u00E2\u0086\u0092"; // UTF-8 bytes e2 86 92 seen through Latin1 glasses
            if (!raw.Contains("# Impact plan:") || !raw.Contains(arrow)) throw new Exception("piped stdout is not UTF-8 (the → did not arrive as bytes e2 86 92)");
            Console.WriteLine("ok   stdout-utf8-redirected (→ survives a pipe as e2 86 92)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL stdout-utf8-redirected: {ex.Message}"); }

        // SPEC-017: ua scan-estate + estate scope + --out — offline battery (stub scanner seam, local bare origins).
        // Layout per case: <root>/repos/<name> checkouts, <root>/origins/<name>.git bare remotes — origins
        // live OUTSIDE the repos root so the wrapper never sees them as candidates.
        string EstateRepo(string root, string name)
        {
            var dir = Path.Combine(root, "repos", name);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "README.md"), "# " + name + "\n");
            Push.Git(dir, "init -q .", out _, out _);
            Push.Git(dir, "config user.email ua@test", out _, out _);
            Push.Git(dir, "config user.name ua-selftest", out _, out _);
            Push.Git(dir, "add -A", out _, out _);
            Push.Git(dir, "-c user.email=ua@test -c user.name=ua commit -qm base", out _, out _);
            Push.Git(dir, "branch -m main", out _, out _);
            Directory.CreateDirectory(Path.Combine(root, "origins"));
            var bare = Path.Combine(root, "origins", name + ".git");
            Push.Git(root, $"clone -q --bare \"{dir}\" \"{bare}\"", out _, out _);
            Push.Git(dir, $"remote add origin \"{bare}\"", out _, out _);
            Push.Git(dir, "push -q -u origin main", out _, out _);
            return dir;
        }
        int StubScanner(ScanEstate.ScanRequest r)
        {
            Directory.CreateDirectory(r.ScanOutDir);
            var n = string.IsNullOrEmpty(r.RepoName) ? Path.GetFileName(r.RepoPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : r.RepoName;
            File.WriteAllText(Path.Combine(r.ScanOutDir, "facts.ndjson"),
                $"{{\"factId\":\"fact-stub-{n}\",\"scanId\":\"scan-stub\",\"repo\":\"{n}\",\"commitSha\":\"{r.HeadSha}\",\"factType\":\"PackageReferenced\",\"ruleId\":\"project.file.v1\",\"evidenceTier\":\"Tier2Structural\",\"evidence\":{{\"filePath\":\"src/App.csproj\",\"startLine\":4,\"endLine\":4}},\"properties\":{{\"ecosystem\":\"nuget\",\"manifestKind\":\"packagereference\",\"packageName\":\"Newtonsoft.Json\",\"version\":\"12.0.3\"}}}}\n");
            File.WriteAllText(Path.Combine(r.ScanOutDir, "scan-manifest.json"),
                $"{{\"scanId\":\"scan-stub\",\"repoName\":\"{n}\",\"remoteUrl\":null,\"branch\":\"main\",\"commitSha\":\"{r.HeadSha}\",\"scannerVersion\":\"stub\",\"buildStatus\":\"Succeeded\",\"knownGaps\":[]}}\n");
            return 0;
        }
        JsonDocument SEManifest(string outDir) => JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "scan-estate.v1.json")));

        // 1. happy path: bootstrap + chain + byte equivalence with the manual runbook §4 sequence
        var seRoot1 = Path.Combine(Path.GetTempPath(), "ua-se1-" + Guid.NewGuid().ToString("N")[..8]);
        var seOut1 = Path.Combine(Path.GetTempPath(), "ua-se1-out-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(seRoot1);
            EstateRepo(seRoot1, "alpha");
            EstateRepo(seRoot1, "beta");
            var rc1 = ScanEstate.Run(Path.Combine(seRoot1, "repos"), seOut1, null, null, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", StubScanner);
            if (rc1 != 0) throw new Exception($"scan-estate exited {rc1}");
            foreach (var n in new[] { "alpha", "beta" })
                foreach (var f in new[] { "facts.ndjson", "scan-manifest.json", "scan-estate.json" })
                    if (!File.Exists(Path.Combine(seOut1, "scans", n, f))) throw new Exception($"missing scans/{n}/{f}");
            var delta1 = File.ReadAllText(Path.Combine(seOut1, "delta.json"));
            if (!delta1.Contains("\"packageName\": \"Newtonsoft.Json\"") || !delta1.Contains("\"oldVersion\": \"12.0.3\"") || !delta1.Contains("\"newVersion\": \"13.0.3\""))
                throw new Exception("delta template wrong");
            var prod1 = File.ReadAllText(Path.Combine(seOut1, "producer-evidence.v0.json"));
            if (!prod1.Contains("Newtonsoft.Json")) throw new Exception("producer template missing the delta target");
            var own1 = File.ReadAllText(Path.Combine(seOut1, "ownership.v0.json"));
            if (!own1.Contains("\"repo\": \"alpha\"") || !own1.Contains("\"repo\": \"beta\"") || !own1.Contains("\"team\": \"team-a\"")) throw new Exception("ownership init wrong");
            // equivalence: manual sequence over the same scans, same sidecars (SPEC-017 §7.1)
            var manual1 = Path.Combine(Path.GetTempPath(), "ua-se1-man-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(new[] { Path.Combine(seOut1, "scans", "alpha"), Path.Combine(seOut1, "scans", "beta") }, manual1,
                Path.Combine(seOut1, "producer-evidence.v0.json"), Path.Combine(seOut1, "ownership.v0.json"), Path.Combine(seOut1, "delta.json")) != 0)
                throw new Exception("manual ingest failed");
            (string, string)[] FilesOf(string d) => Directory.GetFiles(Path.Combine(d, "input")).OrderBy(f => f, StringComparer.Ordinal).Select(f => (Path.GetFileName(f), File.ReadAllText(f))).ToArray();
            if (!FilesOf(seOut1 + "/fixture").SequenceEqual(FilesOf(manual1))) throw new Exception("wrapper fixture differs from the manual sequence");
            var planText1 = File.ReadAllText(Path.Combine(seOut1, "plan.json"));
            var reportText1 = File.ReadAllText(Path.Combine(seOut1, "report.md"));
            if (planText1 != Canonical.Write(LoadEngine(Path.Combine(seOut1, "fixture")).BuildPlan())) throw new Exception("plan.json not canonical bytes");
            if (reportText1 != Report.Render(LoadEngine(Path.Combine(seOut1, "fixture")).BuildPlan())) throw new Exception("report.md not canonical bytes");
            if (!reportText1.Contains("- Repositories: 2 affected")) throw new Exception("expected 2 affected stub repos");
            using (var m1 = SEManifest(seOut1))
                if (m1.RootElement.GetProperty("repos").EnumerateArray().Any(e => e.GetProperty("status").GetString() != "scanned"))
                    throw new Exception("manifest: both repos must be scanned on first run");
            // argv composition (seam contract): scan --repo <abs> --out <dir> + excludes in order
            var argv = ScanEstate.ComposeScanArgs(new ScanEstate.ScanRequest { RepoPath = "/r", ScanOutDir = "/o", HeadSha = "h", Excludes = new[] { "a/**", "b/**" } });
            if (!argv.SequenceEqual(new[] { "scan", "--repo", "/r", "--out", "/o", "--exclude", "a/**", "--exclude", "b/**" })) throw new Exception("scan argv shape wrong");
            Console.WriteLine("ok   scan-estate-happy-path (bootstrap + chain + manual-sequence equivalence)"); pass++;
            Directory.Delete(manual1, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-happy-path: {ex.Message}"); }
        finally { ForceDelete(seRoot1); if (Directory.Exists(seOut1)) ForceDelete(seOut1); }

        // 2. incremental: zero rescans when nothing changed; exactly the bumped repo rescans
        try
        {
            var root2 = Path.Combine(Path.GetTempPath(), "ua-se2-" + Guid.NewGuid().ToString("N")[..8]);
            var out2 = Path.Combine(Path.GetTempPath(), "ua-se2-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root2);
            var beta2 = EstateRepo(root2, "beta");
            EstateRepo(root2, "alpha");
            if (ScanEstate.Run(Path.Combine(root2, "repos"), out2, null, null, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", StubScanner) != 0) throw new Exception("first run failed");
            var planBefore = File.ReadAllText(Path.Combine(out2, "plan.json"));
            var calls = 0;
            int Count(ScanEstate.ScanRequest r) { System.Threading.Interlocked.Increment(ref calls); return StubScanner(r); }
            if (ScanEstate.Run(Path.Combine(root2, "repos"), out2, null, null, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", Count) != 0) throw new Exception("re-run failed");
            if (calls != 0) throw new Exception($"no-change re-run performed {calls} scans");
            if (File.ReadAllText(Path.Combine(out2, "plan.json")) != planBefore) throw new Exception("plan bytes changed on a no-op re-run");
            using (var m2 = SEManifest(out2))
                if (!m2.RootElement.GetProperty("repos").EnumerateArray().All(e => e.GetProperty("status").GetString() == "reused"))
                    throw new Exception("manifest: no-op re-run must be all reused");
            // bump beta to a new trunk-tip commit -> exactly beta rescans
            File.WriteAllText(Path.Combine(beta2, "bump.txt"), "x");
            Push.Git(beta2, "add -A", out _, out _);
            Push.Git(beta2, "-c user.email=ua@test -c user.name=ua commit -qm bump", out _, out _);
            Push.Git(beta2, "push -q origin main", out _, out _);
            calls = 0;
            if (ScanEstate.Run(Path.Combine(root2, "repos"), out2, null, null, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", Count) != 0) throw new Exception("post-bump run failed");
            if (calls != 1) throw new Exception($"bump run performed {calls} scans, expected exactly 1");
            using (var m2b = SEManifest(out2))
            {
                var byName = m2b.RootElement.GetProperty("repos").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString(), e => e.GetProperty("status").GetString());
                if (byName["beta"] != "scanned" || byName["alpha"] != "reused") throw new Exception($"statuses: {string.Join(",", byName.Select(kv => kv.Key + "=" + kv.Value))}");
            }
            Console.WriteLine("ok   scan-estate-incremental (0 rescans; bumped repo alone rescans)"); pass++;
            ForceDelete(root2); ForceDelete(out2);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-incremental: {ex.Message}"); }

        // 3. freshness: behind repo skipped with reason; --allow-stale scans it
        try
        {
            var root3 = Path.Combine(Path.GetTempPath(), "ua-se3-" + Guid.NewGuid().ToString("N")[..8]);
            var out3 = Path.Combine(Path.GetTempPath(), "ua-se3-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root3);
            var gamma = EstateRepo(root3, "gamma");
            // push ahead, then move the checkout back -> behind without dirtying
            File.WriteAllText(Path.Combine(gamma, "ahead.txt"), "x");
            Push.Git(gamma, "add -A", out _, out _);
            Push.Git(gamma, "-c user.email=ua@test -c user.name=ua commit -qm ahead", out _, out _);
            Push.Git(gamma, "push -q origin main", out _, out _);
            Push.Git(gamma, "reset -q --hard HEAD~1", out _, out _);
            if (ScanEstate.Run(Path.Combine(root3, "repos"), out3, null, null, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", StubScanner) != 1) throw new Exception("all-skipped single-repo run must exit 1 (0 fresh)");
            using (var m3 = SEManifest(out3))
            {
                var e3 = m3.RootElement.GetProperty("repos")[0];
                if (e3.GetProperty("status").GetString() != "skipped" || !e3.GetProperty("reason").GetString()!.Contains("is not at origin/main tip"))
                    throw new Exception($"stale skip wrong: {e3.GetRawText()}");
            }
            if (File.Exists(Path.Combine(out3, "scans", "gamma", "facts.ndjson"))) throw new Exception("stale repo must not be scanned");
            if (ScanEstate.Run(Path.Combine(root3, "repos"), out3, null, null, "team-a", true, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", StubScanner) != 0) throw new Exception("allow-stale run failed");
            if (!File.Exists(Path.Combine(out3, "scans", "gamma", "facts.ndjson"))) throw new Exception("--allow-stale must scan the behind repo");
            Console.WriteLine("ok   scan-estate-stale-skip (behind skipped w/ reason; --allow-stale scans)"); pass++;
            ForceDelete(root3); ForceDelete(out3);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-stale-skip: {ex.Message}"); }

        // 4. dirty tree: skipped; --allow-stale scans (marked stale); clean re-run RESCANS (never silent reuse)
        try
        {
            var root4 = Path.Combine(Path.GetTempPath(), "ua-se4-" + Guid.NewGuid().ToString("N")[..8]);
            var out4 = Path.Combine(Path.GetTempPath(), "ua-se4-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root4);
            EstateRepo(root4, "dirty");
            File.WriteAllText(Path.Combine(root4, "repos", "dirty", "junk.txt"), "untracked");
            if (ScanEstate.Run(Path.Combine(root4, "repos"), out4, null, null, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", StubScanner) != 1) throw new Exception("dirty single-repo run must exit 1 (0 fresh)");
            using (var m4 = SEManifest(out4))
                if (m4.RootElement.GetProperty("repos")[0].GetProperty("reason").GetString() != "working tree not clean (uncommitted or untracked changes; --allow-stale to scan anyway)")
                    throw new Exception("dirty reason wrong");
            if (ScanEstate.Run(Path.Combine(root4, "repos"), out4, null, null, "team-a", true, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", StubScanner) != 0) throw new Exception("allow-stale run failed");
            File.Delete(Path.Combine(root4, "repos", "dirty", "junk.txt"));
            var calls4 = 0;
            int Count4(ScanEstate.ScanRequest r) { System.Threading.Interlocked.Increment(ref calls4); return StubScanner(r); }
            if (ScanEstate.Run(Path.Combine(root4, "repos"), out4, null, null, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", Count4) != 0) throw new Exception("clean re-run failed");
            if (calls4 != 1) throw new Exception($"clean re-run at same SHA performed {calls4} scans — a staleScan-marked cache was silently reused");
            Console.WriteLine("ok   scan-estate-dirty-tree (skip; allow-stale scans; clean re-run rescans)"); pass++;
            ForceDelete(root4); ForceDelete(out4);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-dirty-tree: {ex.Message}"); }

        // 5. cache key honesty: --exclude changes and tracemap dll changes force rescan
        try
        {
            var root5 = Path.Combine(Path.GetTempPath(), "ua-se5-" + Guid.NewGuid().ToString("N")[..8]);
            var out5 = Path.Combine(Path.GetTempPath(), "ua-se5-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root5);
            EstateRepo(root5, "eps");
            var calls5 = 0;
            int Count5(ScanEstate.ScanRequest r) { System.Threading.Interlocked.Increment(ref calls5); return StubScanner(r); }
            if (ScanEstate.Run(Path.Combine(root5, "repos"), out5, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count5) != 0) throw new Exception("run 1 failed");
            if (ScanEstate.Run(Path.Combine(root5, "repos"), out5, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count5) != 0) throw new Exception("run 2 failed");
            if (calls5 != 1) throw new Exception("identical run 2 must reuse");
            if (ScanEstate.Run(Path.Combine(root5, "repos"), out5, null, null, "team-a", false, new[] { "Migrations/**" }, "P", "1", "2", Count5) != 0) throw new Exception("run 3 failed");
            if (calls5 != 2) throw new Exception("changed --exclude must rescan");
            var dllA = Path.Combine(Path.GetTempPath(), "ua-se5-a.dll");
            var dllB = Path.Combine(Path.GetTempPath(), "ua-se5-b.dll");
            File.WriteAllText(dllA, "assembly-A"); File.WriteAllText(dllB, "assembly-B");
            if (ScanEstate.Run(Path.Combine(root5, "repos"), out5, dllA, null, "team-a", false, new[] { "Migrations/**" }, "P", "1", "2", Count5) != 0) throw new Exception("run 4 failed");
            if (calls5 != 3) throw new Exception("new --tracemap dll (hash) must rescan");
            if (ScanEstate.Run(Path.Combine(root5, "repos"), out5, dllA, null, "team-a", false, new[] { "Migrations/**" }, "P", "1", "2", Count5) != 0) throw new Exception("run 5 failed");
            if (calls5 != 3) throw new Exception("same dll + same excludes must reuse");
            if (ScanEstate.Run(Path.Combine(root5, "repos"), out5, dllB, null, "team-a", false, new[] { "Migrations/**" }, "P", "1", "2", Count5) != 0) throw new Exception("run 6 failed");
            if (calls5 != 4) throw new Exception("different-bytes dll must rescan");
            Console.WriteLine("ok   scan-estate-cachekey (exclude + tracemap-hash dimensions pinned)"); pass++;
            ForceDelete(root5); ForceDelete(out5); File.Delete(dllA); File.Delete(dllB);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-cachekey: {ex.Message}"); }

        // 6. cache honesty: facts-without-manifest warns+skips; scanner failure keeps the old cache, no residue
        try
        {
            var root6 = Path.Combine(Path.GetTempPath(), "ua-se6-" + Guid.NewGuid().ToString("N")[..8]);
            var out6 = Path.Combine(Path.GetTempPath(), "ua-se6-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root6);
            EstateRepo(root6, "zeta");
            Directory.CreateDirectory(Path.Combine(out6, "scans", "zeta"));
            File.WriteAllText(Path.Combine(out6, "scans", "zeta", "facts.ndjson"), "{}\n"); // facts without manifest
            if (ScanEstate.Run(Path.Combine(root6, "repos"), out6, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner) != 1) throw new Exception("facts-without-manifest single-repo run must exit 1 (0 fresh)");
            using (var m6 = SEManifest(out6))
                if (m6.RootElement.GetProperty("repos")[0].GetProperty("reason").GetString()!.Contains("no scan-manifest.json") != true)
                    throw new Exception("facts-without-manifest reason missing");
            // scanner failure: previous cache intact, no tmp/old residue, repo skipped, then recovers
            Directory.Delete(Path.Combine(out6, "scans", "zeta"), true);
            if (ScanEstate.Run(Path.Combine(root6, "repos"), out6, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner) != 0) throw new Exception("seed run failed");
            int Fail6(ScanEstate.ScanRequest r) { Directory.CreateDirectory(r.ScanOutDir); File.WriteAllText(Path.Combine(r.ScanOutDir, "facts.ndjson"), "partial\n"); return 9; }
            // bump so a rescan is needed
            var zeta6 = Path.Combine(root6, "repos", "zeta");
            File.WriteAllText(Path.Combine(zeta6, "b.txt"), "x");
            Push.Git(zeta6, "add -A", out _, out _); Push.Git(zeta6, "-c user.email=ua@test -c user.name=ua commit -qm b", out _, out _); Push.Git(zeta6, "push -q origin main", out _, out _);
            if (ScanEstate.Run(Path.Combine(root6, "repos"), out6, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Fail6) != 1) throw new Exception("single-repo estate with a failed scan must exit 1 (0 fresh) — skip visible in manifest");
            if (!File.Exists(Path.Combine(out6, "scans", "zeta", "scan-manifest.json"))) throw new Exception("failed scan must leave the previous cache intact");
            if (Directory.GetDirectories(Path.Combine(out6, "scans")).Any(d => Path.GetFileName(d).StartsWith('.'))) throw new Exception("tmp/old residue left behind");
            using (var m6b = SEManifest(out6))
                if (m6b.RootElement.GetProperty("repos")[0].GetProperty("reason").GetString() != "tracemap exited 9") throw new Exception("failure reason wrong");
            if (ScanEstate.Run(Path.Combine(root6, "repos"), out6, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner) != 0) throw new Exception("recovery run failed");
            using (var m6c = SEManifest(out6))
                if (m6c.RootElement.GetProperty("repos")[0].GetProperty("status").GetString() != "scanned") throw new Exception("recovery must rescan");
            Console.WriteLine("ok   scan-estate-cache-honesty (facts-w/o-manifest; failure keeps cache, no residue)"); pass++;
            ForceDelete(root6); ForceDelete(out6);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-cache-honesty: {ex.Message}"); }

        // 7. scope: excluded repo not scanned; report statement; include-mode manifest + report
        try
        {
            var root7 = Path.Combine(Path.GetTempPath(), "ua-se7-" + Guid.NewGuid().ToString("N")[..8]);
            var out7 = Path.Combine(Path.GetTempPath(), "ua-se7-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root7);
            EstateRepo(root7, "alpha");
            EstateRepo(root7, "beta");
            EstateRepo(root7, "gamma");
            var scopeEx = Path.Combine(Path.GetTempPath(), "ua-se7-scope-ex.json");
            File.WriteAllText(scopeEx, "{\"schemaVersion\":\"estate-scope.v0\",\"exclude\":[\"beta\"]}\n");
            var calls7 = 0;
            int Count7(ScanEstate.ScanRequest r) { System.Threading.Interlocked.Increment(ref calls7); return StubScanner(r); }
            if (ScanEstate.Run(Path.Combine(root7, "repos"), out7, null, scopeEx, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", Count7) != 0) throw new Exception("scoped run failed");
            if (calls7 != 2) throw new Exception($"excluded beta must not scan ({calls7} calls)");
            if (Directory.Exists(Path.Combine(out7, "scans", "beta"))) throw new Exception("beta scan dir must not exist");
            var rep7 = File.ReadAllText(Path.Combine(out7, "report.md"));
            if (!rep7.Contains("- Scope: 1 repos out of scope by config: beta")) throw new Exception("report scope statement missing");
            if (!File.Exists(Path.Combine(out7, "fixture", "input", "scope.v0.json"))) throw new Exception("scope file not copied into the fixture");
            // Baz r2: the fixture's scope bytes come from the SNAPSHOT of the one validated read —
            // one run can never mix two scope versions, and the snapshot stays as provenance
            var snapshot7 = Path.Combine(out7, "scope.snapshot.json");
            if (!File.Exists(snapshot7)) throw new Exception("scope.snapshot.json missing (the run's actual evidence)");
            if (File.ReadAllText(snapshot7) != File.ReadAllText(Path.Combine(out7, "fixture", "input", "scope.v0.json"))) throw new Exception("fixture scope bytes differ from the snapshot");
            using (var m7 = SEManifest(out7))
                if (m7.RootElement.GetProperty("repos").EnumerateArray().First(e => e.GetProperty("name").GetString() == "beta").GetProperty("status").GetString() != "out-of-scope")
                    throw new Exception("manifest: beta must be out-of-scope");
            // include-mode: only alpha listed; beta+gamma out-of-scope (named in the manifest, counted in the report)
            var out7b = Path.Combine(Path.GetTempPath(), "ua-se7-out2-" + Guid.NewGuid().ToString("N")[..8]);
            var scopeIn = Path.Combine(Path.GetTempPath(), "ua-se7-scope-in.json");
            File.WriteAllText(scopeIn, "{\"schemaVersion\":\"estate-scope.v0\",\"include\":[\"alpha\"]}\n");
            if (ScanEstate.Run(Path.Combine(root7, "repos"), out7b, null, scopeIn, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", StubScanner) != 0) throw new Exception("include-mode run failed");
            var rep7b = File.ReadAllText(Path.Combine(out7b, "report.md"));
            if (!rep7b.Contains("- Scope: include-mode — 1 repos in scope by config")) throw new Exception("include-mode report line missing");
            using (var m7b = SEManifest(out7b))
            {
                var st7 = m7b.RootElement.GetProperty("repos").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString(), e => e.GetProperty("status").GetString());
                if (st7["alpha"] != "scanned" || st7["beta"] != "out-of-scope" || st7["gamma"] != "out-of-scope") throw new Exception($"include-mode statuses: {string.Join(",", st7.Select(kv => kv.Key + "=" + kv.Value))}");
            }
            Console.WriteLine("ok   scan-estate-scope (exclude + include modes, statement + manifest)"); pass++;
            ForceDelete(root7); ForceDelete(out7); ForceDelete(out7b); File.Delete(scopeEx); File.Delete(scopeIn);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-scope: {ex.Message}"); }

        // 8. alternates: two checkouts of one origin -> the later is visibly skipped; no-origin always skipped
        try
        {
            var root8 = Path.Combine(Path.GetTempPath(), "ua-se8-" + Guid.NewGuid().ToString("N")[..8]);
            var out8 = Path.Combine(Path.GetTempPath(), "ua-se8-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root8);
            EstateRepo(root8, "alpha");
            // a second checkout of the SAME origin (clone of the bare)
            Push.Git(root8, $"clone -q \"{root8}/origins/alpha.git\" \"{root8}/repos/alpha-clone\"", out _, out _);
            // a repo with no remote at all
            var noOrigin = Path.Combine(root8, "repos", "theta");
            Directory.CreateDirectory(noOrigin);
            File.WriteAllText(Path.Combine(noOrigin, "README.md"), "x");
            Push.Git(noOrigin, "init -q .", out _, out _);
            Push.Git(noOrigin, "config user.email ua@test", out _, out _);
            Push.Git(noOrigin, "config user.name ua", out _, out _);
            Push.Git(noOrigin, "add -A", out _, out _);
            Push.Git(noOrigin, "-c user.email=ua@test -c user.name=ua commit -qm base", out _, out _);
            Push.Git(noOrigin, "branch -m main", out _, out _);
            if (ScanEstate.Run(Path.Combine(root8, "repos"), out8, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner) != 0) throw new Exception("run failed");
            using (var m8 = SEManifest(out8))
            {
                var st8 = m8.RootElement.GetProperty("repos").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString(), e => (status: e.GetProperty("status").GetString(), reason: e.TryGetProperty("reason", out var rr) ? rr.GetString() : null));
                if (st8["alpha-clone"].status != "skipped" || !st8["alpha-clone"].reason!.Contains("alternate checkout of alpha")) throw new Exception($"alternate skip wrong: {st8["alpha-clone"]}");
                if (st8["theta"].status != "skipped" || st8["theta"].reason != "no origin remote") throw new Exception($"no-origin skip wrong: {st8["theta"]}");
            }
            if (ScanEstate.Run(Path.Combine(root8, "repos"), out8, null, null, "team-a", true, Array.Empty<string>(), "P", "1", "2", StubScanner) != 0) throw new Exception("allow-stale run failed");
            using (var m8b = SEManifest(out8))
            {
                var th8 = m8b.RootElement.GetProperty("repos").EnumerateArray().First(e => e.GetProperty("name").GetString() == "theta");
                if (th8.GetProperty("status").GetString() != "skipped") throw new Exception("no-origin must be skipped even with --allow-stale (F2 never bypassable)");
            }
            Console.WriteLine("ok   scan-estate-alternates (same-origin dedupe; no-origin never bypassable)"); pass++;
            ForceDelete(root8); ForceDelete(out8);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-alternates: {ex.Message}"); }

        // 9. sidecar preservation: ownership update path; producer derives from a pre-placed delta; delta wins
        try
        {
            var root9 = Path.Combine(Path.GetTempPath(), "ua-se9-" + Guid.NewGuid().ToString("N")[..8]);
            var out9 = Path.Combine(Path.GetTempPath(), "ua-se9-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root9);
            EstateRepo(root9, "alpha");
            EstateRepo(root9, "beta");
            if (ScanEstate.Run(Path.Combine(root9, "repos"), out9, null, null, "team-a", false, Array.Empty<string>(), "Newtonsoft.Json", "12.0.3", "13.0.3", StubScanner) != 0) throw new Exception("first run failed");
            // hand-edit ownership (alpha -> another team) and hand-place a REAL delta; delete producer
            var ownPath9 = Path.Combine(out9, "ownership.v0.json");
            var ownNode9 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ownPath9))!;
            foreach (var e9 in ownNode9["ownerships"]!.AsArray())
                if (e9!["repo"]!.GetValue<string>() == "alpha") e9["team"] = "team-z";
            File.WriteAllText(ownPath9, ownNode9.ToJsonString());
            File.WriteAllText(Path.Combine(out9, "delta.json"),
                "{\"version\":\"package-delta.v1\",\"sourceRepo\":\"https://example.invalid/x.git\",\"sourceCommitSha\":\"0000000000000000000000000000000000000000\",\"changes\":[{\"id\":\"real\",\"packageName\":\"Serilog\",\"ecosystem\":\"nuget\",\"changeType\":\"updated\",\"oldVersion\":\"3.0.0\",\"newVersion\":\"3.1.0\"}]}\n");
            File.Delete(Path.Combine(out9, "producer-evidence.v0.json"));
            if (ScanEstate.Run(Path.Combine(root9, "repos"), out9, null, null, "team-a", false, Array.Empty<string>(), null, null, null, StubScanner) != 0) throw new Exception("re-run failed");
            var own9 = File.ReadAllText(ownPath9);
            if (!own9.Contains("\"team\": \"team-z\"") || !own9.Contains("\"team\": \"team-a\"")) throw new Exception("ownership update must preserve the manual assignment");
            var prod9 = File.ReadAllText(Path.Combine(out9, "producer-evidence.v0.json"));
            if (!prod9.Contains("Serilog") || prod9.Contains("Newtonsoft")) throw new Exception("producer template must derive from the existing delta (no flags)");
            var delta9 = File.ReadAllText(Path.Combine(out9, "delta.json"));
            if (!delta9.Contains("Serilog")) throw new Exception("existing delta must win untouched");
            if (ScanEstate.Run(Path.Combine(root9, "repos"), out9, null, null, "team-b", false, Array.Empty<string>(), null, null, null, StubScanner) == 0) throw new Exception("--self mismatch must refuse");
            Console.WriteLine("ok   scan-estate-sidecar-preserve (update sacred; producer from delta; delta wins)"); pass++;
            ForceDelete(root9); ForceDelete(out9);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-sidecar-preserve: {ex.Message}"); }

        // 10. refusals: --tracemap when needed, --self bootstrap, delta trio, empty fresh set, ≠1-change delta
        try
        {
            var root10 = Path.Combine(Path.GetTempPath(), "ua-se10-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(root10);
            EstateRepo(root10, "alpha");
            var out10 = Path.Combine(Path.GetTempPath(), "ua-se10-out-" + Guid.NewGuid().ToString("N")[..8]);
            // no --tracemap, no seam, scan needed
            if (ScanEstate.Run(Path.Combine(root10, "repos"), out10, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", null) != 1) throw new Exception("missing --tracemap must exit 1");
            // no --self, no existing ownership
            var out10b = Path.Combine(Path.GetTempPath(), "ua-se10-out2-" + Guid.NewGuid().ToString("N")[..8]);
            if (ScanEstate.Run(Path.Combine(root10, "repos"), out10b, null, null, null, false, Array.Empty<string>(), "P", "1", "2", StubScanner) != 1) throw new Exception("missing --self must exit 1");
            // no delta file, no trio
            var out10c = Path.Combine(Path.GetTempPath(), "ua-se10-out3-" + Guid.NewGuid().ToString("N")[..8]);
            if (ScanEstate.Run(Path.Combine(root10, "repos"), out10c, null, null, "team-a", false, Array.Empty<string>(), "P", null, "2", StubScanner) != 1) throw new Exception("missing --delta-old must exit 1");
            if (File.Exists(Path.Combine(out10c, "ownership.v0.json"))) throw new Exception("bootstrap must not write sidecars on the delta-trio refusal");
            // empty fresh set: everything out of scope -> manifest written, typed error, no sidecars
            var scopeAll = Path.Combine(Path.GetTempPath(), "ua-se10-scope.json");
            File.WriteAllText(scopeAll, "{\"schemaVersion\":\"estate-scope.v0\",\"exclude\":[\"alpha\"]}\n");
            var out10d = Path.Combine(Path.GetTempPath(), "ua-se10-out4-" + Guid.NewGuid().ToString("N")[..8]);
            if (ScanEstate.Run(Path.Combine(root10, "repos"), out10d, null, scopeAll, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner) != 1) throw new Exception("empty fresh set must exit 1");
            if (!File.Exists(Path.Combine(out10d, "scan-estate.v1.json"))) throw new Exception("manifest must survive the empty-fresh-set error");
            if (File.Exists(Path.Combine(out10d, "ownership.v0.json"))) throw new Exception("no sidecars on the empty-fresh-set path");
            // pre-placed 2-change delta -> typed refusal at bootstrap; null changes -> typed refusal, never an NRE
            var out10e = Path.Combine(Path.GetTempPath(), "ua-se10-out5-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(out10e);
            File.WriteAllText(Path.Combine(out10e, "delta.json"),
                "{\"version\":\"package-delta.v1\",\"sourceRepo\":\"https://example.invalid/x.git\",\"sourceCommitSha\":\"0000000000000000000000000000000000000000\",\"changes\":[{\"id\":\"a\",\"packageName\":\"P\",\"ecosystem\":\"nuget\",\"changeType\":\"updated\",\"oldVersion\":\"1\",\"newVersion\":\"2\"},{\"id\":\"b\",\"packageName\":\"Q\",\"ecosystem\":\"nuget\",\"changeType\":\"updated\",\"oldVersion\":\"1\",\"newVersion\":\"2\"}]}\n");
            if (ScanEstate.Run(Path.Combine(root10, "repos"), out10e, null, null, "team-a", false, Array.Empty<string>(), null, null, null, StubScanner) != 1) throw new Exception("2-change delta must exit 1");
            var out10f = Path.Combine(Path.GetTempPath(), "ua-se10-out6-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(out10f);
            File.WriteAllText(Path.Combine(out10f, "delta.json"), "{\"version\":\"package-delta.v1\",\"sourceRepo\":\"https://example.invalid/x.git\",\"sourceCommitSha\":\"0000000000000000000000000000000000000000\",\"changes\":null}\n");
            if (ScanEstate.Run(Path.Combine(root10, "repos"), out10f, null, null, "team-a", false, Array.Empty<string>(), null, null, null, StubScanner) != 1) throw new Exception("null-changes delta must exit 1 (typed), not crash");
            Console.WriteLine("ok   scan-estate-refusals (tracemap/self/trio/empty-set/multi-change)"); pass++;
            ForceDelete(root10);
            foreach (var d in new[] { out10, out10b, out10c, out10d, out10e }) if (Directory.Exists(d)) ForceDelete(d);
            File.Delete(scopeAll);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-refusals: {ex.Message}"); }

        // 11. ingest --scope discovery: excluded child skipped, svc-net48 alternate-blocked, explicit wins,
        //     swap leftovers ignored while dot-named REAL scans stay discoverable (SPEC-011 contract)
        try
        {
            var rich11 = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var root11 = Path.Combine(Path.GetTempPath(), "ua-se11-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(Path.Combine(rich11, "scans"), root11);
            string DotFact(string repo, string id) =>
                $"{{\"factId\":\"{id}\",\"scanId\":\"s\",\"repo\":\"{repo}\",\"commitSha\":\"c\",\"factType\":\"PackageReferenced\",\"ruleId\":\"project.file.v1\",\"evidenceTier\":\"Tier2Structural\",\"evidence\":{{\"filePath\":\"a.csproj\",\"startLine\":1,\"endLine\":1}},\"properties\":{{\"ecosystem\":\"nuget\",\"manifestKind\":\"packagereference\",\"packageName\":\"Newtonsoft.Json\",\"version\":\"12.0.3\"}}}}\n";
            Directory.CreateDirectory(Path.Combine(root11, ".planted.tmp-1234abcd")); // swap-pattern leftover WITH facts
            File.WriteAllText(Path.Combine(root11, ".planted.tmp-1234abcd", "facts.ndjson"), DotFact("planted", "f-swap"));
            Directory.CreateDirectory(Path.Combine(root11, ".dotscan")); // dot-named REAL scan — SPEC-011 says discover it
            File.WriteAllText(Path.Combine(root11, ".dotscan", "facts.ndjson"), DotFact("dotscan", "f-dot"));
            var scope11 = Path.Combine(Path.GetTempPath(), "ua-se11-scope.json");
            File.WriteAllText(scope11, "{\"schemaVersion\":\"estate-scope.v0\",\"exclude\":[\"svc\"]}\n");
            var out11 = Path.Combine(Path.GetTempPath(), "ua-se11-out-" + Guid.NewGuid().ToString("N")[..8]);
            var rc11 = Ingest.Run(Array.Empty<string>(), out11,
                Path.Combine(rich11, "sidecars", "producer-evidence.v0.json"), Path.Combine(rich11, "sidecars", "ownership.v0.json"),
                Path.Combine(rich11, "sidecars", "delta.json"), root11, scope11);
            if (rc11 != 0) throw new Exception($"scoped ingest exited {rc11}");
            var px11 = JsonDocument.Parse(File.ReadAllText(Path.Combine(out11, "input", "package-evidence.v0.json"))).RootElement;
            var repos11 = px11.GetProperty("scanCoverage").EnumerateArray().Select(c => c.GetProperty("repo").GetString()).ToList();
            if (repos11.Contains("svc")) throw new Exception("svc must be out of scope");
            if (repos11.Contains("svc-net48")) throw new Exception("excluding svc must not let svc-net48 sneak in as the first snapshot");
            if (repos11.Contains("planted")) throw new Exception("swap-pattern leftover (.planted.tmp-1234abcd) must be ignored at discovery");
            if (!repos11.Contains("dotscan")) throw new Exception("dot-named scan WITH facts.ndjson must stay discoverable (SPEC-011 contract)");
            if (!File.Exists(Path.Combine(out11, "input", "scope.v0.json"))) throw new Exception("scope file not copied to input");
            // explicit out-of-scope dir: warns but includes (deliberate act)
            var out11b = Path.Combine(Path.GetTempPath(), "ua-se11-out2-" + Guid.NewGuid().ToString("N")[..8]);
            var rc11b = Ingest.Run(new[] { Path.Combine(root11, "svc") }, out11b,
                Path.Combine(rich11, "sidecars", "producer-evidence.v0.json"), Path.Combine(rich11, "sidecars", "ownership.v0.json"),
                Path.Combine(rich11, "sidecars", "delta.json"), null, scope11);
            if (rc11b != 0) throw new Exception($"explicit out-of-scope ingest exited {rc11b}");
            var repos11b = JsonDocument.Parse(File.ReadAllText(Path.Combine(out11b, "input", "package-evidence.v0.json"))).RootElement
                .GetProperty("scanCoverage").EnumerateArray().Select(c => c.GetProperty("repo").GetString()).ToList();
            if (!repos11b.Contains("svc")) throw new Exception("explicit dir must be included despite scope");
            Console.WriteLine("ok   ingest-scope-discovery (excluded + alternate-blocked + swap-ignored + .dotscan discovered + explicit wins)"); pass++;
            ForceDelete(root11); ForceDelete(out11); ForceDelete(out11b); File.Delete(scope11);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL ingest-scope-discovery: {ex.Message}"); }

        // 12. ownership --scope: excluded child skipped (alternate-blocked too); an excluded BROKEN
        //     child (manifest-only) warns, never aborts the run
        try
        {
            var rich12 = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var root12 = Path.Combine(Path.GetTempPath(), "ua-se12-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(Path.Combine(rich12, "scans"), root12);
            Directory.CreateDirectory(Path.Combine(root12, "brokenx"));
            File.WriteAllText(Path.Combine(root12, "brokenx", "scan-manifest.json"), "{}"); // excluded + not a valid scan
            var scope12 = Path.Combine(Path.GetTempPath(), "ua-se12-scope.json");
            File.WriteAllText(scope12, "{\"schemaVersion\":\"estate-scope.v0\",\"exclude\":[\"svc\",\"brokenx\"]}\n");
            var own12 = Path.Combine(Path.GetTempPath(), "ua-se12-own.json");
            if (Ownership.Init(Array.Empty<string>(), root12, "team-a", "all-self", null, own12, scope12) != 0) throw new Exception("scoped ownership init failed (excluded broken child must not abort)");
            var text12 = File.ReadAllText(own12);
            if (text12.Contains("svc")) throw new Exception("svc must be excluded from ownership");
            if (!text12.Contains("billing")) throw new Exception("billing must remain");
            Console.WriteLine("ok   ownership-scope-discovery (excluded child skipped; excluded broken child tolerates)"); pass++;
            ForceDelete(root12); File.Delete(own12); File.Delete(scope12);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL ownership-scope-discovery: {ex.Message}"); }

        // 13. --out byte-equality: plan/report files byte-identical to captured child stdout; bare --out refuses
        try
        {
            var exe13 = Environment.ProcessPath ?? throw new Exception("ProcessPath unavailable");
            var f9_13 = Path.Combine(fixturesRoot, "F9-multi-lockfile-disagreement");
            foreach (var cmd in new[] { "plan", "report" })
            {
                var outFile13 = Path.Combine(Path.GetTempPath(), $"ua-out13-{cmd}.tmp");
                var psiOut = new System.Diagnostics.ProcessStartInfo(exe13) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true };
                psiOut.ArgumentList.Add(cmd); psiOut.ArgumentList.Add(f9_13); psiOut.ArgumentList.Add("--out"); psiOut.ArgumentList.Add(outFile13);
                psiOut.StandardOutputEncoding = System.Text.Encoding.UTF8;
                using var pOut = System.Diagnostics.Process.Start(psiOut)!;
                var stdoutOut = pOut.StandardOutput.ReadToEnd();
                pOut.StandardError.ReadToEnd(); pOut.WaitForExit(30000);
                if (pOut.ExitCode != 0) throw new Exception($"{cmd} --out exited {pOut.ExitCode}");
                if (stdoutOut.Length != 0) throw new Exception($"{cmd} --out must print nothing to stdout");

                var psiCap = new System.Diagnostics.ProcessStartInfo(exe13) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true };
                psiCap.ArgumentList.Add(cmd); psiCap.ArgumentList.Add(f9_13);
                psiCap.StandardOutputEncoding = System.Text.Encoding.UTF8;
                using var pCap = System.Diagnostics.Process.Start(psiCap)!;
                var stdoutCap = pCap.StandardOutput.ReadToEnd();
                pCap.StandardError.ReadToEnd(); pCap.WaitForExit(30000);
                if (pCap.ExitCode != 0) throw new Exception($"{cmd} capture exited {pCap.ExitCode}");

                var fileBytes = File.ReadAllBytes(outFile13);
                var stdoutBytes = new System.Text.UTF8Encoding(false).GetBytes(stdoutCap);
                if (!fileBytes.SequenceEqual(stdoutBytes)) throw new Exception($"{cmd}: --out file differs from redirected stdout bytes");
                if (fileBytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) throw new Exception($"{cmd}: --out file has a BOM");
                File.Delete(outFile13);
            }
            // bare trailing --out refuses (never a silent stdout fallback)
            if (Main(new[] { "plan", f9_13, "--out" }) != 1) throw new Exception("bare --out must exit 1");
            if (Main(new[] { "report", f9_13, "-o" }) != 1) throw new Exception("bare -o must exit 1");
            Console.WriteLine("ok   plan-report-out-byte-equality (file == redirected stdout, no BOM; bare --out refuses)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL plan-report-out-byte-equality: {ex.Message}"); }

        // 14. scope plan statement: include-mode echo + one shared validator across surfaces
        try
        {
            var src14 = Path.Combine(fixturesRoot, "F-scope");
            var inc14 = Path.Combine(Path.GetTempPath(), "ua-scope-inc-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(inc14, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(src14, "input"))) File.Copy(f, Path.Combine(inc14, "input", Path.GetFileName(f)));
            File.WriteAllText(Path.Combine(inc14, "input", "scope.v0.json"), "{\"schemaVersion\":\"estate-scope.v0\",\"include\":[\"R4\",\"R3\",\"R3\"],\"exclude\":[\"zz-last\",\"aa-first\"]}\n");
            var plan14 = LoadEngine(inc14).BuildPlan();
            if (plan14.Scope is null || plan14.Scope.Mode != "include") throw new Exception("include mode missing");
            if (plan14.Scope.IncludedCount != 2) throw new Exception($"includedCount must be the deduped config count (R3 twice), got {plan14.Scope.IncludedCount}");
            if (plan14.Scope.OutOfScope is not { Count: 2 } || plan14.Scope.OutOfScope[0] != "aa-first" || plan14.Scope.OutOfScope[1] != "zz-last")
                throw new Exception("outOfScope must be deduped + ordinal, never config order");
            if (!Report.Render(plan14).Contains("- Scope: include-mode — 2 repos in scope by config, 2 out of scope by config: aa-first, zz-last"))
                throw new Exception("include-mode report line wrong");
            // shared validator: same malformed file, same message at ingest-time and plan-load
            // (named scope.v0.json so both surfaces display the same name)
            var badText = "{\"schemaVersion\":\"estate-scope.no\",\"exclude\":[\"x\"]}";
            var bad14 = Path.Combine(Path.GetTempPath(), "ua-scope-bad-" + Guid.NewGuid().ToString("N")[..8], "scope.v0.json");
            Directory.CreateDirectory(Path.GetDirectoryName(bad14)!);
            File.WriteAllText(bad14, badText);
            var savedErr14 = Console.Error;
            var cap14 = new System.IO.StringWriter();
            Console.SetError(cap14);
            var rcBad = Ingest.Run(new[] { Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "scans", "svc") },
                Path.Combine(Path.GetTempPath(), "ua-scope-bad-out-" + Guid.NewGuid().ToString("N")[..8]), null, null, null, null, bad14);
            Console.SetError(savedErr14);
            if (rcBad != 5) throw new Exception($"ingest --scope malformed must exit 5, got {rcBad}");
            string? loadMsg14 = null;
            try
            {
                File.WriteAllText(Path.Combine(inc14, "input", "scope.v0.json"), badText);
                LoadEngine(inc14).BuildPlan();
            }
            catch (UaException ex14) { loadMsg14 = ex14.Message; }
            var ingestMsg14 = cap14.ToString().Split('\n').First(l => l.StartsWith("error: "));
            if (loadMsg14 is null || ingestMsg14 != "error: " + loadMsg14) throw new Exception($"shared-validator drift: ingest '{ingestMsg14}' vs load '{loadMsg14}'");
            // config contradictions
            var (_, e1) = Scope.ParseAndValidate("{\"schemaVersion\":\"estate-scope.v0\",\"include\":[\"a\"],\"exclude\":[\"a\"]}", "s.json");
            if (e1 is null || !e1.Contains("both include and exclude")) throw new Exception("include∩exclude must be a typed error");
            var (_, e2) = Scope.ParseAndValidate("{\"schemaVersion\":\"estate-scope.v0\",\"include\":[],\"exclude\":[\"x\"]}", "s.json");
            if (e2 is null || !e2.Contains("names nothing")) throw new Exception("include:[] + exclude must be a typed error");
            var (noop, e3) = Scope.ParseAndValidate("{\"schemaVersion\":\"estate-scope.v0\"}", "s.json");
            if (e3 is not null || noop is not null) throw new Exception("validates-to-nothing scope must behave as absent");
            Console.WriteLine("ok   scope-plan-statement (include echo; one validator across surfaces; config errors)"); pass++;
            Directory.Delete(inc14, true); Directory.Delete(Path.GetDirectoryName(bad14)!, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scope-plan-statement: {ex.Message}"); }

        // 15. PR #2 Baz round 1: empty origin URL never becomes the dedupe key; ALL --delta-* flags
        //     warn (not just --delta-package); persisted failure reasons scrub URL userinfo
        try
        {
            // empty origin URL: F2 passes (origin listed), the URL lookup yields "" -> visible skip, never "" dedupe
            var root15 = Path.Combine(Path.GetTempPath(), "ua-se15-" + Guid.NewGuid().ToString("N")[..8]);
            var out15 = Path.Combine(Path.GetTempPath(), "ua-se15-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root15, "repos"));
            var e1_15 = EstateRepo(root15, "alpha");
            EstateRepo(root15, "beta");
            Push.Git(e1_15, "remote set-url origin \"\"", out _, out _); // origin exists, URL empty
            var rc15 = ScanEstate.Run(Path.Combine(root15, "repos"), out15, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner);
            if (rc15 != 0) throw new Exception($"beta is fresh — run must succeed despite alpha's broken origin URL (exit {rc15})");
            using (var m15 = SEManifest(out15))
            {
                var a15 = m15.RootElement.GetProperty("repos").EnumerateArray().First(e => e.GetProperty("name").GetString() == "alpha");
                if (a15.GetProperty("status").GetString() != "skipped" || !a15.GetProperty("reason").GetString()!.Contains("no usable URL"))
                    throw new Exception($"empty-origin skip wrong: {a15.GetRawText()}");
                if (!m15.RootElement.GetProperty("repos").EnumerateArray().Any(e => e.GetProperty("name").GetString() == "beta" && e.GetProperty("status").GetString() == "scanned"))
                    throw new Exception("beta must scan normally");
            }

            // all three --delta-* flags warn when an existing delta wins (none silently dropped);
            // --delta-package matches the existing target so the mismatch refusal stays out of the way
            var out15b = Path.Combine(Path.GetTempPath(), "ua-se15-out2-" + Guid.NewGuid().ToString("N")[..8]);
            if (ScanEstate.Run(Path.Combine(root15, "repos"), out15b, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner) != 0)
                throw new Exception("seed run failed");
            Push.Git(e1_15, "remote set-url origin \"" + Path.Combine(root15, "origins", "alpha.git") + "\"", out _, out _); // restore alpha's URL
            var savedErr15 = Console.Error;
            var cap15 = new System.IO.StringWriter();
            Console.SetError(cap15);
            var rc15b = ScanEstate.Run(Path.Combine(root15, "repos"), out15b, null, null, "team-a", false, Array.Empty<string>(), "P", "9.9.9", "9.9.9", StubScanner);
            Console.SetError(savedErr15);
            if (rc15b != 0) throw new Exception($"re-run exited {rc15b}");
            var warn15 = cap15.ToString();
            if (!warn15.Contains("existing delta.json wins (--delta-package --delta-old --delta-new ignored)"))
                throw new Exception($"all-flags delta warning missing or incomplete: {string.Join(" | ", warn15.Split('\n').Where(l => l.Contains("delta.json wins")))}");

            // TrimReason scrubs credential-bearing userinfo before it can reach the manifest
            var scrubbed15 = ScanEstate.TrimReason("fatal: unable to access 'https://user:s3cr3t-token@git.corp.example.com/org/repo.git/': failed connect");
            if (scrubbed15.Contains("s3cr3t-token") || scrubbed15.Contains("user:") || !scrubbed15.Contains("***@git.corp.example.com"))
                throw new Exception($"userinfo not scrubbed: {scrubbed15}");
            var scp15 = ScanEstate.TrimReason("fatal: user:pat@git.internal: repo not found");
            if (scp15.Contains("pat") || !scp15.Contains("***@")) throw new Exception($"scheme-less userinfo not scrubbed: {scp15}");
            // Codex P1: USERNAME-ONLY userinfo (PAT-as-username, no colon) must scrub too
            var pat15 = ScanEstate.TrimReason("fatal: unable to access 'https://ghp_Pr0vIdEnCe123@git.example.com/org/repo.git/': auth failed");
            if (pat15.Contains("ghp_") || !pat15.Contains("***@git.example.com")) throw new Exception($"username-only PAT not scrubbed: {pat15}");
            // Codex r3 P1 + r4/Baz r4: scp-style SINGLE-LABEL hosts (alias@git:org/repo — no dot) must
            // scrub, the host ITSELF masks (internal alias names never reach manifest or sanitized
            // stderr), and underscore aliases (my_ghe:) are recognized like Redact.cs recognizes them
            var scpSingle15 = ScanEstate.TrimReason("secret-token@git:org/repo.git: Permission denied (publickey)");
            if (scpSingle15.Contains("secret-token") || scpSingle15.Contains("@git:") || !scpSingle15.Contains("***@***:")) throw new Exception($"single-label scp host not fully scrubbed: {scpSingle15}");
            var scpSingleB15 = ScanEstate.TrimReason("fatal: 's3cr3t-alias@ghe: Could not read from remote repository'");
            if (scpSingleB15.Contains("s3cr3t-alias") || scpSingleB15.Contains("ghe") || !scpSingleB15.Contains("***@***:")) throw new Exception($"single-label alias host not fully scrubbed: {scpSingleB15}");
            var scpUnder15 = ScanEstate.TrimReason("secret-token@my_ghe:org/repo.git: Permission denied");
            if (scpUnder15.Contains("secret-token") || scpUnder15.Contains("my_ghe") || !scpUnder15.Contains("***@***:")) throw new Exception($"underscore alias not fully scrubbed: {scpUnder15}");
            var dotted15 = ScanEstate.TrimReason("fatal: 'user:pat@git.internal: Could not read'");
            if (!dotted15.Contains("***@git.internal:")) throw new Exception($"dotted host must survive (diagnostic value): {dotted15}");
            // Codex r5: scheme-form URLs with dotless hosts mask too (path/end terminators, not just scp ':')
            var schemeDotless15 = ScanEstate.TrimReason("fatal: unable to access 'ssh://token@ghe/org/repo.git/': auth failed");
            if (schemeDotless15.Contains("token") || schemeDotless15.Contains("@ghe") || !schemeDotless15.Contains("***@***/")) throw new Exception($"scheme-form dotless host not fully masked: {schemeDotless15}");
            var httpsDotless15 = ScanEstate.TrimReason("fatal: unable to access 'https://PAT@intranet/org/repo.git/': denied");
            if (httpsDotless15.Contains("PAT") || httpsDotless15.Contains("@intranet") || !httpsDotless15.Contains("***@***/")) throw new Exception($"https dotless host not fully masked: {httpsDotless15}");
            // Codex P2: IsSwapDir tests BOTH markers independently (embedded .tmp- in a repo name
            // must not mask a real .old-<hex8> suffix)
            if (!Ingest.IsSwapDir(".foo.tmp-copy.old-deadbeef")) throw new Exception("embedded .tmp- masked the .old- marker");
            if (!Ingest.IsSwapDir(".name.tmp-1234abcd")) throw new Exception("plain tmp leftover missed");
            if (!Ingest.IsSwapDir(".weird.old-copy.tmp-00001111")) throw new Exception("embedded .old- masked the .tmp- marker");
            if (Ingest.IsSwapDir(".dotscan") || Ingest.IsSwapDir("normal") || Ingest.IsSwapDir(".x.tmp-nothex!") || Ingest.IsSwapDir(".x.tmp-1234abc")) throw new Exception("IsSwapDir over-matches");
            // Baz r2: cache metadata that is foreign (wrong schemaVersion) or null-exclude must
            // invalidate (rescan), never crash — exercised via the seam
            var metaDir15 = Path.Combine(out15b, "scans", "alpha");
            File.WriteAllText(Path.Combine(metaDir15, "scan-estate.json"), "{\"schemaVersion\":\"scan-estate-cache.v9\",\"exclude\":null}");
            var calls15 = 0;
            int Count15(ScanEstate.ScanRequest r) { System.Threading.Interlocked.Increment(ref calls15); return StubScanner(r); }
            if (ScanEstate.Run(Path.Combine(root15, "repos"), out15b, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count15) != 0) throw new Exception("foreign-metadata run failed");
            if (calls15 != 1) throw new Exception($"invalid metadata must force exactly alpha's rescan (beta reused), got {calls15}");
            File.WriteAllText(Path.Combine(metaDir15, "scan-estate.json"), "{\"schemaVersion\":\"scan-estate-cache.v1\",\"exclude\":null}");
            calls15 = 0;
            if (ScanEstate.Run(Path.Combine(root15, "repos"), out15b, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count15) != 0) throw new Exception("null-exclude run failed");
            if (calls15 != 1) throw new Exception($"null-exclude metadata must invalidate (rescan alpha only), got {calls15}");
            // Baz r2: symlinked children are visible skips (exercised where the platform allows creation)
            DirectoryInfo? link15 = null;
            try { link15 = (DirectoryInfo?)Directory.CreateSymbolicLink(Path.Combine(root15, "repos", "linky"), Path.Combine(root15, "repos", "beta")); }
            catch (IOException) { /* Windows without dev mode: creation unavailable — guard is platform-neutral */ }
            catch (UnauthorizedAccessException) { }
            if (link15 is not null)
            {
                calls15 = 0;
                if (ScanEstate.Run(Path.Combine(root15, "repos"), out15b, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count15) != 0) throw new Exception("symlink run failed");
                if (calls15 != 0) throw new Exception("symlinked child must not be scanned (alpha/beta reused)");
                using (var m15s = SEManifest(out15b))
                    if (!m15s.RootElement.GetProperty("repos").EnumerateArray().Any(e => e.GetProperty("name").GetString() == "linky" && e.GetProperty("status").GetString() == "skipped" && e.GetProperty("reason").GetString()!.Contains("symlinked")))
                        throw new Exception("symlink skip not recorded in the manifest");
            }
            Console.WriteLine("ok   scan-estate-baz-round1 (+r2: PAT scrub, both swap markers, metadata invalidation, symlink skip)"); pass++;
            ForceDelete(root15); ForceDelete(out15); ForceDelete(out15b);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL scan-estate-baz-round1: {ex.Message}"); }

        // SPEC-018: ua drift — feed truth, inventory, classification, canonical output, delta candidates
        try
        {
            var fd = Path.Combine(fixturesRoot, "F-drift");
            var feedFd = Path.Combine(fd, "input", "feed-versions.v1.json");
            // golden + determinism + emitted deltas (acceptance 6/7/8)
            var (rep1, canon1, rc1) = Drift.Run(fd, feedFd, null, null);
            if (rc1 != 0 || canon1 is null) throw new Exception($"drift exited {rc1}");
            if (canon1 != File.ReadAllText(Path.Combine(fd, "golden", "drift.v1.json"))) throw new Exception("drift.v1 differs from golden");
            var (rep2, canon2, rc2) = Drift.Run(fd, feedFd, null, null);
            if (rc2 != 0 || canon2 != canon1) throw new Exception("drift not deterministic");
            var emit18 = Path.Combine(Path.GetTempPath(), "ua-dr18-" + Guid.NewGuid().ToString("N")[..8]);
            if (Drift.Run(fd, feedFd, null, emit18).Item3 != 0) throw new Exception("emit run failed");
            var goldenDeltas = Path.Combine(fd, "golden", "deltas");
            var emitted = Directory.GetFiles(emit18).OrderBy(f => f, StringComparer.Ordinal).Select(Path.GetFileName).ToArray();
            var wanted = Directory.GetFiles(goldenDeltas).OrderBy(f => f, StringComparer.Ordinal).Select(Path.GetFileName).ToArray();
            if (!emitted.SequenceEqual(wanted) || emitted.Length != 1) throw new Exception($"emitted {emitted.Length} candidates, expected exactly the golden set");
            foreach (var f in emitted)
                if (File.ReadAllText(Path.Combine(emit18, f)) != File.ReadAllText(Path.Combine(goldenDeltas, f))) throw new Exception($"emitted {f} differs from golden");
            // summary table pinned by the spec (verified against the committed scans)
            var doc18 = JsonDocument.Parse(canon1).RootElement;
            var sum18 = doc18.GetProperty("summary");
            if (sum18.GetProperty("packagesObserved").GetInt32() != 4 || sum18.GetProperty("behind").GetInt32() != 1
                || sum18.GetProperty("current").GetInt32() != 2 || sum18.GetProperty("ahead").GetInt32() != 1
                || sum18.GetProperty("unclassified").GetInt32() != 4 || sum18.GetProperty("unknownFeedPackages").GetInt32() != 1)
                throw new Exception("summary table drifted: " + sum18.GetRawText());
            var declPin18 = doc18.GetProperty("packages").EnumerateArray().First(p => p.GetProperty("packageId").GetString() == "Serilog")
                .GetProperty("installations").EnumerateArray().Single(i => i.GetProperty("evidence").GetString() == "declared-pin");
            if (declPin18.GetProperty("repo").GetString() != "cleanrepo" || declPin18.GetProperty("status").GetString() != "current") throw new Exception("cleanrepo declared-pin survivor wrong");
            Directory.Delete(emit18, true);
            Console.WriteLine("ok   drift-golden (F-drift bytes + deltas + determinism + pinned summary)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL drift-golden: {ex.Message}"); }

        // folder feeds: conversion shapes, unresolvable skip, two-versions refusal, missing dir (acceptance 1)
        try
        {
            var ff = Path.Combine(Path.GetTempPath(), "ua-dr-ff-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(ff);
            foreach (var n in new[] { "Newtonsoft.Json.13.0.3.nupkg", "X.13.0.3-beta.1.nupkg", "Foo-Bar.1.0.0.nupkg", "Foo.1.2.0.0.nupkg", "notaversion.nupkg" })
                File.WriteAllText(Path.Combine(ff, n), "empty");
            var fd19 = Path.Combine(fixturesRoot, "F-drift");
            var (_, canon19, rc19) = Drift.Run(fd19, null, ff, null);
            if (rc19 != 0 || canon19 is null) throw new Exception($"folder-feed drift exited {rc19}");
            var doc19 = JsonDocument.Parse(canon19).RootElement;
            var feed19 = doc19.GetProperty("feed");
            if (feed19.GetProperty("source").GetString() != "folder-feed" || feed19.GetProperty("packageCount").GetInt32() != 4)
                throw new Exception($"folder conversion wrong: {feed19.GetRawText()}");
            if (feed19.TryGetProperty("asOf", out _)) throw new Exception("folder feed must not carry asOf");
            // the folder's own truth classifies the estate: Newtonsoft latest 13.0.3 → no longer unclassified
            var nj19 = doc19.GetProperty("packages").EnumerateArray().First(p => p.GetProperty("packageId").GetString() == "Newtonsoft.Json");
            if (!nj19.GetProperty("installations").EnumerateArray().All(i => new[] { "current", "behind" }.Contains(i.GetProperty("status").GetString())))
                throw new Exception("folder-derived latest must classify numeric Newtonsoft rows");
            if (Drift.Run(fd19, null, ff + "-absent", null).Item3 != 1) throw new Exception("missing folder-feed dir must exit 1");
            var beforeCount19 = JsonDocument.Parse(Drift.Run(fd19, null, ff, null).Item2!).RootElement.GetProperty("feed").GetProperty("packageCount").GetInt32();
            File.WriteAllText(Path.Combine(ff, "newtonsoft.json.13.0.3.nupkg"), "empty"); // case-variant SAME version -> collapses
            if (Drift.Run(fd19, null, ff, null).Item3 != 0
                || JsonDocument.Parse(Drift.Run(fd19, null, ff, null).Item2!).RootElement.GetProperty("feed").GetProperty("packageCount").GetInt32() != beforeCount19)
                throw new Exception("case-variant same-version folder files must collapse (not refuse, not double-count)");
            File.WriteAllText(Path.Combine(ff, "Foo.1.2.0.nupkg"), "empty"); // second version of Foo
            if (Drift.Run(fd19, null, ff, null).Item3 != 5) throw new Exception("one id at two versions in a folder must exit 5");
            // parse-shape unit pins (greedy-longest, prerelease-with-dots, hyphen ids, numeric-ending ids)
            if (Drift.ParseNupkgName("Newtonsoft.Json.13.0.3") != ("Newtonsoft.Json", "13.0.3")) throw new Exception("dotted id parse wrong");
            if (Drift.ParseNupkgName("X.13.0.3-beta.1") != ("X", "13.0.3-beta.1")) throw new Exception("prerelease-with-dots parse wrong");
            if (Drift.ParseNupkgName("Foo-Bar.1.0.0") != ("Foo-Bar", "1.0.0")) throw new Exception("hyphenated id parse wrong");
            if (Drift.ParseNupkgName("Foo.1.2.0.0") != ("Foo", "1.2.0.0")) throw new Exception("numeric-ending id must resolve longest-version");
            if (Drift.ParseNupkgName("notaversion").Item1 is not null) throw new Exception("unresolvable must be null");
            Console.WriteLine("ok   drift-folder-feed (shapes, skip, two-versions exit 5, missing dir)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL drift-folder-feed: {ex.Message}"); }

        // feed validation + casing + parity + CLI contract (acceptance 2/3/4/5)
        try
        {
            var fd20 = Path.Combine(fixturesRoot, "F-drift");
            string FeedPath(string name, string content)
            {
                var p = Path.Combine(Path.GetTempPath(), "ua-dr20-" + Guid.NewGuid().ToString("N")[..8], name);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, content);
                return p;
            }
            if (Drift.Run(fd20, FeedPath("f1.json", "{\"schemaVersion\":\"feed-versions.v0\",\"packages\":[]}"), null, null).Item3 != 5) throw new Exception("schemaVersion mismatch must exit 5");
            if (Drift.Run(fd20, FeedPath("f2.json", "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"weird\",\"packages\":[]}"), null, null).Item3 != 5) throw new Exception("bad source must exit 5");
            if (Drift.Run(fd20, FeedPath("f2n1.json", "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":null}"), null, null).Item3 != 5) throw new Exception("packages:null must exit 5 (typed), not crash");
            if (Drift.Run(fd20, FeedPath("f2n2.json", "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":[null]}"), null, null).Item3 != 5) throw new Exception("null packages[] element must exit 5 (typed), not crash");
            if (Drift.Run(fd20, FeedPath("f2n3.json", "{\"schemaVersion\":null,\"source\":null,\"packages\":[]}"), null, null).Item3 != 5) throw new Exception("null schemaVersion/source must exit 5 (typed), not crash");
            if (Drift.Run(fd20, FeedPath("f2n4.json", "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":[{\"packageId\":\"X\",\"version\":\"1.0.0\"},{\"packageId\":\"x\",\"version\":\"2.0.0\"}]}"), null, null).Item3 != 5) throw new Exception("case-variant id at two versions must exit 5");
            if (Drift.Run(fd20, FeedPath("f3.json", "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":[{\"packageId\":\"Serilog\",\"version\":\"3.1.1\"},{\"packageId\":\"serilog\",\"version\":\"4.0.0\"}]}"), null, null).Item3 != 5) throw new Exception("same id two versions must exit 5");
            var dupPath = FeedPath("f4.json", "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":[{\"packageId\":\"Serilog\",\"version\":\"3.1.1\"},{\"packageId\":\"Serilog\",\"version\":\"3.1.1\"}]}");
            var (_, dupCanon, dupRc) = Drift.Run(fd20, dupPath, null, null);
            if (dupRc != 0 || JsonDocument.Parse(dupCanon!).RootElement.GetProperty("feed").GetProperty("packageCount").GetInt32() != 1)
                throw new Exception("identical duplicates must collapse to packageCount 1");
            if (Drift.Run(fd20, fd20 + "-absent.json", null, null).Item3 != 1) throw new Exception("missing feed file must exit 1");
            if (Drift.Run(fd20, dupPath, dupPath, null).Item3 != 1) throw new Exception("both truth sources must exit 1");
            if (Drift.Run(fd20, null, null, null).Item3 != 1) throw new Exception("no truth source must exit 1");

            // casing: lowercased shipping lockfile rows + lowercased feed → ONE Newtonsoft entry, estate spelling
            var scratch20 = Path.Combine(Path.GetTempPath(), "ua-dr20x-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(fd20, scratch20);
            var lockPath20 = Path.Combine(scratch20, "input", "lockfile-rows.v2.json");
            var lockNode20 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(lockPath20))!;
            foreach (var rep20 in lockNode20["repos"]!.AsArray())
                if (rep20!["repo"]!.GetValue<string>() == "shipping")
                    foreach (var row20 in rep20["rows"]!.AsArray())
                        if (row20!["packageId"]!.GetValue<string>() == "Newtonsoft.Json") row20["packageId"] = "newtonsoft.json";
            File.WriteAllText(lockPath20, lockNode20.ToJsonString());
            var lowerFeed = FeedPath("f5.json", "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":[{\"packageId\":\"newtonsoft.json\",\"version\":\"13.0.3\"}]}");
            var (_, canonCasing, rcCasing) = Drift.Run(scratch20, lowerFeed, null, null);
            if (rcCasing != 0) throw new Exception($"casing run exited {rcCasing}");
            var pkgs20 = JsonDocument.Parse(canonCasing!).RootElement.GetProperty("packages").EnumerateArray().Select(p => p.GetProperty("packageId").GetString()).ToList();
            if (pkgs20.Count(p => p.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase)) != 1 || !pkgs20.Contains("Newtonsoft.Json"))
                throw new Exception($"case-variant rows must merge to ONE entry with the estate spelling: {string.Join(",", pkgs20)}");

            // classification parity, structural: the shapes apply also judges (CompareCore rows)
            (string, string, string)[] shapes =
            {
                ("13.0.3", "13.0.3.0", "current"), ("13", "13.0.0.0", "current"), ("12.0.3", "13.0.3", "behind"),
                ("14.0.0", "13.0.3", "ahead"), ("13.0.3-beta", "13.0.3", "unclassified"), ("13.0.3", "13.0.3-beta.1", "unclassified"),
                ("1.0.*", "2.0.0", "unclassified"), ("01.2", "2.0.0", "behind"), // CompareCore parses "01"->1 (its guard rejects only zero-valued pads) — parity means agreeing with it
            };
            foreach (var (installed, latest, want) in shapes)
            {
                var cmp = Apply.CompareCore(installed, latest);
                var got = cmp switch { < 0 => "behind", 0 => "current", > 0 => "ahead", _ => "unclassified" };
                if (got != want) throw new Exception($"parity shape {installed} vs {latest}: {got}, expected {want}");
            }
            if (Drift.Run(fd20, FeedPath("f6.json", "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":[{\"packageId\":\"Contoso.Core\",\"version\":\"13.0.3-beta\"}]}"), null, null).Item1!
                    .Packages.First(p => p.PackageId == "Contoso.Core").Installations.All(i => i.Status != "unclassified" || i.Reason is null))
                throw new Exception("unclassified rows must carry a D-template reason");
            Directory.Delete(scratch20, true);
            Console.WriteLine("ok   drift-feed-validation (exits 5/1; collapse; casing merge; CompareCore parity)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL drift-feed-validation: {ex.Message}"); }

        // CLI contract: --out byte-equality + sanitized identity + bare flag + delta-precondition refusals (acceptance 5/7)
        try
        {
            var exe21 = Environment.ProcessPath ?? throw new Exception("ProcessPath unavailable");
            var fd21 = Path.Combine(fixturesRoot, "F-drift");
            var feed21 = Path.Combine(fd21, "input", "feed-versions.v1.json");
            var out21 = Path.Combine(Path.GetTempPath(), "ua-dr21.tmp");
            var psiOut = new System.Diagnostics.ProcessStartInfo(exe21) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true };
            psiOut.ArgumentList.Add("drift"); psiOut.ArgumentList.Add(fd21); psiOut.ArgumentList.Add("--feed"); psiOut.ArgumentList.Add(feed21);
            psiOut.ArgumentList.Add("--out"); psiOut.ArgumentList.Add(out21);
            psiOut.StandardOutputEncoding = System.Text.Encoding.UTF8;
            using (var p = System.Diagnostics.Process.Start(psiOut)!)
            {
                var so = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(30000);
                if (p.ExitCode != 0 || so.Length != 0) throw new Exception($"drift --out exited {p.ExitCode}, stdout {so.Length} chars");
            }
            var psiCap = new System.Diagnostics.ProcessStartInfo(exe21) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true };
            psiCap.ArgumentList.Add("drift"); psiCap.ArgumentList.Add(fd21); psiCap.ArgumentList.Add("--feed"); psiCap.ArgumentList.Add(feed21);
            psiCap.StandardOutputEncoding = System.Text.Encoding.UTF8;
            string stdoutCap;
            using (var p = System.Diagnostics.Process.Start(psiCap)!)
            { stdoutCap = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(30000); }
            if (!File.ReadAllBytes(out21).SequenceEqual(new System.Text.UTF8Encoding(false).GetBytes(stdoutCap))) throw new Exception("--out file differs from stdout bytes");
            if (File.ReadAllBytes(out21).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) throw new Exception("--out file has a BOM");
            File.Delete(out21);
            // sanitized stdout is byte-identical (canonical artifact bypass)
            var psiSan = new System.Diagnostics.ProcessStartInfo(exe21) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true };
            psiSan.ArgumentList.Add("drift"); psiSan.ArgumentList.Add(fd21); psiSan.ArgumentList.Add("--feed"); psiSan.ArgumentList.Add(feed21); psiSan.ArgumentList.Add("--sanitized");
            psiSan.StandardOutputEncoding = System.Text.Encoding.UTF8;
            string stdoutSan;
            using (var p = System.Diagnostics.Process.Start(psiSan)!)
            { stdoutSan = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(30000); }
            if (stdoutSan != stdoutCap) throw new Exception("--sanitized drift stdout differs from canonical");
            if (Main(new[] { "drift", fd21, "--feed", feed21, "--out" }) != 1) throw new Exception("bare trailing --out must exit 1");
            if (Main(new[] { "drift", fd21, "--feed", feed21, "-o" }) != 1) throw new Exception("bare trailing -o must exit 1");
            var emitDir21 = Path.Combine(Path.GetTempPath(), "ua-dr21e-" + Guid.NewGuid().ToString("N")[..8]);
            if (Drift.Run(fd21, feed21, null, emitDir21).Item3 != 0) throw new Exception("emit seed failed");
            if (Drift.Run(fd21, feed21, null, emitDir21).Item3 != 3) throw new Exception("re-emit into the non-empty dir must exit 3 (stale-set refusal)");
            // all-current rerun must ALSO refuse the stale dir (the no-behind path checks the destination first)
            var feedCur21 = Path.Combine(Path.GetTempPath(), "ua-dr21cur-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            File.WriteAllText(feedCur21, "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":[{\"packageId\":\"Contoso.Payments\",\"version\":\"1.0.0\"},{\"packageId\":\"Serilog\",\"version\":\"3.1.1\"},{\"packageId\":\"Newtonsoft.Json\",\"version\":\"13.0.3\"},{\"packageId\":\"Contoso.Core\",\"version\":\"1.0.0\"}]}");
            if (Drift.Run(fd21, feedCur21, null, emitDir21).Item3 != 3)
                throw new Exception("all-current rerun into the stale dir must exit 3 — no-behind must not skip the destination check");
            File.Delete(feedCur21);
            Directory.Delete(emitDir21, true);
            // delta = loader precondition: deleted → exit 3 naming it; 2-change → exit 3; dual-fault (fixture+feed) → 3
            var scratch21 = Path.Combine(Path.GetTempPath(), "ua-dr21x-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(fd21, scratch21);
            File.Delete(Path.Combine(scratch21, "input", "delta.json"));
            if (Drift.Run(scratch21, feed21, null, null).Item3 != 3) throw new Exception("delta-less fixture must exit 3");
            File.WriteAllText(Path.Combine(scratch21, "input", "delta.json"), "{\"version\":\"package-delta.v1\",\"sourceRepo\":\"x\",\"sourceCommitSha\":\"0\",\"changes\":[{\"id\":\"a\",\"packageName\":\"P\",\"ecosystem\":\"nuget\",\"changeType\":\"updated\",\"oldVersion\":\"1\",\"newVersion\":\"2\"},{\"id\":\"b\",\"packageName\":\"Q\",\"ecosystem\":\"nuget\",\"changeType\":\"updated\",\"oldVersion\":\"1\",\"newVersion\":\"2\"}]}");
            if (Drift.Run(scratch21, feed21, null, null).Item3 != 3) throw new Exception("2-change delta must exit 3");
            if (Drift.Run(scratch21, feed21 + "-absent", null, null).Item3 != 3) throw new Exception("dual fault must resolve fixture-first (exit 3)");
            Directory.Delete(scratch21, true);
            Console.WriteLine("ok   drift-cli-contract (--out==stdout no BOM; sanitized identical; refusals)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL drift-cli-contract: {ex.Message}"); }

        // permutation + swap-plan: array order never reaches the bytes; emitted delta drives a real plan (acceptance 6/8)
        try
        {
            var fd22 = Path.Combine(fixturesRoot, "F-drift");
            var scratch22 = Path.Combine(Path.GetTempPath(), "ua-dr22-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(scratch22, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(fd22, "input"))) File.Copy(f, Path.Combine(scratch22, "input", Path.GetFileName(f)));
            foreach (var f in Directory.GetFiles(Path.Combine(scratch22, "input")))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(f));
                File.WriteAllText(f, CanonicalJson(doc.RootElement));
            }
            var (_, permCanon, permRc) = Drift.Run(scratch22, Path.Combine(scratch22, "input", "feed-versions.v1.json"), null, null);
            if (permRc != 0 || permCanon != File.ReadAllText(Path.Combine(fd22, "golden", "drift.v1.json")))
                throw new Exception("drift bytes depend on input array order (incl. feed packages[])");
            Directory.Delete(scratch22, true);

            // swap the emitted candidate in: the estate-spelled packageName plans to the expected affected set
            var swap22 = Path.Combine(Path.GetTempPath(), "ua-dr22x-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(fd22, swap22);
            File.Copy(Path.Combine(fd22, "golden", "deltas", "Contoso.Payments.1.0.0.delta.json"), Path.Combine(swap22, "input", "delta.json"), true);
            var plan22 = LoadEngine(swap22).BuildPlan();
            var affected22 = plan22.Repos.Where(r => r.Classification == "affected").Select(r => r.Repo).ToList();
            if (!affected22.Contains("billing") || !affected22.Contains("shipping"))
                throw new Exception($"swap-plan affected set wrong: {string.Join(",", affected22)} (billing pins 1.0.0, shipping observes the package)");
            if (affected22.Contains("cleanrepo")) throw new Exception("cleanrepo does not reference Contoso.Payments");
            if (!plan22.Delta.PackageName.Contains("Contoso.Payments")) throw new Exception("delta packageName not the estate spelling");
            Directory.Delete(swap22, true);
            Console.WriteLine("ok   drift-permutation-swap-plan (order-independent bytes; candidate plans the expected set)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL drift-permutation-swap-plan: {ex.Message}"); }

        // SPEC-020 acceptance 1/4/5: F-registry golden bytes, determinism, the pinned boundary table
        try
        {
            var fr = Path.Combine(fixturesRoot, "F-registry");
            var (canonR1, rcR1) = Registry.Run(fr);
            if (rcR1 != 0 || canonR1 is null) throw new Exception($"registry exited {rcR1}");
            if (canonR1 != File.ReadAllText(Path.Combine(fr, "golden", "registry.v1.json"))) throw new Exception("registry.v1 differs from golden");
            var (canonR2, rcR2) = Registry.Run(fr);
            if (rcR2 != 0 || canonR2 != canonR1) throw new Exception("registry not deterministic");
            var docR = JsonDocument.Parse(canonR1).RootElement;
            if (docR.GetProperty("schemaVersion").GetString() != "registry.v1") throw new Exception("schemaVersion wrong");
            var prodsR = docR.GetProperty("producers");
            if (prodsR.GetArrayLength() != 1) throw new Exception($"expected exactly the corelib producer, got {prodsR.GetArrayLength()}");
            var p0 = prodsR[0];
            if (p0.GetProperty("repo").GetString() != "corelib" || p0.GetProperty("packageId").GetString() != "Contoso.Core"
                || p0.GetProperty("producedVersion").GetString() != "1.0.0" || p0.GetProperty("provenance").GetString() != "project-declared")
                throw new Exception("corelib producer row wrong (the corpus's only PackageProduced fact): " + p0.GetRawText());
            var bR = docR.GetProperty("boundary");
            List<string> L(string name) => bR.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToList();
            if (!L("internalPackages").SequenceEqual(new[] { "Contoso.Core" })) throw new Exception("internalPackages wrong: " + string.Join(",", L("internalPackages")));
            if (!L("externalPackages").SequenceEqual(new[] { "Newtonsoft.Json" })) throw new Exception("externalPackages wrong: " + string.Join(",", L("externalPackages")));
            if (!L("unknownPackages").SequenceEqual(new[] { "Contoso.Payments", "Serilog" })) throw new Exception("unknownPackages wrong: " + string.Join(",", L("unknownPackages")));
            if (bR.GetProperty("anomalies").GetProperty("producedAndDeclaredExternal").GetArrayLength() != 0) throw new Exception("no anomalies expected in the b1b shape");
            var sumR = docR.GetProperty("provenanceSummary");
            if (sumR.GetProperty("projectDeclared").GetInt32() != 1 || sumR.GetProperty("operatorDeclared").GetInt32() != 0
                || sumR.GetProperty("ciDefined").GetInt32() != 0 || string.IsNullOrEmpty(sumR.GetProperty("note").GetString()))
                throw new Exception("provenanceSummary wrong: " + sumR.GetRawText());
            Console.WriteLine("ok   registry-golden (F-registry bytes + determinism + pinned boundary table)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL registry-golden: {ex.Message}"); }

        // SPEC-020 acceptance 2: operator override — one entry, operator-declared, both provenances named in the warning
        try
        {
            var richO = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich"));
            var sideO = Path.Combine(Path.GetTempPath(), "ua-rg-o-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(sideO);
            File.WriteAllText(Path.Combine(sideO, "producer.json"),
                "{\"schemaVersion\":\"producer-evidence.v0\",\"source\":\"fixture-declared\",\"externalPackages\":[{\"packageId\":\"Newtonsoft.Json\"}],\"producers\":[{\"repo\":\"corelib\",\"packageId\":\"Contoso.Core\",\"producedVersion\":\"2.0.0\"}]}");
            File.WriteAllText(Path.Combine(sideO, "ownership.json"), "{\"schemaVersion\":\"ownership.v0\",\"selfTeamId\":\"team-a\",\"ownerships\":[{\"repo\":\"corelib\",\"team\":\"team-a\"},{\"repo\":\"svc\",\"team\":\"team-a\"}]}");
            File.Copy(Path.Combine(richO, "sidecars-estate2", "delta.json"), Path.Combine(sideO, "delta.json"));
            var outO = Path.Combine(Path.GetTempPath(), "ua-rg-oo-" + Guid.NewGuid().ToString("N")[..8]);
            var errPrevO = Console.Error;
            var swO = new System.IO.StringWriter();
            int rcO;
            try { Console.SetError(swO); rcO = Ingest.Run(new[] { Path.Combine(richO, "scans", "corelib"), Path.Combine(richO, "scans", "svc") }, outO, Path.Combine(sideO, "producer.json"), Path.Combine(sideO, "ownership.json"), Path.Combine(sideO, "delta.json")); }
            finally { Console.SetError(errPrevO); }
            if (rcO != 0) throw new Exception($"ingest exited {rcO}");
            var warnO = swO.ToString();
            if (!warnO.Contains("producer conflict") || !warnO.Contains("operator-declared") || !warnO.Contains("project-declared"))
                throw new Exception("fusion warning must name both provenances (weaker source noted): " + warnO);
            var (canonO, rcO2) = Registry.Run(outO);
            if (rcO2 != 0 || canonO is null) throw new Exception($"registry exited {rcO2}");
            var docO = JsonDocument.Parse(canonO).RootElement;
            var prodsO = docO.GetProperty("producers");
            if (prodsO.GetArrayLength() != 1) throw new Exception($"operator override must yield ONE entry, got {prodsO.GetArrayLength()}");
            var e0 = prodsO[0];
            if (e0.GetProperty("provenance").GetString() != "operator-declared" || e0.GetProperty("producedVersion").GetString() != "2.0.0")
                throw new Exception("sidecar wins with operator-declared: " + e0.GetRawText());
            var sumO = docO.GetProperty("provenanceSummary");
            if (sumO.GetProperty("operatorDeclared").GetInt32() != 1 || sumO.GetProperty("projectDeclared").GetInt32() != 0)
                throw new Exception("provenanceSummary counts wrong after override: " + sumO.GetRawText());
            Directory.Delete(outO, true); Directory.Delete(sideO, true);
            // same-version override + log-injection escape: the loser is noted on EVERY cross-source
            // duplicate (SPEC-020 §2), and sidecar-derived fields never forge log lines (Baz round 1)
            var sideS = Path.Combine(Path.GetTempPath(), "ua-rg-s-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(sideS);
            File.WriteAllText(Path.Combine(sideS, "producer.json"),
                "{\"schemaVersion\":\"producer-evidence.v0\",\"source\":\"fixture-declared\",\"externalPackages\":[],\"producers\":[{\"repo\":\"corelib\",\"packageId\":\"Contoso.Core\",\"producedVersion\":\"1.0.0\",\"provenance\":\"operator-declared\\nFAKE-LOG-LINE\"}]}");
            File.Copy(Path.Combine(richO, "sidecars-estate2", "delta.json"), Path.Combine(sideS, "delta.json"));
            File.WriteAllText(Path.Combine(sideS, "ownership.json"), "{\"schemaVersion\":\"ownership.v0\",\"selfTeamId\":\"team-a\",\"ownerships\":[{\"repo\":\"corelib\",\"team\":\"team-a\"},{\"repo\":\"svc\",\"team\":\"team-a\"}]}");
            var outS = Path.Combine(Path.GetTempPath(), "ua-rg-so-" + Guid.NewGuid().ToString("N")[..8]);
            var errPrevS = Console.Error;
            var swS = new System.IO.StringWriter();
            int rcS;
            try { Console.SetError(swS); rcS = Ingest.Run(new[] { Path.Combine(richO, "scans", "corelib"), Path.Combine(richO, "scans", "svc") }, outS, Path.Combine(sideS, "producer.json"), Path.Combine(sideS, "ownership.json"), Path.Combine(sideS, "delta.json")); }
            finally { Console.SetError(errPrevS); }
            if (rcS != 0) throw new Exception($"same-version ingest exited {rcS}");
            var warnS = swS.ToString();
            if (!warnS.Contains("producer conflict") || !warnS.Contains("sidecar '1.0.0'") || !warnS.Contains("vs scan '1.0.0'"))
                throw new Exception("same-version override must still note the losing source: " + warnS);
            if (!warnS.Contains("\\u000aFAKE-LOG-LINE")) throw new Exception("CR/LF in sidecar fields must be escaped in warnings (never raw): " + warnS);
            if (Registry.Run(outS).Item2 != 3) throw new Exception("poisoned provenance must be refused by the registry (exit 3)");
            Directory.Delete(outS, true); Directory.Delete(sideS, true);
            // stamping with NO scan producers (Codex round 1): a producer-carrying sidecar still gets
            // provenance recorded at fusion — svc carries no PackageProduced facts
            var sideN = Path.Combine(Path.GetTempPath(), "ua-rg-n-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(sideN);
            File.WriteAllText(Path.Combine(sideN, "producer.json"),
                "{\"schemaVersion\":\"producer-evidence.v0\",\"source\":\"fixture-declared\",\"externalPackages\":[],\"producers\":[{\"repo\":\"svc\",\"packageId\":\"Contoso.Core\",\"producedVersion\":\"1.0.0\"}]}");
            File.Copy(Path.Combine(richO, "sidecars-estate2", "delta.json"), Path.Combine(sideN, "delta.json"));
            File.WriteAllText(Path.Combine(sideN, "ownership.json"), "{\"schemaVersion\":\"ownership.v0\",\"selfTeamId\":\"team-a\",\"ownerships\":[{\"repo\":\"svc\",\"team\":\"team-a\"}]}");
            var outN = Path.Combine(Path.GetTempPath(), "ua-rg-no-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(new[] { Path.Combine(richO, "scans", "svc") }, outN, Path.Combine(sideN, "producer.json"), Path.Combine(sideN, "ownership.json"), Path.Combine(sideN, "delta.json")) != 0)
                throw new Exception("no-producer-facts ingest failed");
            var prodN = JsonDocument.Parse(File.ReadAllText(Path.Combine(outN, "input", "producer-evidence.v0.json"))).RootElement.GetProperty("producers");
            if (prodN.GetArrayLength() != 1 || prodN[0].GetProperty("provenance").GetString() != "operator-declared")
                throw new Exception("sidecar entries must be stamped even when the scans found no producers: " + prodN.GetRawText());
            Directory.Delete(outN, true); Directory.Delete(sideN, true);
            Console.WriteLine("ok   registry-operator-override (one entry, operator-declared, warning names both provenances)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL registry-operator-override: {ex.Message}"); }

        // SPEC-020 finding 20-2: produced∩declared-external is SURFACED, never silently resolved
        try
        {
            var frA = Path.Combine(fixturesRoot, "F-registry");
            var scratchA = Path.Combine(Path.GetTempPath(), "ua-rg-a-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(frA, scratchA);
            var peAPath = Path.Combine(scratchA, "input", "producer-evidence.v0.json");
            var peA = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(peAPath))!;
            peA["externalPackages"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("{\"packageId\":\"Contoso.Core\"}")!);
            File.WriteAllText(peAPath, peA.ToJsonString());
            var (canonA, rcA) = Registry.Run(scratchA);
            if (rcA != 0) throw new Exception($"anomaly run exited {rcA} — anomalies are report content, not failures");
            var docA = JsonDocument.Parse(canonA!).RootElement;
            var bA = docA.GetProperty("boundary");
            var anomalyA = bA.GetProperty("anomalies").GetProperty("producedAndDeclaredExternal").EnumerateArray().Select(x => x.GetString()).ToList();
            if (!anomalyA.SequenceEqual(new[] { "Contoso.Core" })) throw new Exception("anomaly must surface Contoso.Core: " + string.Join(",", anomalyA));
            List<string> LA(string name) => bA.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToList();
            if (!LA("internalPackages").Contains("Contoso.Core")) throw new Exception("produced package must stay internal");
            if (LA("externalPackages").Any(x => x.Equals("Contoso.Core", StringComparison.OrdinalIgnoreCase))) throw new Exception("external = declared AND NOT produced — the overlap must not land there");
            if (!LA("externalPackages").SequenceEqual(new[] { "Newtonsoft.Json" })) throw new Exception("externalPackages wrong under anomaly: " + string.Join(",", LA("externalPackages")));
            Directory.Delete(scratchA, true);
            Console.WriteLine("ok   registry-anomaly (produced∩external surfaced in anomalies, lists stay disjoint)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL registry-anomaly: {ex.Message}"); }

        // SPEC-020 finding 20-1/20-3: marker fallback, case-insensitive identity, null-version rows, unconsumed echoes
        try
        {
            var frI = Path.Combine(fixturesRoot, "F-registry");
            string ScratchPe(string name)
            {
                var s = Path.Combine(Path.GetTempPath(), "ua-rg-i-" + name + "-" + Guid.NewGuid().ToString("N")[..8]);
                CopyDir(frI, s);
                return s;
            }
            // pre-spec entry, marker present ⇒ project-declared; marker absent ⇒ operator-declared
            var sM = ScratchPe("marker");
            var peM = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(sM, "input", "producer-evidence.v0.json")))!;
            peM["producers"]![0]!.AsObject().Remove("provenance");
            File.WriteAllText(Path.Combine(sM, "input", "producer-evidence.v0.json"), peM.ToJsonString());
            var (cM, rcM) = Registry.Run(sM);
            if (rcM != 0 || JsonDocument.Parse(cM!).RootElement.GetProperty("producers")[0].GetProperty("provenance").GetString() != "project-declared")
                throw new Exception("in-band marker must classify a pre-spec entry project-declared");
            peM["producers"]![0]!.AsObject().Remove("evidenceNote");
            File.WriteAllText(Path.Combine(sM, "input", "producer-evidence.v0.json"), peM.ToJsonString());
            var (cM2, rcM2) = Registry.Run(sM);
            if (rcM2 != 0 || JsonDocument.Parse(cM2!).RootElement.GetProperty("producers")[0].GetProperty("provenance").GetString() != "operator-declared")
                throw new Exception("marker-less pre-spec entry must classify operator-declared");
            peM["producers"]![0]!["provenance"] = "banana";
            File.WriteAllText(Path.Combine(sM, "input", "producer-evidence.v0.json"), peM.ToJsonString());
            if (Registry.Run(sM).Item2 != 3) throw new Exception("unknown provenance value must be a typed refusal (exit 3)");
            // ci-defined entries exist ⇒ the availability note switches (never "not yet available" over a nonzero count)
            peM["producers"]![0]!["provenance"] = "ci-defined";
            File.WriteAllText(Path.Combine(sM, "input", "producer-evidence.v0.json"), peM.ToJsonString());
            var (cCi, rcCi) = Registry.Run(sM);
            if (rcCi != 0) throw new Exception($"ci-defined entry must be accepted (vocabulary), exited {rcCi}");
            var sumCi = JsonDocument.Parse(cCi!).RootElement.GetProperty("provenanceSummary");
            if (sumCi.GetProperty("ciDefined").GetInt32() != 1 || sumCi.GetProperty("note").GetString()!.Contains("not yet available"))
                throw new Exception("nonzero ciDefined must carry the present note: " + sumCi.GetRawText());
            Directory.Delete(sM, true);
            // case-insensitive identity: lockfile spelling wins; null-version rows still count; unconsumed externals echo
            var sC = ScratchPe("casing");
            var peC = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(sC, "input", "producer-evidence.v0.json")))!;
            peC["externalPackages"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("{\"packageId\":\"newtonsoft.json\"}")!); // case-variant of the consumed spelling
            peC["externalPackages"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("{\"packageId\":\"Never.Consumed\"}")!); // never consumed — echoed
            File.WriteAllText(Path.Combine(sC, "input", "producer-evidence.v0.json"), peC.ToJsonString());
            var lockC = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(sC, "input", "lockfile-rows.v2.json")))!;
            foreach (var repC in lockC["repos"]!.AsArray())
                if (repC!["repo"]!.GetValue<string>() == "svc")
                    foreach (var rowC in repC["rows"]!.AsArray())
                    {
                        var idC = rowC!["packageId"]!.GetValue<string>();
                        if (idC == "Contoso.Core") rowC["packageId"] = "contoso.core"; // case-variant consumption
                        if (idC == "Serilog") rowC["version"] = null; // null-version row: the packageId is still evidence
                    }
            File.WriteAllText(Path.Combine(sC, "input", "lockfile-rows.v2.json"), lockC.ToJsonString());
            var (cC, rcC) = Registry.Run(sC);
            if (rcC != 0) throw new Exception($"casing run exited {rcC}");
            var bC = JsonDocument.Parse(cC!).RootElement.GetProperty("boundary");
            List<string> LC(string name) => bC.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToList();
            var internalC = LC("internalPackages");
            if (internalC.Count != 1 || internalC[0] != "contoso.core")
                throw new Exception($"case-variant consumption must merge to ONE internal entry with the lockfile spelling: {string.Join(",", internalC)}");
            var externalC = LC("externalPackages");
            if (!externalC.SequenceEqual(new[] { "Never.Consumed", "Newtonsoft.Json" }))
                throw new Exception($"case-variant declaration must merge to the estate spelling + echo the unconsumed: {string.Join(",", externalC)}");
            if (!LC("unknownPackages").Contains("Serilog")) throw new Exception("null-version lockfile row must still count as consumption");
            Directory.Delete(sC, true);
            Console.WriteLine("ok   registry-identity (marker fallback; case-insensitive joins; null-version rows; unconsumed echo)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL registry-identity: {ex.Message}"); }

        // SPEC-020 acceptance 3/5: CLI contract — --out bytes, no BOM, sanitized identical, refusals, permutation
        try
        {
            var exeR = Environment.ProcessPath ?? throw new Exception("ProcessPath unavailable");
            var frX = Path.Combine(fixturesRoot, "F-registry");
            var outX = Path.Combine(Path.GetTempPath(), "ua-rg-out.tmp");
            var psiOutX = new System.Diagnostics.ProcessStartInfo(exeR) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true };
            psiOutX.ArgumentList.Add("registry"); psiOutX.ArgumentList.Add(frX); psiOutX.ArgumentList.Add("--out"); psiOutX.ArgumentList.Add(outX);
            psiOutX.StandardOutputEncoding = System.Text.Encoding.UTF8;
            using (var p = System.Diagnostics.Process.Start(psiOutX)!)
            {
                var so = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(30000);
                if (p.ExitCode != 0 || so.Length != 0) throw new Exception($"registry --out exited {p.ExitCode}, stdout {so.Length} chars");
            }
            var psiCapX = new System.Diagnostics.ProcessStartInfo(exeR) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true };
            psiCapX.ArgumentList.Add("registry"); psiCapX.ArgumentList.Add(frX);
            psiCapX.StandardOutputEncoding = System.Text.Encoding.UTF8;
            string stdoutCapX;
            using (var p = System.Diagnostics.Process.Start(psiCapX)!)
            { stdoutCapX = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(30000); }
            if (!File.ReadAllBytes(outX).SequenceEqual(new System.Text.UTF8Encoding(false).GetBytes(stdoutCapX))) throw new Exception("--out file differs from stdout bytes");
            if (File.ReadAllBytes(outX).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) throw new Exception("--out file has a BOM");
            File.Delete(outX);
            var psiSanX = new System.Diagnostics.ProcessStartInfo(exeR) { RedirectStandardOutput = true, UseShellExecute = false, RedirectStandardError = true };
            psiSanX.ArgumentList.Add("registry"); psiSanX.ArgumentList.Add(frX); psiSanX.ArgumentList.Add("--sanitized");
            psiSanX.StandardOutputEncoding = System.Text.Encoding.UTF8;
            string stdoutSanX;
            using (var p = System.Diagnostics.Process.Start(psiSanX)!)
            { stdoutSanX = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(30000); }
            if (stdoutSanX != stdoutCapX) throw new Exception("--sanitized registry stdout differs from canonical");
            if (Main(new[] { "registry", frX, "--out" }) != 1) throw new Exception("bare trailing --out must exit 1");
            if (Main(new[] { "registry", frX, "-o" }) != 1) throw new Exception("bare trailing -o must exit 1");
            // delta = loader precondition (same as drift); missing input ⇒ typed 3
            var sX = Path.Combine(Path.GetTempPath(), "ua-rg-x-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(frX, sX);
            File.Delete(Path.Combine(sX, "input", "delta.json"));
            if (Registry.Run(sX).Item2 != 3) throw new Exception("delta-less fixture must exit 3");
            File.Delete(Path.Combine(sX, "input", "producer-evidence.v0.json"));
            if (Registry.Run(sX).Item2 != 3) throw new Exception("missing producer-evidence must exit 3");
            Directory.Delete(sX, true);
            // permutation: reversed input arrays never reach the bytes
            var sP = Path.Combine(Path.GetTempPath(), "ua-rg-p-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(sP, "input"));
            foreach (var f in Directory.GetFiles(Path.Combine(frX, "input"))) File.Copy(f, Path.Combine(sP, "input", Path.GetFileName(f)));
            foreach (var f in Directory.GetFiles(Path.Combine(sP, "input")))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(f));
                File.WriteAllText(f, CanonicalJson(doc.RootElement));
            }
            var (canonP, rcP) = Registry.Run(sP);
            if (rcP != 0 || canonP != File.ReadAllText(Path.Combine(frX, "golden", "registry.v1.json")))
                throw new Exception("registry bytes depend on input array order");
            Directory.Delete(sP, true);
            Console.WriteLine("ok   registry-cli-contract (--out==stdout no BOM; sanitized identical; refusals; permutation)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL registry-cli-contract: {ex.Message}"); }

        // SPEC-019: deps.json evidence + estate refresh + parallel scans
        try
        {
            var ad = Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest", "tracemap-rich", "deps-scans", "appdeps"));
            var side19 = Path.Combine(Path.GetTempPath(), "ua-d19-side-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(side19);
            File.WriteAllText(Path.Combine(side19, "producer.json"), "{\"schemaVersion\":\"producer-evidence.v0\",\"source\":\"fixture-declared\",\"externalPackages\":[{\"packageId\":\"Serilog\"}],\"producers\":[]}");
            File.WriteAllText(Path.Combine(side19, "ownership.json"), "{\"schemaVersion\":\"ownership.v0\",\"selfTeamId\":\"team-a\",\"ownerships\":[{\"repo\":\"repo\",\"team\":\"team-a\"}]}");
            File.WriteAllText(Path.Combine(side19, "delta.json"), "{\"version\":\"package-delta.v1\",\"sourceRepo\":\"https://example.invalid/x.git\",\"sourceCommitSha\":\"0000000000000000000000000000000000000000\",\"changes\":[{\"id\":\"d\",\"packageName\":\"Serilog\",\"ecosystem\":\"nuget\",\"changeType\":\"updated\",\"oldVersion\":\"2.0.0\",\"newVersion\":\"3.0.0\"}]}");
            var out19 = Path.Combine(Path.GetTempPath(), "ua-d19-out-" + Guid.NewGuid().ToString("N")[..8]);
            if (Ingest.Run(new[] { ad }, out19,
                Path.Combine(side19, "producer.json"), Path.Combine(side19, "ownership.json"), Path.Combine(side19, "delta.json")) != 0) throw new Exception("appdeps ingest failed");
            // 19-1: deps.json facts NEVER become consumer facts (the csproj pin stays; nothing under bin/)
            var px19 = JsonDocument.Parse(File.ReadAllText(Path.Combine(out19, "input", "package-evidence.v0.json"))).RootElement;
            var facts19 = px19.GetProperty("facts").EnumerateArray().ToList();
            if (facts19.Any(f => f.GetProperty("path").GetString()!.Contains("bin/"))) throw new Exception("deps.json leaked into the consumer-fact path");
            if (facts19.Count != 1 || facts19[0].GetProperty("path").GetString() != "src/App/App.csproj") throw new Exception("the csproj declaration must be the only consumer fact");
            // v2 rows carry provenance + the verbatim full-form tfm; drift sees evidence "deps.json"
            var v219 = JsonDocument.Parse(File.ReadAllText(Path.Combine(out19, "input", "lockfile-rows.v2.json"))).RootElement;
            var row19 = v219.GetProperty("repos")[0].GetProperty("rows")[0];
            if (row19.GetProperty("tfm").GetString() != ".NETCoreApp,Version=v10.0") throw new Exception("tfm must stay verbatim (full target form)");
            if (row19.GetProperty("provenance").GetProperty("freshness").GetString() != "unknown") throw new Exception("upstream placeholder must pass through untouched");
            File.WriteAllText(Path.Combine(out19, "input", "build-freshness.v0.json"), "{\"schemaVersion\":\"build-freshness.v0\",\"repos\":[{\"repo\":\"repo\",\"freshness\":\"fresh\"}]}");
            File.WriteAllText(Path.Combine(side19, "feed.json"), "{\"schemaVersion\":\"feed-versions.v1\",\"source\":\"operator-provided\",\"packages\":[{\"packageId\":\"Newtonsoft.Json\",\"version\":\"13.0.3\"},{\"packageId\":\"Serilog\",\"version\":\"3.1.1\"}]}");
            var (_, driftCanon19b, rcD19b) = Drift.Run(out19, Path.Combine(side19, "feed.json"), null, null);
            if (rcD19b != 0 || driftCanon19b is null) throw new Exception("drift over deps.json rows failed");
            var inst19 = JsonDocument.Parse(driftCanon19b).RootElement.GetProperty("packages").EnumerateArray()
                .First(pp => pp.GetProperty("packageId").GetString() == "Newtonsoft.Json").GetProperty("installations")[0];
            if (inst19.GetProperty("evidence").GetString() != "deps.json") throw new Exception("drift must label deps.json installations");
            if (inst19.GetProperty("status").GetString() != "current") throw new Exception("13.0.3 vs 13.0.3 must be current");
            Console.WriteLine("ok   deps-real-e2e (real tracemap bytes: consumer-path exclusion, provenance rows, verbatim tfm, drift evidence)"); pass++;
            Directory.Delete(out19, true); Directory.Delete(side19, true);
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL deps-real-e2e: {ex.Message}"); }

        // SPEC-019 §5: freshness gating (fresh ⇒ not-allowed + Q1; stale ⇒ unknown two-sided; absent ⇒ conservative)
        try
        {
            var fd19 = Path.Combine(fixturesRoot, "F-deps");
            var plan19 = LoadEngine(fd19).BuildPlan();
            var r19 = plan19.Repos.First(r => r.Repo == "repo");
            if (r19.Classification != "not-affected" || !r19.Reasons[0].Contains("closure evidenced by build output (deps.json), freshness: fresh"))
                throw new Exception($"fresh deps.json closure must prove not-affected with Q1: {r19.Reasons[0]}");
            var mixed19 = Path.Combine(fixturesRoot, "F-deps-mixed");
            var planM19 = LoadEngine(mixed19).BuildPlan();
            var rM19 = planM19.Repos.First(r => r.Repo == "repo");
            if (rM19.Classification != "not-affected" || !rM19.Reasons[0].Contains("lockfile closure (Newtonsoft.Json direct) and build-resolved closure"))
                throw new Exception("mixed closure must name both evidence kinds");
            if (!planM19.Uncertainty.Findings!.Any(f => f.Subject == "repo version disagreement: Newtonsoft.Json")) throw new Exception("lockfile-vs-deps.json divergence must fire T10");
            foreach (var (mode19, want19) in new[] { ("stale", "build evidence stale: deps.json closure predates the current commit"), ("none", "classification unknown") })
            {
                var sc19 = Path.Combine(Path.GetTempPath(), "ua-d19g-" + mode19 + "-" + Guid.NewGuid().ToString("N")[..8]);
                CopyDir(fd19, sc19);
                File.WriteAllText(Path.Combine(sc19, "input", "build-freshness.v0.json"),
                    $"{{\"schemaVersion\":\"build-freshness.v0\",\"repos\":[{{\"repo\":\"repo\",\"freshness\":\"{mode19}\"}}]}}");
                var p19 = LoadEngine(sc19).BuildPlan();
                var rr19 = p19.Repos.First(r => r.Repo == "repo");
                if (rr19.Classification != "unknown") throw new Exception($"{mode19}: stale/absent build closure must not prove not-affected (got {rr19.Classification})");
                if (!rr19.Reasons.Any(x => x.Contains(want19) || (mode19 == "none" && x.Contains("never not-affected")))) throw new Exception($"{mode19}: reason wrong: {string.Join(" | ", rr19.Reasons)}");
                Directory.Delete(sc19, true);
            }
            // Codex P1 (PR4 r1): a STALE deps.json row carrying the TARGET must classify unknown, never
            // schedule rule-c work and never throw on the filtered provable set
            var scT19 = Path.Combine(Path.GetTempPath(), "ua-d19t-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(fd19, scT19);
            var pxT19 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(scT19, "input", "package-evidence.v0.json")))!;
            pxT19["facts"] = new System.Text.Json.Nodes.JsonArray(); // isolate rule-c: no csproj declaration, deps.json rows only
            File.WriteAllText(Path.Combine(scT19, "input", "package-evidence.v0.json"), pxT19.ToJsonString());
            var dT19 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(scT19, "input", "delta.json")))!;
            dT19["changes"]![0]!["packageName"] = "Newtonsoft.Json"; dT19["changes"]![0]!["oldVersion"] = "13.0.3"; dT19["changes"]![0]!["newVersion"] = "14.0.0";
            File.WriteAllText(Path.Combine(scT19, "input", "delta.json"), dT19.ToJsonString());
            File.WriteAllText(Path.Combine(scT19, "input", "build-freshness.v0.json"), "{\"schemaVersion\":\"build-freshness.v0\",\"repos\":[{\"repo\":\"repo\",\"freshness\":\"stale\"}]}");
            var planT19 = LoadEngine(scT19).BuildPlan();
            var rT19 = planT19.Repos.First(r => r.Repo == "repo");
            if (rT19.Classification != "unknown" || planT19.Waves.SelectMany(w => w.ReleaseUnits).Any(u => u.Repo == "repo"))
                throw new Exception($"stale target row: {rT19.Classification} + scheduled — must be unknown, never scheduled, never a crash");
            Directory.Delete(scT19, true);
            // C17 (PR4 r5): a stale deps.json row mentioning the target must NOT demote a repo whose
            // independent lockfile closure proves cleanliness (spec 19-5: lockfile stands; stale demotes build-only)
            var scL19 = Path.Combine(Path.GetTempPath(), "ua-d19l-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(mixed19, scL19);
            var pxL19 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(scL19, "input", "package-evidence.v0.json")))!;
            pxL19["facts"] = new System.Text.Json.Nodes.JsonArray(); // isolate closure semantics: no consumer declarations, rows only
            File.WriteAllText(Path.Combine(scL19, "input", "package-evidence.v0.json"), pxL19.ToJsonString());
            var dLock19 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(scL19, "input", "delta.json")))!;
            dLock19["changes"]![0]!["packageName"] = "Serilog";
            File.WriteAllText(Path.Combine(scL19, "input", "delta.json"), dLock19.ToJsonString());
            // mixed19's repo: lockfile row Newtonsoft 12.0.3 + deps.json row 13.0.3; make deps STALE, delta targets Newtonsoft
            var v2L19 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(scL19, "input", "lockfile-rows.v2.json")))!;
            foreach (var row in v2L19["repos"]![0]!["rows"]!.AsArray())
                if (row!["provenance"] is not null) row["packageId"] = "Newtonsoft.Json"; // ensure the deps row names the target (it already does; explicit)
            File.WriteAllText(Path.Combine(scL19, "input", "lockfile-rows.v2.json"), v2L19.ToJsonString());
            File.WriteAllText(Path.Combine(scL19, "input", "build-freshness.v0.json"), "{\"schemaVersion\":\"build-freshness.v0\",\"repos\":[{\"repo\":\"repo\",\"freshness\":\"stale\"}]}");
            var dNL19 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(scL19, "input", "delta.json")))!;
            dNL19["changes"]![0]!["packageName"] = "Newtonsoft.Json"; dNL19["changes"]![0]!["oldVersion"] = "12.0.3"; dNL19["changes"]![0]!["newVersion"] = "13.0.3";
            File.WriteAllText(Path.Combine(scL19, "input", "delta.json"), dNL19.ToJsonString());
            // (a lockfile row naming the target with a direct relation is rule-c affected — expected, not under test here)
            // now a target the LOCKFILE does not name but the STALE deps row does: lockfile closure must stand
            var v2L19b = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(scL19, "input", "lockfile-rows.v2.json")))!;
            foreach (var row in v2L19b["repos"]![0]!["rows"]!.AsArray())
                if (row!["provenance"] is null) row["packageId"] = "Serilog"; // lockfile names Serilog (not the target)
            File.WriteAllText(Path.Combine(scL19, "input", "lockfile-rows.v2.json"), v2L19b.ToJsonString());
            var pL19b = LoadEngine(scL19).BuildPlan();
            var rL19b = pL19b.Repos.First(r => r.Repo == "repo");
            if (rL19b.Classification != "not-affected") throw new Exception($"independent lockfile closure must stand despite the stale deps row naming the target — got {rL19b.Classification}: {string.Join(" | ", rL19b.Reasons)}");
            Directory.Delete(scL19, true);
            var scAbs19 = Path.Combine(Path.GetTempPath(), "ua-d19a-" + Guid.NewGuid().ToString("N")[..8]);
            CopyDir(fd19, scAbs19);
            File.Delete(Path.Combine(scAbs19, "input", "build-freshness.v0.json"));
            var pAbs19 = LoadEngine(scAbs19).BuildPlan();
            if (pAbs19.Repos.First(r => r.Repo == "repo").Classification != "unknown") throw new Exception("absent freshness file must be conservative");
            File.WriteAllText(Path.Combine(scAbs19, "input", "build-freshness.v0.json"), "{\"schemaVersion\":\"build-freshness.v0\",\"repos\":[{\"repo\":\"repo\",\"freshness\":\"bogus\"}]}");
            try { LoadEngine(scAbs19).BuildPlan(); throw new Exception("bad freshness value must refuse at load"); }
            catch (UaException ex19) { if (!ex19.Message.Contains("not in fresh|stale|none|fresh-by-build")) throw new Exception("wrong refusal message"); }
            Directory.Delete(scAbs19, true);
            Console.WriteLine("ok   deps-freshness-gating (Q1 fresh; stale two-sided; absent conservative; mixed names both)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL deps-freshness-gating: {ex.Message}"); }

        // SPEC-019 §3.1 + §6: --update-checkouts rescue; fingerprint cache; indexDepsJson toggle
        try
        {
            var root20 = Path.Combine(Path.GetTempPath(), "ua-d19r-" + Guid.NewGuid().ToString("N")[..8]);
            var out20 = Path.Combine(Path.GetTempPath(), "ua-d19r-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root20, "repos"));
            var alpha20 = EstateRepo(root20, "alpha");
            // behind + dirty: rescue branch + commit, then trunk
            File.WriteAllText(Path.Combine(alpha20, "stray.txt"), "wip");
            Push.Git(alpha20, "commit -qm ahead --allow-empty", out _, out _);
            Push.Git(alpha20, "push -q origin main", out _, out _);
            Push.Git(alpha20, "reset -q --hard HEAD~1", out _, out _);
            if (ScanEstate.Run(Path.Combine(root20, "repos"), out20, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner,
                updateCheckouts: true) != 0) throw new Exception("update-checkouts run failed");
            Push.Git(alpha20, "branch --list ua/rescue-*", out var rescue20, out _);
            if (rescue20.Trim().Length == 0) throw new Exception("rescue branch not created");
            Push.Git(alpha20, "rev-parse main", out var local20, out _);
            Push.Git(alpha20, "rev-parse origin/main", out var remote20, out _);
            if (local20.Trim() != remote20.Trim()) throw new Exception("trunk not fast-forwarded");
            // local-ahead: refuses, never merges
            Push.Git(alpha20, "commit -qm localwork --allow-empty", out _, out _);
            var out20b = Path.Combine(Path.GetTempPath(), "ua-d19r-out2-" + Guid.NewGuid().ToString("N")[..8]);
            if (ScanEstate.Run(Path.Combine(root20, "repos"), out20b, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner,
                updateCheckouts: true) != 1) throw new Exception("local-ahead estate (single repo) must exit 1");
            using (var m20 = SEManifest(out20b))
                if (!m20.RootElement.GetProperty("repos")[0].GetProperty("reason").GetString()!.Contains("local commits ahead"))
                    throw new Exception("local-ahead must refuse-with-reason, never merge");
            // fingerprint cache: rebuild (fake bin deps.json, mtime bump) with same HEAD => exactly that repo rescans
            Push.Git(alpha20, "reset -q --hard origin/main", out _, out _);
            var out20c = Path.Combine(Path.GetTempPath(), "ua-d19r-out3-" + Guid.NewGuid().ToString("N")[..8]);
            int Count20(ScanEstate.ScanRequest r) => StubScanner(r);
            if (ScanEstate.Run(Path.Combine(root20, "repos"), out20c, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count20,
                indexDepsJson: true) != 0) throw new Exception("indexDepsJson seed failed"); // no bin/ => fingerprint null, freshness none
            var calls20 = 0;
            int Count20b(ScanEstate.ScanRequest r) { System.Threading.Interlocked.Increment(ref calls20); return StubScanner(r); }
            if (ScanEstate.Run(Path.Combine(root20, "repos"), out20c, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count20b,
                indexDepsJson: true) != 0) throw new Exception("rerun failed");
            if (calls20 != 0) throw new Exception("no-change rerun must reuse");
            var fakeBin = Path.Combine(alpha20, "bin", "Release", "net10.0");
            Directory.CreateDirectory(fakeBin);
            File.WriteAllText(Path.Combine(fakeBin, "alpha.deps.json"), "{}");
            File.WriteAllText(Path.Combine(alpha20, ".git", "info", "exclude"), "bin/\n"); // real repos ignore bin/ — an untracked build dir is not dirt
            System.Threading.Thread.Sleep(20); // distinct mtime
            var calls20c = 0;
            int Count20c(ScanEstate.ScanRequest r) { System.Threading.Interlocked.Increment(ref calls20c); return StubScanner(r); }
                        if (ScanEstate.Run(Path.Combine(root20, "repos"), out20c, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count20c,
                indexDepsJson: true) != 0) throw new Exception("post-bin run failed");
            if (calls20c != 1) throw new Exception($"new build output must change the fingerprint and force a rescan (calls={calls20c})");
            var calls20d = 0;
            int Count20d(ScanEstate.ScanRequest r) { System.Threading.Interlocked.Increment(ref calls20d); return StubScanner(r); }
            if (ScanEstate.Run(Path.Combine(root20, "repos"), out20c, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", Count20d,
                indexDepsJson: false) != 0) throw new Exception("toggle run failed");
            if (calls20d != 1) throw new Exception("toggling --index-deps-json must rescan (scan condition)");
            ForceDelete(root20); ForceDelete(out20); ForceDelete(out20b); ForceDelete(out20c);
            Console.WriteLine("ok   estate-refresh-cache (rescue+ff; local-ahead refuses; fingerprint + condition toggles)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL estate-refresh-cache: {ex.Message}"); }

        // SPEC-019 §3.2: worktree mode — checkout untouched, namespace isolation, foreign survivors, parallel determinism
        try
        {
            var root21 = Path.Combine(Path.GetTempPath(), "ua-d19w-" + Guid.NewGuid().ToString("N")[..8]);
            var out21 = Path.Combine(Path.GetTempPath(), "ua-d19w-out-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Path.Combine(root21, "repos"));
            var beta21 = EstateRepo(root21, "beta");
            File.WriteAllText(Path.Combine(beta21, "stray.txt"), "wip"); // dirty + behind: no problem in worktree mode
            Push.Git(beta21, "commit -qm ahead --allow-empty", out _, out _);
            Push.Git(beta21, "push -q origin main", out _, out _);
            Push.Git(beta21, "reset -q --hard HEAD~1", out _, out _);
            Push.Git(beta21, "rev-parse HEAD", out var headBefore21, out _);
            Push.Git(beta21, "status --porcelain", out var statusBefore21, out _);
            Push.Git(beta21, "rev-parse --abbrev-ref HEAD", out var branchBefore21, out _);
            var wtRoot21 = Path.Combine(root21, "shared-worktrees");
            // a foreign worktree beside our namespace must SURVIVE
            var foreign21 = Path.Combine(wtRoot21, "codex-session-1");
            Directory.CreateDirectory(foreign21);
            File.WriteAllText(Path.Combine(foreign21, "session.txt"), "an agent lives here");
            if (ScanEstate.Run(Path.Combine(root21, "repos"), out21, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner,
                worktreeMode: true, worktreeRoot: wtRoot21) != 0) throw new Exception("worktree run failed");
            Push.Git(beta21, "rev-parse HEAD", out var headAfter21, out _);
            Push.Git(beta21, "status --porcelain", out var statusAfter21, out _);
            Push.Git(beta21, "rev-parse --abbrev-ref HEAD", out var branchAfter21, out _);
            if (headAfter21.Trim() != headBefore21.Trim() || statusAfter21 != statusBefore21 || branchAfter21.Trim() != branchBefore21.Trim())
                throw new Exception("operator checkout was touched (HEAD/branch/status must be byte-identical)");
            if (!File.Exists(Path.Combine(foreign21, "session.txt"))) throw new Exception("foreign worktree did not survive the run");
            var uaNs21 = Path.Combine(wtRoot21, "ua");
            if (Directory.Exists(uaNs21) && Directory.GetDirectories(uaNs21).Length > 0) throw new Exception("worktree satellites not cleaned up");
            using (var m21 = SEManifest(out21))
                if (m21.RootElement.GetProperty("repos")[0].GetProperty("status").GetString() != "scanned") throw new Exception("worktree scan must succeed for a behind+dirty checkout");
            // parallel determinism: two repos, N=4 == sequential bytes
            EstateRepo(root21, "alpha");
            var outSeq = Path.Combine(Path.GetTempPath(), "ua-d19w-seq-" + Guid.NewGuid().ToString("N")[..8]);
            var outPar = Path.Combine(Path.GetTempPath(), "ua-d19w-par-" + Guid.NewGuid().ToString("N")[..8]);
            // clean beta's behind state for a comparable run: worktree mode ignores it anyway; alpha is fresh
            if (ScanEstate.Run(Path.Combine(root21, "repos"), outSeq, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner,
                worktreeMode: true, worktreeRoot: wtRoot21, parallel: 1) != 0) throw new Exception("sequential run failed");
            if (ScanEstate.Run(Path.Combine(root21, "repos"), outPar, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner,
                worktreeMode: true, worktreeRoot: wtRoot21, parallel: 4) != 0) throw new Exception("parallel run failed");
            if (File.ReadAllText(Path.Combine(outSeq, "scan-estate.v1.json")) != File.ReadAllText(Path.Combine(outPar, "scan-estate.v1.json")))
                throw new Exception("manifest bytes depend on --parallel");
            if (File.ReadAllText(Path.Combine(outSeq, "plan.json")) != File.ReadAllText(Path.Combine(outPar, "plan.json")))
                throw new Exception("plan bytes depend on --parallel");
            // trunk override + invalid parallel
            if (ScanEstate.Run(Path.Combine(root21, "repos"), outPar, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner,
                worktreeMode: true, worktreeRoot: wtRoot21, trunkOverride: "nosuchbranch", parallel: 4) != 1) throw new Exception("missing --trunk branch must exit 1 (all repos skipped)");
            if (ScanEstate.Run(Path.Combine(root21, "repos"), outPar, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner, parallel: 0) != 1
                || ScanEstate.Run(Path.Combine(root21, "repos"), outPar, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner, parallel: 99) != 1)
                throw new Exception("--parallel bounds must be typed errors");
            if (ScanEstate.Run(Path.Combine(root21, "repos"), outPar, null, null, "team-a", false, Array.Empty<string>(), "P", "1", "2", StubScanner,
                updateCheckouts: true, worktreeMode: true) != 1) throw new Exception("both refresh modes must refuse");
            ForceDelete(root21); ForceDelete(out21); ForceDelete(outSeq); ForceDelete(outPar);
            Console.WriteLine("ok   estate-worktree-parallel (checkout untouched; namespace isolation; foreign survives; N-independent bytes)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL estate-worktree-parallel: {ex.Message}"); }

        // working-tree EOL guard: a CRLF checkout (core.autocrlf=true on Windows) converts unpinned
        // text files and silently breaks byte-exact apply goldens (seen on a real work machine: apply-F13).
        // Checks tracked files (the checkout); falls back to a walk that skips build-output dirs when no git.
        try
        {
            var crlf = new List<string>();
            var roots = new[] { fixturesRoot, Path.GetFullPath(Path.Combine(fixturesRoot, "..", "testdata-ingest")) };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (var f in SelfTestTreeFiles(root))
                    if (File.ReadAllText(f).Contains('\r')) crlf.Add(f);
            }
            if (crlf.Count > 0)
            {
                var sample = string.Join(", ", crlf.Take(3).Select(Path.GetFileName)) + (crlf.Count > 3 ? $" (+{crlf.Count - 3} more)" : "");
                throw new Exception($"{sample}: CRLF in working tree — a CRLF checkout (git core.autocrlf) breaks byte-exact goldens; fix: git config core.autocrlf false; git rm -r --cached .; git reset --hard");
            }
            Console.WriteLine("ok   crlf-guard (fixtures + testdata working tree is LF-clean)"); pass++;
        }
        catch (Exception ex) { fail++; Console.WriteLine($"FAIL crlf-guard: {ex.Message}"); }

        Console.WriteLine(fail == 0 ? $"SELFTEST PASS ({pass} cases)" : $"SELFTEST FAIL ({fail} failing)");
        return fail == 0 ? 0 : 1;
    }

    // deterministically "unsort" arrays (reverse) to simulate shuffled inputs
    static string CanonicalJson(JsonElement e) => Rewrite(e).ToString();
    static JsonNode Rewrite(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var obj = new JsonObject();
                foreach (var p in e.EnumerateObject()) obj[p.Name] = Rewrite(p.Value);
                return obj;
            }
            case JsonValueKind.Array:
            {
                var arr = new JsonArray();
                var items = e.EnumerateArray().ToList();
                items.Reverse(); // deterministic shuffle stand-in
                foreach (var i in items) arr.Add(Rewrite(i));
                return arr;
            }
            default: return JsonNode.Parse(e.GetRawText())!;
        }
    }

    static void DumpDiff(string golden, string actual)
    {
        var g = golden.Split('\n'); var a = actual.Split('\n');
        int shown = 0;
        for (int i = 0; i < Math.Max(g.Length, a.Length) && shown < 12; i++)
        {
            var gl = i < g.Length ? g[i] : "<missing>";
            var al = i < a.Length ? a[i] : "<missing>";
            if (gl != al) { Console.WriteLine($"  line {i + 1}:\n    golden: {gl.TrimEnd()}\n    actual: {al.TrimEnd()}"); shown++; }
        }
        if (shown == 0 && golden != actual) Console.WriteLine("  (length/trailing-byte difference)");
    }
}

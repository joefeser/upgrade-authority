using System.Diagnostics;
using System.Text;

namespace UpgradeAuthority;

// SPEC-010: ua push — apply execution. Branch per wave from --base, verify+patch INSIDE a temporary
// worktree of that branch (verification always targets the tree being committed), commit with the
// wave's gates verbatim, optional GitHub PR (--pr). Never merges, never force-updates; every refusal
// rolls back to the pre-invocation state. Local-only by default (offline-first).
public static class Push
{
    public const int RefusalExit = 7;

    public sealed class PushReport
    {
        public string SchemaVersion = "push.v1";
        public string Mode = ""; // "dry-run" | "local" | "pr"
        public List<ReportWave> Waves = new();      // deterministic — byte-exact goldens
        public List<Observed> Observations = new(); // shas/urls/failures — asserted structurally, never goldened
    }
    public sealed class ReportWave
    {
        public int Index; public string Status = ""; public string Branch = ""; public string Base = "";
        public string CommitSubject = ""; public List<string> CommitBody = new();
        public string PrTitle = ""; public List<string> PrBody = new(); public List<string> Commands = new();
        public int Edits;
    }
    public sealed class Observed { public string Wave = ""; public string? CommitSha; public string? PrUrl; public string? Failure; }

    public static int Run(string fixtureDir, string? repoDir, string? baseBranch, string outDir, bool dryRun, bool openPr, string? githubApi = null)
    {
        if (!dryRun && repoDir is null) { Console.Error.WriteLine("error: --repo <checkout> is required for execution (dry-run-only is --dry-run)"); return 1; }
        if (repoDir is not null && baseBranch is null) { Console.Error.WriteLine("error: --base <branch> is required with --repo — the PR target is never guessed"); return 1; }
        // SPEC-013: API base — flag > GITHUB_API_URL > https://api.github.com. Validation is PR-scoped:
        // local-only runs never touch the API and ignore a stray env value entirely.
        var apiBase = (githubApi ?? Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com").TrimEnd('/');
        var isDefaultBase = string.Equals(apiBase, "https://api.github.com", StringComparison.OrdinalIgnoreCase);
        if (openPr)
        {
            // userinfo FIRST and never echoed back (the base may contain the credential being rejected)
            if (System.Text.RegularExpressions.Regex.IsMatch(apiBase, @"^[a-z]+://[^/@]+@", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            { Console.Error.WriteLine("error: --github-api must not carry credentials — the base is written into push.v1.json"); return RefusalExit; }
            var loopback = System.Text.RegularExpressions.Regex.IsMatch(apiBase, @"^http://(127\.0\.0\.1|localhost)(:\d+)?($|/)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if ((!apiBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !loopback)
                || apiBase.Contains('?') || apiBase.Contains('#'))
            { Console.Error.WriteLine("error: --github-api must be a clean https base (no query/fragment; loopback http is the testing carve-out) — GITHUB_TOKEN must never ride a plaintext or rewritten transport"); return RefusalExit; }
            if (!isDefaultBase && !apiBase.Contains("/api/", StringComparison.Ordinal))
                Console.Error.WriteLine($"note: --github-api base '{apiBase}' does not contain /api/ — using it verbatim (no layout guessing)");
            try
            {
                _ = new Uri(apiBase, UriKind.Absolute); // full well-formedness (':invalid' ports fail here) BEFORE any repo mutation
            }
            catch (UriFormatException)
            {
                Console.Error.WriteLine("error: --github-api is not a well-formed absolute https base (checked before any repository mutation)");
                return RefusalExit;
            }
        }

        if (openPr && !dryRun)
        {
            if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is null or "")
            { Console.Error.WriteLine("error: --pr requires GITHUB_TOKEN to be set"); return RefusalExit; }
            if (repoDir is not null)
            {
                if (Git(repoDir, "config --get remote.origin.url", out var remote0, out _) != 0) // raw URL: insteadOf rewrites must not confuse validation
                { Console.Error.WriteLine("error: --pr requires an 'origin' remote — none configured"); return RefusalExit; }
                var origin = remote0.Trim();
                var ownerRepo = ParseOwnerRepo(origin);
                var okOrigin = isDefaultBase
                    ? System.Text.RegularExpressions.Regex.IsMatch(origin, @"github\.com[:/]", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                    : ownerRepo is not null; // overridden base: any forge; the base names it
                if (!okOrigin)
                { Console.Error.WriteLine(isDefaultBase
                    ? $"error: --pr targets github.com only by default — origin is '{origin}' (pass --github-api for another forge)"
                    : $"error: cannot parse owner/repo from origin '{origin}' (expected https://host/owner/repo(.git) or git@host:owner/repo(.git))"); return RefusalExit; }
                if (!isDefaultBase && ownerRepo is not null)
                {
                    var originHost = ExtractHost(origin);
                    var baseHost = System.Text.RegularExpressions.Regex.Match(apiBase, @"^[a-z]+://(?<h>[^/]+)").Groups["h"].Value.Split(':')[0];
                    if (!string.Equals(originHost, baseHost, StringComparison.OrdinalIgnoreCase))
                        Console.Error.WriteLine($"note: origin host '{originHost}' differs from API base host '{baseHost}' — proceeding (proxied/mirrored remotes are legal)");
                }
            }
        }

        var engine = Program.LoadEngine(fixtureDir);
        var plan = engine.BuildPlan();
        var manifest = Apply.BuildManifest(engine, plan);
        var repo = manifest.Waves.SelectMany(w => w.Units).Where(u => u.Edits is { Count: > 0 }).Select(u => u.Repo).Distinct().ToList();
        if (repo.Count > 1)
        { Console.Error.WriteLine($"error: manifest carries edits for {repo.Count} repos ({string.Join(", ", repo)}) — ua push executes ONE checkout per run"); return 1; }

        var mode = dryRun ? "dry-run" : openPr ? "pr" : "local";
        var report = new PushReport { Mode = mode };

        // git availability: only when a checkout is involved (SPEC-010 §2)
        if (repoDir is not null)
        {
            var gitOk = Git(repoDir!, "--version", out _, out var gitErr);
            if (gitOk != 0) { Console.Error.WriteLine($"error: git CLI not available on PATH ({gitErr}) — required for checkout-backed runs"); return RefusalExit; }
        }

        // execution preconditions (checked, not mutated, when dry-run with --repo)
        if (repoDir is not null)
        {
            if (!PreconditionsHold(repoDir!, baseBranch!, manifest, out var failure))
            {
                Console.Error.WriteLine($"error: {failure}");
                if (!dryRun) return RefusalExit;
                report.Observations.Add(new Observed { Wave = "*", Failure = $"precondition (not mutated, dry-run): {failure}" });
            }
        }

        foreach (var w in manifest.Waves)
        {
            var edits = w.Units.Sum(u => u.Edits?.Count ?? 0);
            var rw = BuildReportWave(plan, manifest, w, edits, baseBranch ?? "<base>", openPr, apiBase);
            report.Waves.Add(rw);
            if (edits == 0) continue;

            if (dryRun) continue; // deterministic section only — no git, no mutation

            // ---- execution: branch -> worktree -> verify+patch -> commit, with rollback ----
            var worktree = Path.Combine(Path.GetTempPath(), "ua-push-" + Guid.NewGuid().ToString("N")[..8]);
            var patchDir = Path.Combine(Path.GetTempPath(), "ua-push-patch-" + Guid.NewGuid().ToString("N")[..8]);
            var committed = false; // once true, NEVER roll back: the commit stands even if push/PR fail later (PR16 R2)
            try
            {
                if (Git(repoDir!, $"branch {rw.Branch} {baseBranch}", out _, out var be) != 0)
                { Console.Error.WriteLine($"error: branch creation failed: {be}"); return Fail(report, w.Index, "branch", outDir); }
                if (Git(repoDir!, $"worktree add \"{worktree}\" {rw.Branch}", out _, out var we) != 0) // NOT detached: the worktree holds the branch checked out, so commits update the branch ref
                { Rollback(repoDir!, rw.Branch, worktree); Console.Error.WriteLine($"error: worktree creation failed: {we}"); return Fail(report, w.Index, "worktree", outDir); }

                // verification + patch against the WORKTREE (the tree being committed) — SPEC-010 §3.3
                Directory.CreateDirectory(patchDir);
                var rc = Apply.WriteVerifiedPatches(manifest, worktree, patchDir, manifest.Delta.PackageName);
                if (rc != 0) { Rollback(repoDir!, rw.Branch, worktree); return rc; } // apply-semantics exit (6) — a verification refusal is an apply refusal wherever it happens
                var patchFile = Path.Combine(patchDir, $"wave-{w.Index}.patch");
                if (File.Exists(patchFile) && new FileInfo(patchFile).Length > 0)
                {
                    if (Git(worktree, $"apply \"{patchFile}\"", out _, out var ae) != 0)
                    { Rollback(repoDir!, rw.Branch, worktree); Console.Error.WriteLine($"error: patch application failed in worktree: {ae}"); return Fail(report, w.Index, "patch", outDir); }
                    foreach (var pf in PatchFiles(patchFile)) Git(worktree, $"add \"{pf}\"", out _, out _); // stage EXACTLY the verified files — hooks cannot smuggle bytes into the commit
                }

                var msgPath = Path.Combine(patchDir, "msg.txt");
                File.WriteAllText(msgPath, rw.CommitSubject + "\n\n" + string.Join("\n", rw.CommitBody) + "\n");
                if (Git(worktree, $"commit --file \"{msgPath}\"", out var co, out var ce) != 0)
                { Rollback(repoDir!, rw.Branch, worktree); Console.Error.WriteLine($"error: commit failed (rolled back — branch deleted, worktree removed): {ce}"); return Fail(report, w.Index, "commit", outDir); }
                Git(repoDir!, $"worktree remove --force {worktree}", out _, out _);
                Git(repoDir!, $"rev-parse {rw.Branch}", out var sha, out _);
                committed = true;
                report.Observations.Add(new Observed { Wave = rw.Branch, CommitSha = sha.Trim() });
                Directory.Delete(patchDir, true);

                if (openPr)
                {
                    if (Git(repoDir!, $"push -u origin {rw.Branch}", out _, out var pe) != 0)
                    { Console.Error.WriteLine($"error: git push failed — local branch+commit stand; re-run refuses on the existing branch (push it manually or delete it): {pe}"); return Fail(report, w.Index, "push", outDir); }
                    var url = OpenPullRequest(repoDir!, rw, baseBranch!, apiBase).Result;
                    if (url is null) { Console.Error.WriteLine("error: PR creation failed — local branch+commit stand; re-run refuses on the existing branch (push it manually or delete it)"); return Fail(report, w.Index, "pr", outDir); }
                    report.Observations.Add(new Observed { Wave = rw.Branch, PrUrl = url });
                }
            }
            catch (Exception ex)
            {
                if (!committed) Rollback(repoDir!, rw.Branch, worktree); // pre-commit failure: repo left as found
                Console.Error.WriteLine(committed
                    ? $"error: post-commit failure (commit stands on {rw.Branch}): {ex.Message}"
                    : $"error: unexpected failure (rolled back): {ex.Message}");
                return Fail(report, w.Index, committed ? "post-commit" : "unexpected", outDir);
            }
            finally
            {
                if (Directory.Exists(worktree)) { Git(repoDir!, $"worktree remove --force \"{worktree}\"", out _, out _); }
                if (Directory.Exists(patchDir)) Directory.Delete(patchDir, true); // never leak temp patches
            }
        }

        WriteReport(outDir, report);
        Console.Error.WriteLine($"push: {report.Waves.Count} wave(s) [{mode}] -> {outDir}");
        return 0;
    }

    static int Fail(PushReport report, int wave, string stage, string? outDir = null)
    {
        report.Observations.Add(new Observed { Wave = "wave-" + wave, Failure = stage });
        if (outDir is not null) WriteReport(outDir, report); // the report ALWAYS lands — a failed run is still a record (PR16 Q6/C2)
        return RefusalExit;
    }

    static void Rollback(string repoDir, string branch, string worktree)
    {
        Git(repoDir, $"worktree remove --force {worktree}", out _, out _);
        Git(repoDir, $"branch -D {branch}", out _, out _); // only ever deletes THIS invocation's branch (it did not exist at entry — preconditions)
    }

    // owner/repo from https://host[:port]/owner/repo(.git) or scp-style git@host:owner/repo(.git)
    internal static (string Owner, string Repo)? ParseOwnerRepo(string origin)
    {
        var m = System.Text.RegularExpressions.Regex.Match(origin.Trim(), @"^(?:(?:https?|ssh)://[^/]+/|[^@\s]+@[^:]+:)(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?$");
        return m.Success ? (m.Groups["owner"].Value, m.Groups["repo"].Value) : null;
    }
    static string ExtractHost(string origin)
    {
        var m = System.Text.RegularExpressions.Regex.Match(origin.Trim(), @"^(?:https?://(?<h>[^/]+)|[^@\s]+@(?<h>[^:]+):)");
        return m.Success ? m.Groups["h"].Value.Split(':')[0] : "";
    }

    // files touched by a unified diff we generated: the '+++ b/<path>' headers
    static List<string> PatchFiles(string patchFile) =>
        File.ReadLines(patchFile).Where(l => l.StartsWith("+++ b/", StringComparison.Ordinal)).Select(l => l[6..]).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();

    static bool PreconditionsHold(string repoDir, string baseBranch, Apply.ApplyManifest manifest, out string failure)
    {
        failure = "";
        if (Git(repoDir, "status --porcelain", out var status, out _) != 0)
        { failure = $"{repoDir} is not a git work tree"; return false; }
        if (status.Trim().Length > 0) { failure = "working tree is dirty — commit or stash first (ua push never mixes with uncommitted changes)"; return false; }
        if (Git(repoDir, $"rev-parse --verify {baseBranch}", out _, out _) != 0)
        { failure = $"base branch '{baseBranch}' does not exist in the checkout"; return false; }
        foreach (var w in manifest.Waves.Where(w => w.Units.Any(u => u.Edits is { Count: > 0 })))
        {
            var br = $"ua/wave-{w.Index}/{manifest.Delta.PackageName}-{manifest.Delta.OldVersion}-to-{manifest.Delta.NewVersion}";
            if (Git(repoDir, $"rev-parse --verify {br}", out _, out _) == 0)
            { failure = $"branch '{br}' already exists — never force-updated; delete it deliberately or push it manually"; return false; }
        }
        return true;
    }

    static ReportWave BuildReportWave(Plan plan, Apply.ApplyManifest manifest, Apply.ApplyWave w, int edits, string baseBranch, bool openPr, string apiBase)
    {
        var pkg = manifest.Delta.PackageName;
        var branch = $"ua/wave-{w.Index}/{pkg}-{manifest.Delta.OldVersion}-to-{manifest.Delta.NewVersion}";
        var subject = $"ua: {pkg} {manifest.Delta.OldVersion} -> {manifest.Delta.NewVersion} (wave {w.Index})"; // ASCII arrow — tooling-safe subject
        var body = new List<string> { $"wave-status: {w.Status}" };
        if (w.Condition is not null) body.Add($"condition: {w.Condition}");
        if (w.BlockedOn is not null) body.Add($"blocked-on: {w.BlockedOn}");
        body.AddRange(w.Prerequisites.Select(p => $"prerequisite: {p}"));
        body.Add($"evidence: upgrade-authority apply.v1 verified {edits} edit(s)");
        body.Add("generated-by: ua push (SPEC-010)");
        body.Add("");

        var prBody = new List<string>
        {
            $"## Upgrade {pkg} {manifest.Delta.OldVersion} -> {manifest.Delta.NewVersion} (wave {w.Index} of {manifest.Waves.Count})",
            $"- status: {w.Status}",
        };
        if (w.Condition is not null) prBody.Add($"- condition: {w.Condition}");
        if (w.BlockedOn is not null) prBody.Add($"- blocked on: {w.BlockedOn}");
        prBody.AddRange(w.Prerequisites.Select(p => $"- prerequisite: {p}"));
        foreach (var u in w.Units)
            foreach (var e in u.Edits ?? new List<Apply.ApplyEdit>())
                prBody.Add($"- edit: {e.Path} ({e.Kind}, {e.EvidenceKind})");
        foreach (var f in plan.Uncertainty.Findings ?? new List<PlanFinding>())
            prBody.Add($"- finding: {f.Subject} — {f.Detail}");
        prBody.Add("Human merges. Generated by ua push; every edit was evidence-verified against this checkout before commit.");

        var commands = new List<string> { $"git branch {branch} {baseBranch}", $"git worktree add <wt> {branch}", "<verify+patch in worktree>", "git add <patched files>", "git commit --file <msg>" };
        if (openPr) { commands.Add($"git push -u origin {branch}"); commands.Add($"POST {apiBase}/repos/<owner>/<repo>/pulls (head: {branch}, base: {baseBranch})"); }

        return new ReportWave { Index = w.Index, Status = w.Status, Branch = branch, Base = baseBranch, CommitSubject = subject, CommitBody = body, PrTitle = subject, PrBody = prBody, Commands = commands, Edits = edits };
    }

    static async System.Threading.Tasks.Task<string?> OpenPullRequest(string repoDir, ReportWave rw, string baseBranch, string apiBase)
    {
        if (Git(repoDir, "config --get remote.origin.url", out var remote, out _) != 0) return null; // raw URL (see precondition note)
        var or = ParseOwnerRepo(remote);
        if (or is null) return null;
        var owner = or.Value.Owner; var repoName = or.Value.Repo;
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var body = new StringBuilder();
        body.Append('{');
        body.Append("\"title\":").Append(Json(rw.PrTitle)).Append(',');
        body.Append("\"head\":").Append(Json(rw.Branch)).Append(',');
        body.Append("\"base\":").Append(Json(baseBranch)).Append(',');
        body.Append("\"body\":").Append(Json(string.Join("\n", rw.PrBody)));
        body.Append('}');
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.UserAgent.ParseAdd("upgrade-authority-ua-push");
        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsync($"{apiBase}/repos/{owner}/{repoName}/pulls",
                new StringContent(body.ToString(), Encoding.UTF8, "application/json"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: GitHub API unreachable: {ex.Message}");
            return null;
        }
        if (!resp.IsSuccessStatusCode) { Console.Error.WriteLine($"error: GitHub API {resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}"); return null; }
        var json = await resp.Content.ReadAsStringAsync();
        var doc = System.Text.Json.JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("html_url", out var url) ? url.GetString() : null;
    }

    static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    internal static int Git(string cwd, string args, out string stdout, out string stderr)
    {
        var psi = new ProcessStartInfo("git", args) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        using var p = Process.Start(psi)!;
        stdout = p.StandardOutput.ReadToEnd();
        stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    // ---- push.v1 canonical writer ----
    internal static void WriteReport(string outDir, PushReport r)
    {
        var outParent = Path.GetDirectoryName(Path.GetFullPath(outDir)) ?? ".";
        var outName = Path.GetFileName(Path.GetFullPath(outDir));
        var tmp = Path.Combine(outParent, "." + outName + ".tmp-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, "push.v1.json"), Render(r));
            var backup = Path.Combine(outParent, "." + outName + ".old-" + Guid.NewGuid().ToString("N")[..8]);
            if (Directory.Exists(outDir)) Directory.Move(outDir, backup);
            try { Directory.Move(tmp, outDir); }
            catch { if (Directory.Exists(backup)) Directory.Move(backup, outDir); throw; }
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
    }

    static string Render(PushReport r)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        K(sb, 1, "schemaVersion"); sb.Append(": "); Q(sb, r.SchemaVersion); sb.Append(",\n");
        K(sb, 1, "mode"); sb.Append(": "); Q(sb, r.Mode); sb.Append(",\n");
        K(sb, 1, "waves"); sb.Append(": [\n");
        for (int i = 0; i < r.Waves.Count; i++)
        {
            var w = r.Waves[i];
            sb.Append("    {\n");
            K(sb, 3, "index"); sb.Append(": ").Append(w.Index).Append(",\n");
            K(sb, 3, "status"); sb.Append(": "); Q(sb, w.Status); sb.Append(",\n");
            K(sb, 3, "branch"); sb.Append(": "); Q(sb, w.Branch); sb.Append(",\n");
            K(sb, 3, "base"); sb.Append(": "); Q(sb, w.Base); sb.Append(",\n");
            K(sb, 3, "edits"); sb.Append(": ").Append(w.Edits).Append(",\n");
            K(sb, 3, "commitSubject"); sb.Append(": "); Q(sb, w.CommitSubject); sb.Append(",\n");
            K(sb, 3, "commitBody"); sb.Append(": "); Arr(sb, w.CommitBody, 3); sb.Append(",\n");
            K(sb, 3, "prTitle"); sb.Append(": "); Q(sb, w.PrTitle); sb.Append(",\n");
            K(sb, 3, "prBody"); sb.Append(": "); Arr(sb, w.PrBody, 3); sb.Append(",\n");
            K(sb, 3, "commands"); sb.Append(": "); Arr(sb, w.Commands, 3); sb.Append("\n");
            sb.Append("    }").Append(i < r.Waves.Count - 1 ? "," : "").Append("\n");
        }
        sb.Append("  ],\n");
        K(sb, 1, "observed"); sb.Append(": [\n");
        for (int i = 0; i < r.Observations.Count; i++)
        {
            var o = r.Observations[i];
            sb.Append("    {\n");
            K(sb, 3, "wave"); sb.Append(": "); Q(sb, o.Wave);
            if (o.CommitSha is not null) { sb.Append(",\n"); K(sb, 3, "commitSha"); sb.Append(": "); Q(sb, o.CommitSha); }
            if (o.PrUrl is not null) { sb.Append(",\n"); K(sb, 3, "prUrl"); sb.Append(": "); Q(sb, o.PrUrl); }
            if (o.Failure is not null) { sb.Append(",\n"); K(sb, 3, "failure"); sb.Append(": "); Q(sb, o.Failure); }
            sb.Append("\n    }").Append(i < r.Observations.Count - 1 ? "," : "").Append("\n");
        }
        sb.Append("  ]\n}\n");
        return sb.ToString();
    }
    static void Arr(StringBuilder sb, List<string> items, int ind)
    {
        if (items.Count == 0) { sb.Append("[]"); return; }
        sb.Append("[\n");
        for (int i = 0; i < items.Count; i++) { Pad(sb, ind + 1); Q(sb, items[i]); sb.Append(i < items.Count - 1 ? "," : "").Append('\n'); }
        Pad(sb, ind); sb.Append(']');
    }
    static void Pad(StringBuilder sb, int ind) { for (int i = 0; i < ind * 2; i++) sb.Append(' '); }
    static void K(StringBuilder sb, int ind, string key) { Pad(sb, ind); Q(sb, key); } // caller appends ": " (matches Apply.Str)
    static void Q(StringBuilder sb, string s)
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

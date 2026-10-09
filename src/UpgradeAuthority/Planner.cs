namespace UpgradeAuthority;

public sealed class UaException(string message) : Exception(message);

// SPEC-002 fusion + SPEC-003 planning. The merged golden corpus is normative:
// every emitted string matches it byte-exactly. Wording variants key on input
// properties (format: packages.config vs cpm vs packagereference; external vs
// internal target) — never on fixture identity.
public sealed class Engine
{
    readonly ProducerEvidence _pe; readonly OwnershipFile _ow; readonly PackageEvidence _px;
    readonly DeltaChange _c; readonly List<LockfileRows> _lockRepos = new(); // SPEC-008: 0..n lockfile repos (one entry per repo)
    readonly EstateScope? _scope; // SPEC-017: optional estate scope echo
    readonly Dictionary<string, string> _depsFreshness = new(); // SPEC-019 §5.3: repo -> fresh|stale|none|fresh-by-build; absent entry = conservative
    HashSet<string>? _affectedPkgsForReasons; // set in BuildPlan before BuildRepo runs (stale-reason scope)
    BuildFreshnessFile? _pendingFreshness; // ctor staging: applied once mirrors are loaded
    string _lockKind = "lockfile-rows.v0"; // evidence kind cites the input's actual schemaVersion (SPEC-007 §5.6)

    readonly Dictionary<string, List<string>> _produced = new();
    readonly Dictionary<string, List<string>> _producerRepos = new();
    readonly HashSet<string> _external = new();
    readonly Dictionary<string, string> _team = new();
    readonly Dictionary<string, List<Fact>> _facts = new();
    readonly Dictionary<string, List<LockRow>> _lockRows = new();
    readonly Dictionary<string, ScanCoverage> _coverage = new();
    readonly Dictionary<(string Repo, string Pkg), string> _pubStatus = new();
    readonly Dictionary<string, (string Producer, string Pkg)> _dEdge = new();

    static readonly Dictionary<string, string> _mirrors = new(); // alias -> canonical (declared in ownership.v0)

    internal static string NormBasic(string repo)
    {
        var r = repo.Trim();
        if (r.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) r = r[7..];
        if (r.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) r = r[8..];
        var slash = r.IndexOf('/');
        if (slash > 0) r = r[..slash].ToLowerInvariant() + r[slash..]; // lowercase host only
        r = r.TrimEnd('/');
        if (r.EndsWith(".git")) r = r[..^4];
        return r.TrimEnd('/');
    }

    // SPEC-018: drift reads the loaded inventory without planning — read-only views of the fused inputs.
    internal IReadOnlyDictionary<string, List<Fact>> FactsView => _facts;
    internal IReadOnlyDictionary<string, List<LockRow>> LockRowsView => _lockRows;

    // SPEC-017 §5: the plan echoes the config — deduped, ordinal (permutation-stable); the config's
    // own order never reaches the plan. Null when there is no scope input (zero churn for existing fixtures).
    PlanScope? BuildScope() => _scope is null ? null : new PlanScope
    {
        Mode = _scope.EffectiveInclude is null ? "exclude" : "include",
        IncludedCount = _scope.EffectiveInclude?.Count,
        OutOfScope = _scope.EffectiveExclude is { Count: > 0 } e ? e.OrderBy(x => x, StringComparer.Ordinal).ToList() : null,
    };

    static string Norm(string repo)
    {
        // Full identity is preserved (org-a/service != org-b/service). A short/alias spelling
        // merges onto its canonical identity ONLY through a declared mirror mapping (SPEC-001 §3).
        var basic = NormBasic(repo);
        return _mirrors.TryGetValue(basic, out var canonical) ? canonical : basic;
    }

    static readonly List<(string Repo, string Pkg)> _noDeps = new();

    public Engine(ProducerEvidence pe, OwnershipFile ow, PackageEvidence px, DeltaFile delta, List<LockfileRows> lockRepos, EstateScope? scope = null, BuildFreshnessFile? buildFreshness = null)
    {
        if (delta.Changes.Count != 1)
            throw new UaException($"rejected: package-delta.v1 must contain exactly one change (found {delta.Changes.Count}); single-change planning only in V0 (SPEC-003 §1a)");
        _c = delta.Changes[0]; _pe = pe; _ow = ow; _px = px; _scope = scope;
        _pendingFreshness = buildFreshness; // normalized AFTER mirrors load (aliases must resolve like every other input)
        foreach (var e in pe.ExternalPackages) _external.Add(e.PackageId);
        foreach (var p in pe.Producers)
        {
            var repo = Norm(p.Repo);
            var list = _produced.GetValueOrDefault(repo) ?? (_produced[repo] = new());
            if (!list.Contains(p.PackageId)) list.Add(p.PackageId);
            var repos = _producerRepos.GetValueOrDefault(p.PackageId) ?? (_producerRepos[p.PackageId] = new());
            if (!repos.Contains(repo)) repos.Add(repo);
            if (p.PublicationStatus is not null) _pubStatus[(repo, p.PackageId)] = p.PublicationStatus;
        }
        foreach (var kv in _produced) kv.Value.Sort(new NaturalComparer());
        _mirrors.Clear();
        foreach (var m in ow.Mirrors)
        {
            var canonical = NormBasic(m.Canonical);
            foreach (var a in m.Aliases) _mirrors[NormBasic(a)] = canonical;
            if (NormBasic(m.Canonical) != canonical) _mirrors[NormBasic(m.Canonical)] = canonical;
        }
        if (_pendingFreshness is not null) // SPEC-019: applied AFTER mirrors load (Codex P2 r2); under BOTH the raw
        {                                // key (lockfile rows key RepoKey verbatim) and Norm (aliases + URL hosts resolve)
            foreach (var e in _pendingFreshness.Repos)
            {
                foreach (var key in new[] { e.Repo, Norm(e.Repo) })
                {
                    if (_depsFreshness.TryGetValue(key, out var prior) && prior != e.Freshness)
                        throw new UaException($"malformed input build-freshness.v0.json: '{e.Repo}' and an alias resolve to one repo with conflicting freshness ({prior} and {e.Freshness}) — the file contradicts itself"); // C10 r4: normalization must not turn conflicts into last-wins
                    _depsFreshness[key] = e.Freshness;
                }
            }
            _pendingFreshness = null;
        }
        foreach (var o in ow.Ownerships) _team[Norm(o.Repo)] = o.Team;
        foreach (var f in px.Facts) { f.Repo = Norm(f.Repo); f.Projects.Sort(StringComparer.Ordinal); (_facts.GetValueOrDefault(f.Repo) ?? (_facts[f.Repo] = new())).Add(f); }
        foreach (var kv in _facts) kv.Value.Sort((a, b) => string.CompareOrdinal(a.PackageId + "\0" + a.Path, b.PackageId + "\0" + b.Path));
        foreach (var r in px.ScanCoverage) { r.Repo = Norm(r.Repo); r.Gaps.Sort(StringComparer.Ordinal); _coverage[r.Repo] = r; }
        // Insert in NORMALIZED-repo order: dict enumeration order is load-bearing (First() lookups in
        // BuildRepo), so it must be a function of identity, never repos[] file order (SPEC-008 §6.3).
        var duplicates = new List<string>();
        foreach (var lockRows in lockRepos.OrderBy(l => Norm(l.Repo), StringComparer.Ordinal))
        {
            lockRows.Repo = Norm(lockRows.Repo);
            if (_lockRows.ContainsKey(lockRows.Repo)) { duplicates.Add(lockRows.Repo); continue; } // alias spellings mapping to one identity (PR #11 C1/Q4)
            lockRows.Rows.Sort(Ingest.RowOrder); // SPEC-007 §8.1 canonical row order (v0: packageId-only — byte-identical)
            _lockRows[lockRows.Repo] = lockRows.Rows;
            _lockRepos.Add(lockRows);
        }
        if (duplicates.Count > 0)
            throw new UaException($"malformed input: duplicate lockfile repos[] entries after identity normalization ({string.Join(", ", duplicates.Distinct().OrderBy(d => d, StringComparer.Ordinal))}) — alias spellings of one repository must arrive as one merged entry, never two");
        // evidence-kind provenance (SPEC-007 §5.6): one lockfile file per input, so one version for all repos in it
        if (lockRepos.Count > 0)
            _lockKind = lockRepos[0].SchemaVersion switch { "lockfile-rows.v1" => "lockfile-rows.v1", "lockfile-rows.v2" => "lockfile-rows.v2", _ => "lockfile-rows.v0" };
    }

    string Target => _c.PackageName;
    string PubStatusOf(string repo, string pkg) => _pubStatus.GetValueOrDefault((repo, pkg), "unknown");
    Dictionary<string, List<char>> letters0 = new(); // set at plan start (BuildPlan)
    string OwnershipOf(string repo) => !_team.TryGetValue(repo, out var t) ? "unknown" : t == _ow.SelfTeamId ? "self" : "external";
    string TeamOf(string repo) => _team.GetValueOrDefault(repo, "?");
    List<string> ProducersOf(string pkg) { var l = _producerRepos.GetValueOrDefault(pkg, new List<string>()); l.Sort(StringComparer.Ordinal); return l; }
    string ProducerRepoOf(string pkg) => ProducersOf(pkg).FirstOrDefault() ?? "";
    List<Fact> FactsOf(string repo) => _facts.GetValueOrDefault(repo, new List<Fact>());
    internal List<Fact> ApplyFacts(string repo) => FactsOf(repo); // SPEC-009: ua apply reads the same evidence
    internal bool ProducesTarget(string repo) => ProducersOf(Target).Contains(repo);
    List<LockRow> LockOf(string repo) => _lockRows.GetValueOrDefault(repo, new List<LockRow>());
    // SPEC-019 §5.3: build-resolved rows participate in PROOF (rule-c affectedness, producer/consumer
    // edges, not-affected closure) only when fresh — stale/absent rows stay visible for suspicion
    // (touches) and findings (input observations) but prove nothing in either direction.
    bool DepsFresh(string repo) => _depsFreshness.GetValueOrDefault(repo) is "fresh" or "fresh-by-build";
    List<LockRow> LockProvable(string repo) => LockOf(repo).Where(r => r.Provenance is null || DepsFresh(repo)).ToList();
    bool HasStaleDepsRow(string repo, HashSet<string> pkgs) => LockOf(repo).Any(r => r.Provenance is not null && !DepsFresh(repo) && pkgs.Contains(r.PackageId));
    List<string> ProducedOf(string repo) => _produced.GetValueOrDefault(repo, new List<string>());

    IEnumerable<string> ConsumersOf(string pkg)
    {
        foreach (var (repo, facts) in _facts) if (facts.Any(f => f.PackageId == pkg)) yield return repo;
        foreach (var (repo, rows) in _lockRows) if (LockProvable(repo).Any(r => r.PackageId == pkg && r.Type == "direct")) yield return repo; // SPEC-019: provable rows only
    }

    IEnumerable<string> AllRepos()
    {
        var all = new HashSet<string>(_produced.Keys.Concat(_facts.Keys).Concat(_team.Keys).Concat(_coverage.Keys).Concat(_producerRepos.Values.SelectMany(v => v)));
        foreach (var lf in _lockRepos) all.Add(lf.Repo);
        return all.OrderBy(r => r, StringComparer.Ordinal);
    }

    bool VersioningDiffers(string repo) => VersionGroups(repo).Count > 1;

    // (major, version, packages) groups from actual ProducedVersion values; dominant group first (largest, tie: lowest major)
    List<(string Major, string Version, List<string> Pkgs)> VersionGroups(string repo)
    {
        var entries = _pe.Producers.Where(p => p.Repo == repo && p.ProducedVersion is not null && ProducedOf(repo).Contains(p.PackageId));
        var groups = entries.GroupBy(p => p.ProducedVersion!.Split('.')[0])
            .Select(g => (Major: g.Key, Version: g.Select(p => p.ProducedVersion!).OrderBy(v => v, StringComparer.Ordinal).First(), Pkgs: g.Select(p => p.PackageId).OrderBy(p2 => p2, new NaturalComparer()).ToList()))
            .OrderByDescending(g => g.Pkgs.Count).ThenBy(g => g.Major, StringComparer.Ordinal).ToList();
        return groups;
    }

    string VersioningNoteText(string repo)
    {
        var g = VersionGroups(repo);
        var dom = g[0]; var rest = g.Skip(1).ToList();
        var restText = string.Join("; ", rest.Select(x => $"{string.Join(",", x.Pkgs)} = independent {x.Version} line"));
        return $"ASSUMPTION FLAGGED: versioning evidence differs within this unit ({Range(dom.Pkgs)} = {dom.Major}.x; {restText}) — single-unit grouping is an assumption, not evidenced shared release";
    }

    string VersioningGapDetail(string repo)
    {
        var rest = VersionGroups(repo).Skip(1).ToList();
        var parts = rest.Select(x => $"{string.Join("/", x.Pkgs)} carry independent {x.Version} versioning").ToList();
        return $"{string.Join("; ", parts)}; per-package shared-release semantics unevidenced in V0 inputs (see wave 1 notes)";
    }

    // the produced package through which some consumer's lockfile evidences target flow
    string? CarrierPkg(string repo) =>
        ProducedOf(repo).FirstOrDefault(p2 => _lockRows.Any(kv => kv.Key != repo && kv.Value.Any(r => r.PackageId == Target && r.Via == p2)));

    static string Range(List<string> pkgs)
    {
        var sorted = pkgs.OrderBy(p2 => p2, new NaturalComparer()).ToList();
        return sorted.Count == 1 ? sorted[0] : $"{sorted.First()}..{sorted.Last()}";
    }

    sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string? a, string? b)
        {
            a ??= ""; b ??= "";
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
                {
                    int sa = ia, sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                    var da = a[sa..ia]; var db = b[sb..ib];
                    int cmp = da.Length != db.Length ? da.Length.CompareTo(db.Length) : string.CompareOrdinal(da, db); // overflow-safe: length then digits
                    if (cmp != 0) return cmp;
                }
                else { int cmp = char.ToLowerInvariant(a[ia]).CompareTo(char.ToLowerInvariant(b[ib])); if (cmp != 0) return cmp; ia++; ib++; }
            }
            return (a.Length - ia).CompareTo(b.Length - ib);
        }
    }

    public Plan BuildPlan()
    {
        var producers = ProducersOf(Target);
        bool externalTarget = _external.Contains(Target);
        bool contradiction = producers.Count >= 2;
        bool producerUnknown = producers.Count == 0 && !externalTarget;
        string? externalProducer = producers.Count == 1 && OwnershipOf(producers[0]) == "external" ? producers[0] : null;

        // ---- affected closure + rule letters (SPEC-003 §2) ----
        var letters = new Dictionary<string, List<char>>();
        letters0 = letters;
        void AddRule(string r, char l) { var list = letters.GetValueOrDefault(r) ?? (letters[r] = new()); if (!list.Contains(l)) list.Add(l); }
        var affected = new HashSet<string>();
        _dEdge.Clear(); // causative ripple edge per consumer (this plan's closure)
        foreach (var r in producers) { affected.Add(r); AddRule(r, 'a'); }
        foreach (var (repo, facts) in _facts.Where(kv => kv.Value.Any(f => f.PackageId == Target))) { affected.Add(repo); AddRule(repo, 'b'); }
        foreach (var lf in _lockRepos.Where(l => LockProvable(l.Repo).Any(r => r.PackageId == Target && r.Type == "transitive"))) { affected.Add(lf.Repo); AddRule(lf.Repo, 'c'); } // SPEC-019: freshness-gated — stale build output never schedules work
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var repo in affected.OrderBy(r => r, StringComparer.Ordinal).ToList())
                foreach (var pkg in ProducedOf(repo).OrderBy(p2 => p2, StringComparer.Ordinal))
                    foreach (var consumer in ConsumersOf(pkg).OrderBy(c => c, StringComparer.Ordinal))
                    {
                        var edge = (repo, pkg);
                        if (!_dEdge.TryGetValue(consumer, out var prev) || string.CompareOrdinal(edge.Item1 + "\0" + edge.Item2, prev.Producer + "\0" + prev.Pkg) < 0)
                            _dEdge[consumer] = edge;
                        AddRule(consumer, 'd');
                        if (affected.Add(consumer)) changed = true;
                    }
        }

        // ---- dependency edges among affected repos: consumer -> (producer, pkg) ----
        string? ProducerInPlan(string pkg) => ProducersOf(pkg).FirstOrDefault(affected.Contains);
        var deps = new Dictionary<string, List<(string Repo, string Pkg)>>();
        foreach (var repo in affected)
        {
            var ds = new List<(string Repo, string Pkg)>();
            foreach (var f in FactsOf(repo)) { if (ProducerInPlan(f.PackageId) is { } pp && pp != repo) ds.Add((pp, f.PackageId)); }
            foreach (var row in LockProvable(repo).Where(r => r.Type == "direct")) { if (ProducerInPlan(row.PackageId) is { } pp && pp != repo) ds.Add((pp, row.PackageId)); }
            ds = ds.Distinct().ToList(); // SPEC-007 §5.2: same package in two lockfiles = one edge
            ds.Sort((x, y) => string.CompareOrdinal(x.Repo + "\0" + x.Pkg, y.Repo + "\0" + y.Pkg));
            if (ds.Count > 0) deps[repo] = ds;
        }

        // ---- affected packages + classification (before cycle handling: rev3 unknown visibility
        // applies to cycle-stop plans too — the stop must not hide unclassified repos) ----
        var affectedPkgs = new HashSet<string> { Target };
        foreach (var r in affected) foreach (var p2 in ProducedOf(r)) affectedPkgs.Add(p2);
        _affectedPkgsForReasons = affectedPkgs;

        var classification = new Dictionary<string, string>();
        foreach (var repo in AllRepos())
        {
            if (affected.Contains(repo)) { classification[repo] = "affected"; continue; }
            bool complete = _coverage.GetValueOrDefault(repo)?.Status == "complete";
            bool hasLock = LockProvable(repo).Count > 0; // SPEC-019: a stale build-resolved closure proves nothing — only lockfile rows or FRESH deps.json rows can
            bool touches = FactsOf(repo).Any(f => affectedPkgs.Contains(f.PackageId)) || LockOf(repo).Any(r => affectedPkgs.Contains(r.PackageId)); // suspicion sees ALL rows incl. stale deps.json (conservative direction)
            classification[repo] = complete && hasLock && !touches ? "not-affected" : "unknown";
        }

        // ---- F-cyc: cycle among affected repos -> typed refusal ----
        var cycle = FindCycle(affected, deps);
        if (cycle is not null)
        {
            var cp = CyclePlan(cycle, affected, letters, classification);
            var cycleFindings = BuildFindings(); // findings are input observations (SPEC-007 §6): they surface even in a stop plan
            if (cycleFindings.Count > 0) cp.Uncertainty.Findings = cycleFindings;
            return cp;
        }

        var scheduled = affected.Where(r => OwnershipOf(r) != "unknown").ToList();
        bool producerGated = contradiction || producerUnknown;

        // root gate: own unpublished dep, else inherited from deps (transitive propagation).
        // Tracks (pkg, owner-producer) so an inherited gate is verified against the repo owning the status.
        var rootGate = new Dictionary<string, (string? Pkg, string? Owner)>();
        (string?, string?) RootGate(string repo)
        {
            if (rootGate.TryGetValue(repo, out var g)) return (g.Pkg, g.Owner);
            rootGate[repo] = (null, null); // guard while resolving
            var own = deps.GetValueOrDefault(repo, _noDeps)
                .Where(d => PubStatusOf(d.Repo, d.Pkg) == "unpublished")
                .OrderBy(d => d.Pkg, StringComparer.Ordinal).Select(d => (d.Pkg, d.Repo)).FirstOrDefault();
            if (own.Pkg is not null) { rootGate[repo] = own; return (own.Pkg, own.Repo); }
            foreach (var d in deps.GetValueOrDefault(repo, _noDeps).Where(d => scheduled.Contains(d.Repo)).OrderBy(d => d.Repo, StringComparer.Ordinal))
            {
                var up = RootGate(d.Repo);
                if (up.Item1 is { } upPkg && up.Item2 is { } upOwn && PubStatusOf(upOwn, upPkg) == "unpublished")
                { rootGate[repo] = (upPkg, upOwn); return (upPkg, upOwn); }
            }
            return (rootGate[repo].Pkg, rootGate[repo].Owner);
        }
        string? NeedsUnpublished(string repo) => RootGate(repo).Item1;

        string StatusOf(string repo) =>
            contradiction ? "provisional" :
            externalProducer is not null || producerUnknown ? "conditional" :
            NeedsUnpublished(repo) is not null ? "conditional" : "ready";

        var depth = new Dictionary<string, int>();
        var inProgress = new HashSet<string>();
        int DepthOf(string repo)
        {
            if (depth.TryGetValue(repo, out var d)) return d;
            if (!inProgress.Add(repo)) throw new UaException($"internal: dependency cycle reached depth computation for {repo} without detection");
            var ds = deps.GetValueOrDefault(repo, _noDeps).Select(x => x.Repo).Where(scheduled.Contains).ToList();
            var result = ds.Count == 0 ? 1 : 1 + ds.Max(DepthOf);
            inProgress.Remove(repo);
            return depth[repo] = result;
        }
        foreach (var r in scheduled) DepthOf(r);

        static int Rank(string s) => s switch { "ready" => 0, "conditional" => 1, _ => 2 };
        var groups = scheduled.GroupBy(r => (Rank(StatusOf(r)), DepthOf(r))).OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2).ToList();

        const string cid = "C1";
        var waves = new List<PlanWave>();
        int idx = 1;
        foreach (var g in groups)
        {
            var w = new PlanWave { Index = idx++, Status = StatusOf(g.First()) };
            var prereqs = new List<(int Rank, string Text)>();
            bool isClaimantWave = contradiction && g.All(r => producers.Contains(r));

            if (w.Status == "provisional") w.BlockedOn = cid;
            if (w.Status == "conditional")
            {
                if (externalProducer is not null) w.Condition = T6(TeamOf(externalProducer), Target, _c.NewVersion);
                else if (producerUnknown) w.Condition = T8(Target, g.First(), _c.NewVersion);
                else if (g.Select(NeedsUnpublished).FirstOrDefault(x => x is not null) is { } pub)
                    w.Condition = T7(pub, g.First(r => NeedsUnpublished(r) == pub));
            }

            if (externalTarget && w.Index == 1) prereqs.Add((1, T5(Target, _c.NewVersion)));
            if (externalProducer is not null) prereqs.Add((2, T6a(Target, _c.NewVersion)));
            if (producerUnknown) prereqs.Add((3, T8a(Target, _c.NewVersion)));
            if (contradiction && !isClaimantWave) prereqs.Add((4, T9(cid, Target, _c.NewVersion)));
            foreach (var depRepo in contradiction ? Enumerable.Empty<string>() : g.SelectMany(r => deps.GetValueOrDefault(r, _noDeps)).Select(d => d.Repo).Distinct().OrderBy(r => r, StringComparer.Ordinal))
            {
                if (depRepo == externalProducer) continue;
                var consumed = deps.Where(kv => g.Contains(kv.Key)).SelectMany(kv => kv.Value).Where(d => d.Repo == depRepo).Select(d => d.Pkg).Distinct().OrderBy(p2 => p2, StringComparer.Ordinal).ToList();
                var unpub = consumed.FirstOrDefault(p2 => PubStatusOf(depRepo, p2) == "unpublished");
                if (depRepo == producers.FirstOrDefault())
                    prereqs.Add((0, PubStatusOf(depRepo, Target) == "unpublished" ? T7a(depRepo, Target) : unpub is { } u ? T7a(depRepo, u) : T1(depRepo, Target, _c.NewVersion)));
                else
                    prereqs.Add((0, unpub is { } u2 ? T7a(depRepo, u2) : T4(depRepo, consumed[0], Target, _c.NewVersion)));
            }
            w.Prerequisites = prereqs.OrderBy(x => x.Rank).ThenBy(x => x.Text, StringComparer.Ordinal).Select(x => x.Text).ToList();

            foreach (var r in g.OrderBy(r => r, new NaturalComparer())) w.ReleaseUnits.Add(BuildUnit(r, producers, contradiction));
            waves.Add(w);
        }

        // ripple content-exposure notes (F1/F1b/F4b): unit's package flows to a lockfile-less
        // d-rule downstream; attach with the downstream's REAL wave index; skip unscheduled.
        var waveIndexOf = new Dictionary<string, int>();
        foreach (var w in waves) foreach (var u2 in w.ReleaseUnits) if (!waveIndexOf.ContainsKey(u2.Repo)) waveIndexOf[u2.Repo] = w.Index;
        foreach (var w in waves)
            foreach (var u2 in w.ReleaseUnits)
            {
                if (producers.Contains(u2.Repo) && u2.Repo == producers.FirstOrDefault()) continue;
                var ds = _facts.Keys
                    .Where(r2 => r2 != u2.Repo && scheduled.Contains(r2) && waveIndexOf.ContainsKey(r2) && waveIndexOf[r2] > w.Index
                                 && !_lockRows.ContainsKey(r2) && DMaterial(r2)
                                 && FactsOf(r2).Any(f => u2.Packages.Contains(f.PackageId)))
                    .OrderBy(r2 => r2, StringComparer.Ordinal).FirstOrDefault();
                if (ds is not null)
                {
                    (u2.Notes ??= new List<string>()).Clear();
                    u2.Notes.Add($"{ds} scheduled in wave {waveIndexOf[ds]} on the ripple obligation; its {Target} content-exposure stays a gap");
                }
            }

        // ---- repos[] order (§4.2) ----
        var firstWave = new Dictionary<string, (int rank, int idx)>();
        foreach (var w in waves) foreach (var u in w.ReleaseUnits) if (!firstWave.ContainsKey(u.Repo)) firstWave[u.Repo] = (Rank(w.Status), w.Index);
        var ordered = waves.SelectMany(w => w.ReleaseUnits.Select(u => u.Repo))
            .OrderBy(r => firstWave[r]).ThenBy(r => r, StringComparer.Ordinal).ToList();
        ordered.AddRange(affected.Where(r => !scheduled.Contains(r)).OrderBy(r => r, StringComparer.Ordinal));
        ordered.AddRange(AllRepos().Where(r => classification[r] == "unknown").OrderBy(r => r, StringComparer.Ordinal)); // SPEC-003 rev3: unknown repos are visible, never silently dropped (2026-10-08 real-estate finding)
        ordered.AddRange(AllRepos().Where(r => classification[r] == "not-affected").OrderBy(r => r, StringComparer.Ordinal));

        var plan = new Plan { Waves = waves, Uncertainty = new PlanUncertainty() };
        plan.Delta = new PlanDelta { PackageName = Target, Ecosystem = _c.Ecosystem, ChangeType = _c.ChangeType, OldVersion = _c.OldVersion, NewVersion = _c.NewVersion };
        plan.Repos = ordered.Select(r => BuildRepo(r, letters.GetValueOrDefault(r, new List<char>()), classification, producers, externalTarget, producerGated)).ToList();
        plan.Scope = BuildScope(); // SPEC-017 §5: null unless a scope input exists

        if (contradiction)
            plan.Uncertainty.Contradictions.Add(new PlanContradiction
            {
                Id = cid, Subject = $"{Target} producer",
                Claims = producers.Select(r => (r, Target)).ToList(),
                DownstreamProvisional = scheduled.Where(r => !producers.Contains(r)).OrderBy(r => r, StringComparer.Ordinal).ToList(),
            });
        plan.Uncertainty.Gaps.AddRange(BuildGaps(affected, producers, externalTarget, letters, deps));
        plan.Uncertainty.Gaps.Sort((a, b) => string.CompareOrdinal(a.Subject, b.Subject));
        var findings = BuildFindings();
        if (findings.Count > 0) plan.Uncertainty.Findings = findings; // SPEC-007 §6: emitted only when non-empty
        return plan;
    }

    // ---------------- F-cyc ----------------

    // Full DFS (white/grey/black) over every dependency edge; deterministic ordinal order.
    // Returns repo/package alternation rotated to start at the delta-target producer when present.
    List<string>? FindCycle(HashSet<string> affected, Dictionary<string, List<(string Repo, string Pkg)>> deps)
    {
        var color = new Dictionary<string, int>(); // 0 white, 1 grey, 2 black
        var stack = new List<(string Repo, string? Pkg)>();
        List<(string Repo, string? Pkg)>? cycleEdges = null;
        bool Dfs(string node)
        {
            color[node] = 1; stack.Add((node, null));
            foreach (var d in deps.GetValueOrDefault(node, _noDeps).OrderBy(d => d.Repo, StringComparer.Ordinal).ThenBy(d => d.Pkg, StringComparer.Ordinal))
            {
                if (!affected.Contains(d.Repo)) continue;
                // hop label: a package NODE produces that d.Repo consumes (deps[d.Repo] where Repo == node),
                // so the path reads producer -> produced package -> consumer, matching the golden alternation
                var hopPkg = deps.GetValueOrDefault(d.Repo, _noDeps).Where(x => x.Repo == node).Select(x => x.Pkg)
                    .OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? d.Pkg;
                stack.Add((d.Repo, hopPkg));
                if (color.GetValueOrDefault(d.Repo, 0) == 1) { cycleEdges = stack.Skip(stack.FindIndex(e => e.Repo == d.Repo)).ToList(); return true; }
                if (color.GetValueOrDefault(d.Repo, 0) == 0 && Dfs(d.Repo)) return true;
                stack.RemoveAt(stack.Count - 1);
            }
            color[node] = 2; stack.RemoveAt(stack.Count - 1);
            return false;
        }
        foreach (var start in affected.OrderBy(r => r, StringComparer.Ordinal))
            if (color.GetValueOrDefault(start, 0) == 0 && Dfs(start)) break;
        if (cycleEdges is null) return null;
        // DFS walked consumer->producer; the path narrates producer->consumer, so reverse the repo cycle
        var cycleRepos = cycleEdges.Where(e => e.Pkg is null).Select(e => e.Repo).ToList();
        cycleRepos.Reverse();
        var tp = ProducersOf(Target).FirstOrDefault(cycleRepos.Contains);
        while (tp is not null && cycleRepos[0] != tp) { cycleRepos.Add(cycleRepos[0]); cycleRepos.RemoveAt(0); }
        var full = new List<string>();
        for (int k = 0; k < cycleRepos.Count; k++)
        {
            var a = cycleRepos[k];
            var b = cycleRepos[(k + 1) % cycleRepos.Count];
            full.Add(a);
            full.Add(deps.GetValueOrDefault(b, _noDeps).Where(d => d.Repo == a).Select(d => d.Pkg).OrderBy(x => x, StringComparer.Ordinal).First());
        }
        full.Add(full[0]);
        return full;
    }

    Plan CyclePlan(List<string> path, HashSet<string> affected, Dictionary<string, List<char>> letters, Dictionary<string, string> classification)
    {
        string cycleId = "CYC-1";
        var participants = new HashSet<string>(path.Where((_, i) => i % 2 == 0));
        var p = new Plan
        {
            Delta = new PlanDelta { PackageName = Target, Ecosystem = _c.Ecosystem, ChangeType = _c.ChangeType, OldVersion = _c.OldVersion, NewVersion = _c.NewVersion },
            Scope = BuildScope(), // SPEC-017 §5: stop plans tell the whole truth too (rev3 corollary parity)
            Stop = new PlanStop
            {
                Reason = "CYCLE_DETECTED",
                Detail = CycleDetail(path),
                CyclePath = path,
            }
        };
        foreach (var repo in affected.OrderBy(r => r, StringComparer.Ordinal))
        {
            var e = new PlanRepo { Repo = repo, Classification = "affected", Ownership = OwnershipOf(repo), ActionType = "unknown", Confidence = ("declared", 1) };
            var cycleNotes = _coverage.GetValueOrDefault(repo)?.Notes;
            if (cycleNotes is { Count: > 0 }) e.ScanNotes = cycleNotes.OrderBy(n => n, StringComparer.Ordinal).ToList(); // SPEC-015 parity with BuildRepo
            if (participants.Contains(repo))
            {
                var idx = path.IndexOf(repo);
                var nextRepo = path[(idx + 2) % (path.Count - 1)];
                var pkgOut = ProducedOf(repo).FirstOrDefault(pp => path.Contains(pp)) ?? ProducedOf(repo).OrderBy(pp => pp, new NaturalComparer()).FirstOrDefault() ?? "?";
                var facts = FactsOf(repo);
                if (letters.GetValueOrDefault(repo, new List<char>()).Contains('a') && facts.Count > 0)
                {
                    var consumedPkg = facts.Where(f => f.PackageId != Target).Select(f => f.PackageId).OrderBy(x => x, StringComparer.Ordinal).FirstOrDefault() ?? facts[0].PackageId;
                    e.Reasons.Add($"produces {Target} (delta target); also consumes {consumedPkg} produced by {ProducerRepoOf(consumedPkg)} — participant in release cycle {cycleId}");
                }
                else if (facts.Count > 0)
                {
                    var consumesPkg = facts.Select(f => f.PackageId).OrderBy(x => x, StringComparer.Ordinal).First();
                    e.Reasons.Add($"consumes {consumesPkg} and produces {pkgOut} consumed by {nextRepo} — participant in release cycle {cycleId}");
                }
                else
                    e.Reasons.Add($"produces {pkgOut} consumed within release cycle {cycleId}");
                e.EvidenceKinds = new List<string> { "package-evidence.v0", "producer-evidence.v0" };
            }
            else
            {
                e.Reasons.Add($"affected by the delta but blocked behind release cycle {cycleId}; no order is emitted while the cycle stands");
                e.EvidenceKinds = letters.GetValueOrDefault(repo, new List<char>()).Contains('a')
                    ? new List<string> { "producer-evidence.v0" } : new List<string> { "package-evidence.v0" };
            }
            p.Repos.Add(e);
        }
        // SPEC-003 rev3: a stopped plan still tells the whole truth — classification-unknown
        // repos are visible here too, never hidden behind the cycle refusal
        foreach (var repo in AllRepos().Where(r => classification.GetValueOrDefault(r) == "unknown").OrderBy(r => r, StringComparer.Ordinal))
        {
            var e = new PlanRepo
            {
                Repo = repo,
                Classification = "unknown",
                Ownership = OwnershipOf(repo),
                ActionType = "unknown",
                Reasons = new List<string> { "classification unknown: coverage gaps or unresolved exposure — never not-affected without positive evidence" },
                EvidenceKinds = new List<string> { "package-evidence.v0" },
                Confidence = ("declared", 1),
            };
            var cycleNotesU = _coverage.GetValueOrDefault(repo)?.Notes;
            if (cycleNotesU is { Count: > 0 }) e.ScanNotes = cycleNotesU.OrderBy(n => n, StringComparer.Ordinal).ToList(); // SPEC-015 parity with BuildRepo (Baz round-2)
            p.Repos.Add(e);
        }
        return p;
    }

    static string CycleDetail(List<string> path)
    {
        // path alternates repo, package, repo, ..., back to start; describe every hop.
        var hops = new List<string>();
        for (int i = 0; i + 2 < path.Count + 1 && i + 2 <= path.Count; i += 2)
        {
            var from = path[i]; var pkg = path[i + 1]; var to = path[(i + 2) % path.Count];
            hops.Add($"{from} produces {pkg} consumed by {to}");
        }
        var joined = string.Join(", while ", hops);
        return $"repo-level release cycle detected: {joined}. V0 detects and refuses: no wave order is emitted. Bootstrap/intermediate-version resolution is a human-reviewed decision (SPEC-003).";
    }

    // ---------------- units ----------------

    PlanUnit BuildUnit(string repo, List<string> producers, bool contradiction)
    {
        var u = new PlanUnit { Repo = repo, Packages = ProducedOf(repo).OrderBy(p2 => p2, new NaturalComparer()).ToList() };
        bool isProducerRepo = producers.Contains(repo);
        bool hasPubMap = u.Packages.Any(p2 => _pubStatus.ContainsKey((repo, p2)));
        if (isProducerRepo && contradiction) u.Notes = new List<string> { $"candidate producer — provisional pending C1 resolution" };
        else if (OwnershipOf(repo) == "external") u.Notes = new List<string> { "external — request/await" };
        else if (hasPubMap) u.Notes = new List<string> { "current publication state cited from producer-evidence.v0 (fixture-declared)" };
        else if (VersioningDiffers(repo))
        {
            u.Notes = new List<string>
            {
                VersioningNoteText(repo),
                "changing the unit later is a deliberate golden-file change"
            };
        }
        else if (u.Packages.Count > 1) u.Notes = new List<string> { "shared compilation observed; shared-release NOT evidenced (no publication/versioning evidence per package)" };
        else if (_coverage.GetValueOrDefault(repo)?.Status == "gaps" && FactsOf(repo).Any(f => f.PackageId == Target))
        {
            var gap = (_coverage[repo].Gaps.FirstOrDefault() ?? "").Split(" not scanned")[0];
            var lastSeg = gap.Contains('/') ? gap[(gap.LastIndexOf('/') + 1)..] : gap;
            if (FactsOf(repo).FirstOrDefault(f => f.PackageId == Target)?.Format == "packages.config")
                u.Notes = new List<string> { $"packages.config consumer — edit path is packages.config; unscanned {lastSeg} area may surface additional work (tracked as a gap)" };
            else
                u.Notes = new List<string> { "scheduled on the observed edge; unscanned area may surface additional work — tracked as a gap, not a blocker" };
        }
        else if (FactsOf(repo).FirstOrDefault(f => f.PackageId == Target)?.Format == "packages.config")
            u.Notes = new List<string> { "packages.config consumer — edit path is packages.config, not PackageReference" };
        else if (FactsOf(repo).FirstOrDefault(f => f.PackageId == Target)?.Format == "cpm")
        {
            static string Bare(string p2) => p2.Contains('/') ? p2[(p2.LastIndexOf('/') + 1)..] : p2;
            var fs = FactsOf(repo);
            var central = fs.FirstOrDefault(f => f.ConstraintSource == "Directory.Packages.props");
            var ov = fs.FirstOrDefault(f => f.ConstraintSource == "VersionOverride");
            var parts = new List<string>();
            if (central is not null) parts.Add($"{Bare(central.Path)} (central, fans out to {string.Join("/", central.Projects.Select(Bare))})");
            if (ov is not null) parts.Add($"{ov.Projects.Select(Bare).FirstOrDefault()} VersionOverride (separate visible pin — not hidden by the central version)");
            u.Notes = new List<string> { $"edit sites: {string.Join(" AND ", parts)}" };
        }
        if (hasPubMap)
        {
            u.PublicationStatus = new SortedDictionary<string, string>(new NaturalComparer());
            foreach (var p2 in u.Packages.Where(p2 => _pubStatus.ContainsKey((repo, p2)))) u.PublicationStatus[p2] = _pubStatus[(repo, p2)];
        }
        return u;
    }

    // ---------------- repo entries ----------------

    PlanRepo BuildRepo(string repo, List<char> ruleLettersRaw, Dictionary<string, string> classification, List<string> producers, bool externalTarget, bool producerGated)
    {
        var e = new PlanRepo { Repo = repo, Classification = classification[repo], Ownership = OwnershipOf(repo) };
        var covNotes = _coverage.GetValueOrDefault(repo)?.Notes;
        if (covNotes is { Count: > 0 }) e.ScanNotes = covNotes.OrderBy(n => n, StringComparer.Ordinal).ToList(); // SPEC-015: notes ride the plan, order-canonicalized (input-permutation stability)
        var letters = ruleLettersRaw.OrderBy(x => "abcd".IndexOf(x)).ToList();
        var kinds = new List<string>();
        string rung = "declared"; int corroboration = 1;
        var facts = FactsOf(repo);
        var bFact = facts.FirstOrDefault(f => f.PackageId == Target);

        if (e.Classification == "not-affected")
        {
            e.ActionType = "none";
            var provable = LockProvable(repo);
            var lockRows = provable.Where(r => r.Provenance is null).Select(r => $"{r.PackageId} {r.Type}").Distinct().ToList();
            var depsRows = provable.Where(r => r.Provenance is not null).Select(r => $"{r.PackageId} {r.Type}").Distinct().ToList();
            // SPEC-007 §5.4: multi-row closure enumerates distinct (packageId, type) pairs.
            // SPEC-019 §5.3: lockfile-only closures stay BYTE-IDENTICAL to the pre-019 text (Q1/Q2 ride only
            // when build-resolved rows participated); mixed closures name both evidence kinds.
            if (depsRows.Count == 0)
            {
                e.Reasons.Add($"positive evidence: complete scan coverage AND {repo}'s own lockfile closure ({string.Join(", ", lockRows)}, no {Target} row) — transitive exposure ruled out by lockfile evidence, not by absence-of-match");
            }
            else
            {
                var fresh = _depsFreshness.GetValueOrDefault(repo) == "fresh-by-build" ? "fresh-by-build (built at the scanned commit)" : "fresh";
                var closureText = lockRows.Count > 0
                    ? $"lockfile closure ({string.Join(", ", lockRows)}) and build-resolved closure ({string.Join(", ", depsRows)})"
                    : $"build-resolved closure ({string.Join(", ", depsRows)}, no {Target} row)";
                e.Reasons.Add($"positive evidence: complete scan coverage AND {repo}'s own {closureText} — transitive exposure ruled out, closure evidenced by build output (deps.json), freshness: {fresh}");
            }
            kinds.AddRange(new[] { "package-evidence.v0", _lockKind, "scan-coverage" });
            corroboration = 2;
            e.EvidenceKinds = OrderKinds(kinds); e.Confidence = (rung, corroboration);
            return e;
        }
        if (e.Classification == "unknown")
        {
            e.ActionType = "unknown";
            // SPEC-019 §5.3: a stale build-resolved closure names both sides (HEAD commit vs build) —
            // it can neither prove exposure (no rule-c edge) nor safety (no closure credit)
            if (LockOf(repo).Any(r => r.Provenance is not null && !DepsFresh(repo))) // SPEC-019 §5.3: a stale build-resolved closure is WHY this repo is unknown — name both sides
                e.Reasons.Add("build evidence stale: deps.json closure predates the current commit — neither exposure nor safety can be proven from it");
            e.Reasons.Add("classification unknown: coverage gaps or unresolved exposure — never not-affected without positive evidence");
            kinds.Add("package-evidence.v0");
            e.EvidenceKinds = OrderKinds(kinds); e.Confidence = (rung, corroboration);
            return e;
        }

        // affected
        bool isProducer = letters.Contains('a');
        string? carrier = CarrierPkg(repo);
        bool carrierCorroborated = carrier is not null && !externalTarget;

        bool dMat = DMaterial(repo);
        if (isProducer) { kinds.Add("producer-evidence.v0"); rung = "fixture-declared"; }
        if (letters.Any(l => l is 'b' or 'c') || dMat) kinds.Add("package-evidence.v0");
        if (letters.Contains('c')) kinds.Add(_lockKind);
        if (dMat) kinds.Add("producer-evidence.v0");
        if (carrierCorroborated) { kinds.Add(_lockKind); kinds.Add("producer-evidence.v0"); }
        if (carrierCorroborated) corroboration = 2;
        if (carrier is not null) kinds.Add("producer-evidence.v0");

        var bFormat = bFact?.Format ?? "packagereference";
        foreach (var l in letters)
        {
            switch (l)
            {
                case 'a':
                    var pkgs = ProducedOf(repo);
                    if (VersioningDiffers(repo)) e.Reasons.Add($"produces {Target} (delta target); release unit {repo} = {Range(pkgs)} (one unit, assumption flagged)");
                    else if (pkgs.Any(p2 => _pubStatus.ContainsKey((repo, p2)))) e.Reasons.Add($"produces {Target} (delta target); unit = {Range(pkgs)}, basis assumed-same-repo");
                    else if (pkgs.Count > 1) e.Reasons.Add($"produces {Target} (delta target) via producer-evidence.v0; release unit {repo} = {Range(pkgs)}");
                    else if (e.Ownership == "external") e.Reasons.Add($"produces {Target} (delta target) via producer-evidence.v0");
                    else e.Reasons.Add($"produces {Target} (delta target)");
                    break;
                case 'b':
                    if (bFormat == "cpm")
                    {
                        static string Bare(string p2) => p2.Contains('/') ? p2[(p2.LastIndexOf('/') + 1)..] : p2;
                        var central = facts.FirstOrDefault(f => f.ConstraintSource == "Directory.Packages.props");
                        if (central is not null)
                            e.Reasons.Add($"central package management: single pin edit in {central.Path} fans out to {string.Join(" + ", central.Projects.Select(Bare))}");
                        var ov = facts.FirstOrDefault(f => f.ConstraintSource == "VersionOverride");
                        if (ov is not null)
                            e.Reasons.Add($"{ov.Projects.Select(Bare).FirstOrDefault() ?? "project"} carries an explicit VersionOverride pinning {Target} = {ov.DeclaredConstraint} ({ov.Path}) — the override is visible and must be updated or consciously kept");
                    }
                    else if (bFormat == "packages.config")
                        e.Reasons.Add($"packages.config reference to {Target} ({bFact!.Path}{(bFact.Tfm is not null ? $", {bFact.Tfm}" : "")}) — legacy format ingested as a first-class edge");
                    else if (externalTarget && facts.Count == 1 && carrier is not null)
                        e.Reasons.Add($"direct reference to {Target} ({bFact!.Path}); produces {carrier} — bump + republish flows the update");
                    else if (!externalTarget && carrier is not null)
                    {
                        var corrRepo = _lockRows.First(kv => kv.Value.Any(r => r.PackageId == Target && r.Via == carrier)).Key;
                        e.Reasons.Add($"direct reference to {Target} on the producer side ({bFact!.Path}); produces {carrier} — {corrRepo}'s lockfile corroborates that {carrier}'s closure carries {Target} (transitive via {carrier})");
                    }
                    else if (facts.Any(f => f.PackageId != Target))
                        e.Reasons.Add($"direct references to {string.Join(" and ", facts.Select(f => f.PackageId).Distinct().OrderBy(p2 => p2))}; the delta targets {Target}");
                    else
                        e.Reasons.Add($"direct reference to {Target} ({bFact!.Path})");
                    break;
                case 'c':
                    // SPEC-007 §5.3: rule (c) is established by a transitive row; narrate THAT row's
                    // evidence — never a direct row's absent via (mixed-relation case, F11).
                    var via = (LockProvable(repo).FirstOrDefault(r => r.PackageId == Target && r.Type == "transitive")
                               ?? LockProvable(repo).First(r => r.PackageId == Target)).Via;
                    var direct = via is not null
                        ? (LockOf(repo).FirstOrDefault(r => r.PackageId == via && r.Type == "direct") is { } vp ? vp.PackageId : null)
                        : null;
                    if (via is null)
                        e.Reasons.Add($"transitive exposure to {Target} evidenced by {repo}'s lockfile rows (parent not evidenced — dependencyNames absent or exceeded 256 chars)");
                    else if (externalTarget)
                        e.Reasons.Add($"transitive path {repo} -> {direct ?? via} -> {Target} evidenced by {repo}'s lockfile rows ({Target} transitive via {direct ?? via})");
                    else
                        e.Reasons.Add($"transitive path {repo} -> {via} -> {Target} reconstructed from {repo}'s own lockfile ({direct ?? via} direct; {Target} transitive via {via})");
                    break;
                case 'd':
                    if (!dMat) break;
                    var producer2 = producers.FirstOrDefault();
                    var depPkg = depsForD(repo);
                    if (depPkg.producer == producer2 && producer2 is not null)
                        e.Reasons.Add($"direct reference to {depPkg.pkg} (same release unit as the delta target — unit-level republish affects {depPkg.pkg} consumers)");
                    else
                        e.Reasons.Add($"consumes {depPkg.pkg} produced by {depPkg.producer}; {depPkg.producer} is affected — unit ripple: {repo} must rebuild/bump to consume the republished {depPkg.pkg}");
                    break;
            }
        }
        // content-exposure reason for lockfile-less ripple consumers (F1/F1b/F4b R3)
        if (dMat && !_lockRows.ContainsKey(repo) && !letters.Contains('c') && depsForD(repo).producer != producers.FirstOrDefault())
            e.Reasons.Add($"transitive CONTENT exposure to {Target} via {depsForD(repo).pkg} remains UNKNOWN (no lockfile) — recorded as a gap, never as not-affected");

        // qualifiers
        if (contradiction0(producers) && isProducer)
        {
            int claimIdx = producers.IndexOf(repo);
            e.Reasons.Insert(0, claimIdx == 0
                ? $"producer claim for {Target} (claim A of contradiction C1) — BOTH claims retained; neither outranks the other for scheduling"
                : $"producer claim for {Target} (claim B of contradiction C1) — BOTH claims retained");
            e.Reasons.RemoveAll(r => r.StartsWith("produces "));
            e.ActionType = "unknown";
        }
        if (e.Ownership == "external")
        {
            e.Reasons.Add($"ownership.v0 declares {repo} = {TeamOf(repo)} (external to {_ow.SelfTeamId}): rendered as coordination, NOT executable work for our team");
            kinds.Add("ownership.v0");
            e.ActionType = "external-request-await";
        }
        else if (e.Ownership == "unknown")
        {
            e.Reasons.Add($"repo ABSENT from ownership.v0 — ownership unknown, never assumed ours; coordination target unidentified, so no executable action is scheduled");
            e.ActionType = "unknown";
        }
        else if (producerGated && !isProducer)
        {
            if (producers.Count == 0)
            {
                var bIdx = e.Reasons.FindIndex(r => r.StartsWith("direct reference to"));
                if (bIdx >= 0) e.Reasons[bIdx] += " — affected-ness is consumer-side and fully evidenced";
                e.Reasons.Add($"no producer wave possible: producer of {Target} unknown");
                if (e.Reasons.Count > 1 && e.Reasons[0].StartsWith("direct reference")) e.Reasons[0] = e.Reasons[0].Replace("direct reference to", "direct reference to"); // F3a keeps both reasons
                e.ActionType = "unknown";
            }
            else
            {
                e.Reasons.Add($"downstream of a CONTRADICTED producer — appears only in a provisional wave blocked on C1; no firm prerequisite on either claim");
                e.ActionType = "unknown";
            }
        }
        else if (e.ActionType == "") e.ActionType = "executable";

        // coverage-gap qualifier replaces the plain b-reason with the OBSERVED form
        var cov = _coverage.GetValueOrDefault(repo);
        if (cov?.Status == "gaps" && bFact is not null)
        {
            e.Reasons.RemoveAll(r => r.StartsWith("direct reference to") || r.StartsWith("packages.config reference to"));
            var gap = cov.Gaps.FirstOrDefault() ?? "unscanned area";
            if (bFact.Format == "packages.config")
            {
                var shortGap = gap.Contains(" not scanned") ? gap[..gap.IndexOf(" not scanned")] : gap;
                e.Reasons.Insert(0, $"OBSERVED packages.config reference to {Target} ({bFact.Path}) — observed edge is positive evidence of affected-ness; coverage gaps ({shortGap}) qualify additional unknown work, they do not erase the observed edge");
            }
            else
                e.Reasons.Insert(0, $"OBSERVED direct reference to {Target} ({bFact.Path}) — an observed reference is positive evidence of affected-ness; coverage gaps qualify HOW MUCH work is uncertain, they do not erase the observed edge");
            kinds.Add("scan-coverage");
        }
        else if (e.Ownership == "unknown" && bFact is not null)
        {
            e.Reasons.RemoveAll(r => r.StartsWith("direct reference to") || r.StartsWith("packages.config reference to"));
            e.Reasons.Insert(0, $"OBSERVED direct reference to {Target} ({bFact.Path})");
        }
        if (externalTarget && letters.Contains('b'))
            e.Reasons.Add($"{Target} attribution: DECLARED external via producer-evidence.v0 externalPackages (evidence-backed, not inferred from producer absence — absence alone means unknown, per F3a)");

        e.EvidenceKinds = OrderKinds(kinds);
        e.Confidence = (rung, corroboration);
        return e;
    }

    static bool contradiction0(List<string> producers) => producers.Count >= 2;

    (string producer, string pkg) depsForD(string repo) =>
        _dEdge.TryGetValue(repo, out var e) ? (e.Producer, e.Pkg) : ("?", "?");
    // d is "material" (emitted) only when the causative edge is NOT the target package itself
    // (a b/c reason already covers consuming the target; goldens show no doubled reason there)
    bool DMaterial(string repo)
    {
        var l = letters0.GetValueOrDefault(repo, new List<char>());
        return l.Contains('d') && !l.Contains('a') && !l.Contains('b') && !l.Contains('c');
    }

    static List<string> OrderKinds(List<string> kinds)
    {
        var order = new[] { "package-evidence.v0", "producer-evidence.v0", "lockfile-rows.v0", "lockfile-rows.v1", "lockfile-rows.v2", "ownership.v0", "scan-coverage" };
        return kinds.Distinct().OrderBy(k => Array.IndexOf(order, k)).ToList();
    }

    List<PlanGap> BuildGaps(HashSet<string> affected, List<string> producers, bool externalTarget, Dictionary<string, List<char>> letters, Dictionary<string, List<(string Repo, string Pkg)>> deps)
    {
        var gaps = new List<PlanGap>();
        // SPEC-012 §2.3: coverage gaps also surface for lockfile-evidenced (rule-c) repos — a FailedOrPartial
        // scan of a repo affected purely via its lockfile must not be invisible in the plan (G2).
        foreach (var (repo, cov) in _coverage.Where(kv => kv.Value.Status == "gaps"
                     && letters.GetValueOrDefault(kv.Key, new List<char>()).Contains('c')
                     && !FactsOf(kv.Key).Any(f => f.PackageId == Target)) // rule-b repos keep their existing path
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            gaps.Add(new PlanGap { Subject = $"{repo} coverage (lockfile-evidenced)", Detail = $"scan gaps on {cov.Gaps.FirstOrDefault() ?? "unscanned area"} — {repo} is affected via lockfile-evidenced transitive exposure; the gaps qualify how much additional work is unknown, they do not erase the evidenced exposure" });
        }
        foreach (var (repo, cov) in _coverage.Where(kv => kv.Value.Status == "gaps" && FactsOf(kv.Key).Any(f => f.PackageId == Target)).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var gap = cov.Gaps.FirstOrDefault() ?? "unscanned area";
            var fact = FactsOf(repo).First(f => f.PackageId == Target);
            if (fact.Format == "packages.config")
            {
                var shortGap = gap.Contains(" not scanned") ? gap[..gap.IndexOf(" not scanned")] + " not scanned" : gap;
                gaps.Add(new PlanGap { Subject = $"{repo} coverage", Detail = $"{shortGap} — {repo} still affected and scheduled on its observed edge; additional edges/work in the unscanned area remain unknown" });
            }
            else
                gaps.Add(new PlanGap { Subject = $"{repo} coverage", Detail = $"scan gaps on {gap.Replace(" not scanned", "")} — {repo} still affected (observed edge) and scheduled; additional edges/work in the unscanned area remain unknown" });
        }
        // content-exposure gaps: lockfile-less unit-ripple consumers whose dependency's
        // producer is NOT the target producer (F1/F1b/F4b R3; F8 R10 exempt — its dep IS the target unit)
        foreach (var repo in affected.Where(r2 => DMaterial(r2) && !_lockRows.ContainsKey(r2)).OrderBy(r2 => r2, StringComparer.Ordinal))
        {
            var dep = depsForD(repo);
            if (dep.producer == producers.FirstOrDefault()) continue;
            gaps.Add(new PlanGap { Subject = $"{repo} content exposure", Detail = $"no lockfile-rows for {repo}; whether {Target} itself flows into {repo} via {dep.pkg} is unresolved — ripple obligation stands (affected), content exposure stays unknown (gap), never not-affected" });
            gaps.Add(new PlanGap { Subject = $"{dep.pkg} -> {Target} dependency edge", Detail = $"{dep.producer}'s own reference to {Target} is evidenced, but {dep.pkg}'s manifest dependency on {Target} is a future rung (nuspec-in-index); only {repo}'s lockfile (absent here) could evidence the consumer-side path" });
        }
        foreach (var repo in affected.Where(r => !_team.ContainsKey(r)).OrderBy(r => r, StringComparer.Ordinal))
            gaps.Add(new PlanGap { Subject = $"{repo} ownership", Detail = $"{repo} absent from ownership.v0 — ownership unknown (never 'ours'); {repo} is NOT scheduled into a wave until ownership is established" });
        if (producers.Count == 0 && !externalTarget)
            gaps.Add(new PlanGap { Subject = $"{Target} producer", Detail = $"no pinned input declares who produces {Target}; zero producers invented (no heuristic, no name inference)" });
        if (externalTarget)
            gaps.Add(new PlanGap { Subject = $"{Target} producer", Detail = $"{Target} is declared external via producer-evidence.v0 externalPackages — attribution is evidence-backed; no scanner/advisory input is claimed (V0 does not ingest scanners)" });
        // F1b release-coupling gap
        foreach (var repo in _produced.Keys.Where(VersioningDiffers).OrderBy(r => r, StringComparer.Ordinal))
            gaps.Add(new PlanGap { Subject = $"{repo} release coupling", Detail = VersioningGapDetail(repo) });
        return gaps;
    }

    // ---------------- SPEC-007 findings (evidenced version disagreements) ----------------

    // T10/T10a (SPEC-007 §6). Disagreement = >1 distinct non-null, non-empty version among the
    // lockfile repo's rows for a package; null versions are unevidenced and never count/render.
    List<PlanFinding> BuildFindings()
    {
        var result = new List<PlanFinding>();
        var trailerShown = new HashSet<string>(StringComparer.Ordinal); // SPEC-012 §2.2: T10a once per repo (subject-first finding)
        foreach (var lf in _lockRepos) // SPEC-008 §5.2: findings are per-repo internal — cross-repo differences are never findings
        {
            var repo = lf.Repo;
            bool unsupportedGroups = _coverage.GetValueOrDefault(repo)?.Status == "gaps" &&
                _coverage[repo].Gaps.Any(g => g.Contains("packages-lock-group-unsupported") || g.Contains("target-framework group is unsupported"));
            static string Ord(string? s) => s ?? "";
            foreach (var grp in lf.Rows.GroupBy(r => r.PackageId))
            {
                var versions = grp.Where(r => !string.IsNullOrEmpty(r.Version)).Select(r => r.Version!).Distinct().ToList();
                if (versions.Count < 2) continue;
                // SPEC-012 §2.1: from-version markers appear ONLY on the delta package's finding — an
                // unrelated package coincidentally on oldVersion is not about the delta.
                var markFromVersion = string.Equals(grp.Key, Target, StringComparison.OrdinalIgnoreCase); // NuGet ids are case-insensitive throughout the pipeline
                var resolutions = grp.Where(r => !string.IsNullOrEmpty(r.Version))
                    .OrderBy(r => Ord(r.Lockfile), StringComparer.Ordinal).ThenBy(r => Ord(r.Tfm), StringComparer.Ordinal)
                    .Select(r => markFromVersion && Apply.CompareCore(r.Version!, _c.OldVersion) == 0
                        ? $"{r.Version} in {Ord(r.Lockfile)} ({Ord(r.Tfm)}) [delta from-version]"
                        : $"{r.Version} in {Ord(r.Lockfile)} ({Ord(r.Tfm)})")
                    .ToList();
                var detail = $"{repo} resolves {grp.Key} to {versions.Count} versions across its lockfile/TFM resolution groups: {string.Join("; ", resolutions)} — evidenced disagreement, not an error; V0 schedules {repo} once at unit level and picks no winner (convergence is deliberately out of scope, §10)";
                if (unsupportedGroups && trailerShown.Add(repo)) // once per repo, and this loop is subject-sorted per repo
                    detail += $"; {repo} also has lockfile groups the scanner could not parse and contributed no rows — those resolutions stay unevidenced (coverage gap)";
                result.Add(new PlanFinding { Subject = $"{repo} version disagreement: {grp.Key}", Detail = detail });
            }
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Subject + "\0" + a.Detail, b.Subject + "\0" + b.Detail));
        return result;
    }

    // ---- templates (SPEC-003 §4.1) ----
    static string T1(string producer, string pkg, string version) => $"{producer} publishes {pkg} >= {version} (stated, not verified live)";
    static string T4(string producer, string pkg, string dep, string depVersion) => $"{producer} publishes {pkg} rebuilt against {dep} {depVersion} (stated, not verified live)";
    static string T5(string pkg, string version) => $"{pkg} {version} available from the public feed ({pkg} declared external via producer-evidence.v0; stated, not verified live)";
    static string T6(string team, string pkg, string version) => $"external {team} releases {pkg} >= {version} (request/await — outside our change authority; escalation path is the coordination step, not a PR)";
    static string T6a(string pkg, string version) => $"{pkg} >= {version} available and restorable from the consumer environment (stated, not verified live)";
    static string T7(string pkg, string repo) => $"awaiting publication of {pkg}: producer-evidence.v0 declares {pkg} publicationStatus = unpublished — {repo}'s wave can never be 'ready' while the sidecar says unpublished";
    static string T7a(string producer, string pkg) => $"{producer} publishes {pkg} (cite: producer-evidence.v0 {pkg} publicationStatus = unpublished; stated, not verified live)";
    static string T8(string pkg, string repo, string version) => $"producer of {pkg} is unknown — no producer wave is derivable; {repo}'s wave proceeds only after the producer is identified and {pkg} {version} is available";
    static string T8a(string pkg, string version) => $"{pkg} {version} exists on the configured feed and authenticated restore succeeds (stated, not verified live)";
    static string T9(string cid, string pkg, string version) => $"{cid} resolved: authoritative producer of {pkg} determined by human decision; then {pkg} {version} published (stated, not verified live)";
}

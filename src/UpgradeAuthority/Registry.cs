using System.Text;

namespace UpgradeAuthority;

// SPEC-020: estate producer registry — the "us vs the internet" boundary. Assembles the fused
// producer evidence (provenance recorded at fusion time by ingest), cross-references everything
// the estate consumes against it, and reports internal/external/unknown visibly: unknowns and
// anomalies are report content, never silently resolved, never failures. The boundary is estate
// evidence vs declared-external — no feed lookups, no network ("stated, not verified live").
public static class Registry
{
    // §3: the ci-defined availability note — "not yet available" until the tracemap CI-workflow slice
    // lands (§2b). The moment entries carry ci-defined provenance, the source IS represented and the
    // note switches — the report never says "not yet available" over a nonzero count.
    public const string CiDefinedNote = "ci-defined source not yet available — project-declared + operator only";
    public const string CiDefinedPresentNote = "ci-defined entries present — recorded at fusion (the tracemap CI-workflow slice's consuming contract: SPEC-020 §2b)";

    public sealed class ProducerRow
    {
        public string Repo = "";
        public string PackageId = "";
        public string? ProducedVersion; // omitted when null — a declaration without a version is honest
        public string Provenance = "";
    }

    public sealed class RegistryReport
    {
        public List<ProducerRow> Producers = new(); // ordinal by (repo, packageId)
        public List<string> InternalPackages = new(); // produced by the estate (any provenance)
        public List<string> ExternalPackages = new(); // declared external AND not produced
        public List<string> UnknownPackages = new(); // consumed, produced nowhere, declared nowhere
        public List<string> ProducedAndDeclaredExternal = new(); // both — surfaced, never silently resolved
        public int ProjectDeclared;
        public int OperatorDeclared;
        public int CiDefined;
    }

    // ---- entry: returns (canonical, rc). rc != 0 → canonical null, message already printed. ----
    public static (string? canonical, int rc) Run(string fixtureDir)
    {
        Engine engine;
        try { engine = Program.LoadEngine(fixtureDir); } // delta.json = loader precondition only; content never read here
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return (null, 3); }
        RegistryReport report;
        try { report = Build(engine); }
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return (null, 3); }
        return (Write(report), 0);
    }

    // Untrusted values (sidecar fields) reach diagnostics verbatim; control characters would forge
    // terminal/log line structure the redactor does not escape (house rule, PR #3 Baz round 1).
    static string Esc(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
            sb.Append(c < 0x20 || c == 0x7f ? $"\\u{(int)c:x4}" : c);
        return sb.ToString();
    }

    static RegistryReport Build(Engine engine)
    {
        var report = new RegistryReport();

        // producers — post-fusion, as the planner sees it (repo identity normalized, entry spelling kept;
        // case-variant duplicates stay visible — the fusion already warned, the report never merges them away)
        foreach (var p in engine.ProducersView)
        {
            var provenance = p.Provenance ?? Ingest.MarkerProvenance(p); // pre-spec entries: in-band marker classifies
            if (provenance is not ("project-declared" or "operator-declared" or "ci-defined"))
                throw new UaException($"malformed input producer-evidence.v0.json: producer '{Esc(p.Repo)}/{Esc(p.PackageId)}' provenance '{Esc(provenance)}' not in project-declared|operator-declared|ci-defined");
            report.Producers.Add(new ProducerRow { Repo = engine.RepoIdentity(p.Repo), PackageId = p.PackageId, ProducedVersion = p.ProducedVersion, Provenance = provenance });
            if (provenance == "project-declared") report.ProjectDeclared++;
            else if (provenance == "operator-declared") report.OperatorDeclared++;
            else report.CiDefined++;
        }
        report.Producers.Sort((a, b) => string.CompareOrdinal(a.Repo + "\0" + a.PackageId, b.Repo + "\0" + b.PackageId));

        // consumption evidence — ANY consumer fact or ANY lockfile-class row counts (null-version rows
        // included: the packageId is the evidence). Spelling sets mirror Drift's rule: lockfile > deps.json
        // > declared-pin (ordinal-first within each), extended with any-fact (range-only consumers) below
        // the pins and the producer/declared spellings below all consumption evidence.
        var lockSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var depsSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var pinSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var factSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var producerSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var declaredSpellings = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        void Note(Dictionary<string, SortedSet<string>> sets, string id)
        {
            if (!sets.TryGetValue(id, out var set)) sets[id] = set = new SortedSet<string>(StringComparer.Ordinal);
            set.Add(id);
        }

        foreach (var (_, rows) in engine.LockRowsView)
            foreach (var row in rows)
                Note(row.Provenance is null ? lockSpellings : depsSpellings, row.PackageId); // null-version rows count too (SPEC-020 §3)
        foreach (var (_, facts) in engine.FactsView)
            foreach (var f in facts)
            {
                Note(factSpellings, f.PackageId);
                if (Apply.IsExactPin(f.DeclaredConstraint)) Note(pinSpellings, f.PackageId);
            }
        foreach (var p in engine.ProducersView) Note(producerSpellings, p.PackageId);
        foreach (var e in engine.ExternalView) Note(declaredSpellings, e.PackageId);

        string SpellingOf(string key) =>
            lockSpellings.TryGetValue(key, out var ls) && ls.Count > 0 ? ls.Min!
            : depsSpellings.TryGetValue(key, out var ds) && ds.Count > 0 ? ds.Min!
            : pinSpellings.TryGetValue(key, out var ps) && ps.Count > 0 ? ps.Min!
            : factSpellings.TryGetValue(key, out var fs) && fs.Count > 0 ? fs.Min!
            : producerSpellings.TryGetValue(key, out var pr) && pr.Count > 0 ? pr.Min!
            : declaredSpellings.TryGetValue(key, out var dx) && dx.Count > 0 ? dx.Min!
            : key;

        var produced = producerSpellings.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var declared = declaredSpellings.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        consumed.UnionWith(lockSpellings.Keys); consumed.UnionWith(depsSpellings.Keys);
        consumed.UnionWith(pinSpellings.Keys); consumed.UnionWith(factSpellings.Keys);

        report.InternalPackages = produced.Select(SpellingOf).ToList();
        report.ExternalPackages = declared.Except(produced).Select(SpellingOf).ToList();
        report.UnknownPackages = consumed.Except(produced).Except(declared).Select(SpellingOf).ToList();
        report.ProducedAndDeclaredExternal = produced.Intersect(declared).Select(SpellingOf).ToList();
        report.InternalPackages.Sort(StringComparer.Ordinal);
        report.ExternalPackages.Sort(StringComparer.Ordinal);
        report.UnknownPackages.Sort(StringComparer.Ordinal);
        report.ProducedAndDeclaredExternal.Sort(StringComparer.Ordinal);
        return report;
    }

    // ---- canonical registry.v1 writer (house style: 2-space, LF, trailing newline, fixed key order) ----
    public static string Write(RegistryReport r)
    {
        var sb = new StringBuilder();
        sb.Append('{').Append('\n');
        sb.Append("  \"schemaVersion\": \"registry.v1\",\n");
        sb.Append("  \"producers\": ");
        if (r.Producers.Count == 0) sb.Append("[]");
        else
        {
            sb.Append('[').Append('\n');
            for (int i = 0; i < r.Producers.Count; i++)
            {
                var p = r.Producers[i];
                sb.Append("    {").Append('\n');
                sb.Append("      \"repo\": ").Append(Q(p.Repo)).Append(',').Append('\n');
                sb.Append("      \"packageId\": ").Append(Q(p.PackageId)).Append(',').Append('\n');
                if (p.ProducedVersion is not null) sb.Append("      \"producedVersion\": ").Append(Q(p.ProducedVersion)).Append(',').Append('\n');
                sb.Append("      \"provenance\": ").Append(Q(p.Provenance)).Append('\n');
                sb.Append("    }").Append(i < r.Producers.Count - 1 ? "," : "").Append('\n');
            }
            sb.Append("  ]");
        }
        sb.Append(',').Append('\n');
        sb.Append("  \"boundary\": {").Append('\n');
        StringArray(sb, "    ", "internalPackages", r.InternalPackages, trailingComma: true);
        StringArray(sb, "    ", "externalPackages", r.ExternalPackages, trailingComma: true);
        StringArray(sb, "    ", "unknownPackages", r.UnknownPackages, trailingComma: true);
        sb.Append("    \"anomalies\": {").Append('\n');
        StringArray(sb, "      ", "producedAndDeclaredExternal", r.ProducedAndDeclaredExternal, trailingComma: false);
        sb.Append("    }").Append('\n');
        sb.Append("  },").Append('\n');
        sb.Append("  \"provenanceSummary\": {").Append('\n');
        sb.Append($"    \"projectDeclared\": {r.ProjectDeclared},").Append('\n');
        sb.Append($"    \"operatorDeclared\": {r.OperatorDeclared},").Append('\n');
        sb.Append($"    \"ciDefined\": {r.CiDefined},").Append('\n');
        sb.Append("    \"note\": ").Append(Q(r.CiDefined > 0 ? CiDefinedPresentNote : CiDefinedNote)).Append('\n');
        sb.Append("  }").Append('\n');
        sb.Append('}').Append('\n');
        return sb.ToString();
    }

    static void StringArray(StringBuilder sb, string indent, string name, List<string> items, bool trailingComma)
    {
        sb.Append(indent).Append('"').Append(name).Append("\": ");
        if (items.Count == 0) sb.Append("[]");
        else
        {
            sb.Append('[').Append('\n');
            for (int i = 0; i < items.Count; i++)
                sb.Append(indent).Append("  ").Append(Q(items[i])).Append(i < items.Count - 1 ? "," : "").Append('\n');
            sb.Append(indent).Append(']');
        }
        if (trailingComma) sb.Append(',');
        sb.Append('\n');
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
}

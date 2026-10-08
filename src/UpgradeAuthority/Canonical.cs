using System.Text;

namespace UpgradeAuthority;

// Canonical serializer mirroring tools/canonicalize-goldens.mjs byte-for-byte:
// 2-space indent, fully expanded, fixed key order (schema order), LF endings,
// trailing newline, minimal string escaping (literal non-ASCII, e.g. em dashes).
public static class Canonical
{
    public static string Write(Plan plan)
    {
        var sb = new StringBuilder();
        WritePlan(sb, plan, 0);
        sb.Append('\n');
        return sb.ToString();
    }

    static void WritePlan(StringBuilder sb, Plan p, int ind)
    {
        sb.Append('{').Append('\n');
        Str(sb, ind + 1, "schemaVersion"); sb.Append(": "); Str(sb, p.SchemaVersion); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "delta"); sb.Append(": "); WriteDelta(sb, p.Delta, ind + 1); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "repos"); sb.Append(": ");
        if (p.Repos.Count == 0) { sb.Append("[]"); } else { sb.Append("[").Append('\n'); for (int i = 0; i < p.Repos.Count; i++) { Pad(sb, ind + 2); WriteRepo(sb, p.Repos[i], ind + 2); sb.Append(i < p.Repos.Count - 1 ? "," : "").Append('\n'); } Pad(sb, ind + 1); sb.Append(']'); }
        sb.Append(",").Append('\n');
        if (p.Scope != null) { Str(sb, ind + 1, "scope"); sb.Append(": "); WriteScope(sb, p.Scope, ind + 1); sb.Append(",").Append('\n'); }
        Str(sb, ind + 1, "waves"); sb.Append(": ");
        if (p.Waves.Count == 0) { sb.Append("[]"); } else { sb.Append("[").Append('\n'); for (int i = 0; i < p.Waves.Count; i++) { Pad(sb, ind + 2); WriteWave(sb, p.Waves[i], ind + 2); sb.Append(i < p.Waves.Count - 1 ? "," : "").Append('\n'); } Pad(sb, ind + 1); sb.Append(']'); }
        sb.Append(",").Append('\n');
        if (p.Stop != null) { Str(sb, ind + 1, "stop"); sb.Append(": "); WriteStop(sb, p.Stop, ind + 1); sb.Append(",").Append('\n'); }
        Str(sb, ind + 1, "uncertainty"); sb.Append(": "); WriteUncertainty(sb, p.Uncertainty, ind + 1);
        sb.Append('\n');
        Pad(sb, ind); sb.Append('}');
    }

    static void WriteDelta(StringBuilder sb, PlanDelta d, int ind)
    {
        sb.Append('{').Append('\n');
        Str(sb, ind + 1, "packageName"); sb.Append(": "); Str(sb, d.PackageName); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "ecosystem"); sb.Append(": "); Str(sb, d.Ecosystem); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "changeType"); sb.Append(": "); Str(sb, d.ChangeType); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "oldVersion"); sb.Append(": "); Str(sb, d.OldVersion); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "newVersion"); sb.Append(": "); Str(sb, d.NewVersion);
        if (d.Origin != null) { sb.Append(",").Append('\n'); Str(sb, ind + 1, "origin"); sb.Append(": "); Str(sb, d.Origin); }
        sb.Append('\n'); Pad(sb, ind); sb.Append('}');
    }

    static void WriteRepo(StringBuilder sb, PlanRepo r, int ind)
    {
        sb.Append('{').Append('\n');
        Str(sb, ind + 1, "repo"); sb.Append(": "); Str(sb, r.Repo); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "classification"); sb.Append(": "); Str(sb, r.Classification); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "reasons"); sb.Append(": "); StrArr(sb, r.Reasons, ind + 1); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "ownership"); sb.Append(": "); Str(sb, r.Ownership); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "actionType"); sb.Append(": "); Str(sb, r.ActionType); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "evidenceKinds"); sb.Append(": "); StrArr(sb, r.EvidenceKinds, ind + 1); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "confidence"); sb.Append(": ");
        sb.Append('{').Append('\n');
        Str(sb, ind + 2, "rung"); sb.Append(": "); Str(sb, r.Confidence.Rung); sb.Append(",").Append('\n');
        Str(sb, ind + 2, "corroboration"); sb.Append(": ").Append(r.Confidence.Corroboration);
        sb.Append('\n'); Pad(sb, ind + 1); sb.Append('}');
        if (r.ScanNotes is { Count: > 0 })
        {
            sb.Append(",").Append('\n');
            Str(sb, ind + 1, "scanNotes"); sb.Append(": "); StrArr(sb, r.ScanNotes, ind + 1);
        }
        sb.Append('\n'); Pad(sb, ind); sb.Append('}');
    }

    static void WriteScope(StringBuilder sb, PlanScope s, int ind)
    {
        sb.Append('{').Append('\n');
        Str(sb, ind + 1, "mode"); sb.Append(": "); Str(sb, s.Mode);
        if (s.IncludedCount is { } n) { sb.Append(",").Append('\n'); Str(sb, ind + 1, "includedCount"); sb.Append(": ").Append(n); }
        if (s.OutOfScope is { Count: > 0 }) { sb.Append(",").Append('\n'); Str(sb, ind + 1, "outOfScope"); sb.Append(": "); StrArr(sb, s.OutOfScope, ind + 1); }
        sb.Append('\n'); Pad(sb, ind); sb.Append('}');
    }

    static void WriteWave(StringBuilder sb, PlanWave w, int ind)
    {
        sb.Append('{').Append('\n');
        Str(sb, ind + 1, "index"); sb.Append(": ").Append(w.Index); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "status"); sb.Append(": "); Str(sb, w.Status); sb.Append(",").Append('\n');
        if (w.BlockedOn != null) { Str(sb, ind + 1, "blockedOn"); sb.Append(": "); Str(sb, w.BlockedOn); sb.Append(",").Append('\n'); }
        if (w.Condition != null) { Str(sb, ind + 1, "condition"); sb.Append(": "); Str(sb, w.Condition); sb.Append(",").Append('\n'); }
        Str(sb, ind + 1, "prerequisites"); sb.Append(": "); StrArr(sb, w.Prerequisites, ind + 1); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "releaseUnits"); sb.Append(": ");
        if (w.ReleaseUnits.Count == 0) { sb.Append("[]"); } else { sb.Append("[").Append('\n'); for (int i = 0; i < w.ReleaseUnits.Count; i++) { Pad(sb, ind + 2); WriteUnit(sb, w.ReleaseUnits[i], ind + 2); sb.Append(i < w.ReleaseUnits.Count - 1 ? "," : "").Append('\n'); } Pad(sb, ind + 1); sb.Append(']'); }
        sb.Append('\n'); Pad(sb, ind); sb.Append('}');
    }

    static void WriteUnit(StringBuilder sb, PlanUnit u, int ind)
    {
        sb.Append('{').Append('\n');
        Str(sb, ind + 1, "repo"); sb.Append(": "); Str(sb, u.Repo); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "packages"); sb.Append(": "); StrArr(sb, u.Packages, ind + 1); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "basis"); sb.Append(": "); Str(sb, u.Basis);
        if (u.Notes is { Count: > 0 }) { sb.Append(",").Append('\n'); Str(sb, ind + 1, "notes"); sb.Append(": "); StrArr(sb, u.Notes, ind + 1); }
        if (u.PublicationStatus is { Count: > 0 })
        {
            sb.Append(",").Append('\n'); Str(sb, ind + 1, "publicationStatus"); sb.Append(": ");
            sb.Append('{').Append('\n');
            var keys = u.PublicationStatus.Keys.ToList();
            for (int i = 0; i < keys.Count; i++) { Str(sb, ind + 2, keys[i]); sb.Append(": "); Str(sb, u.PublicationStatus[keys[i]]); sb.Append(i < keys.Count - 1 ? "," : "").Append('\n'); }
            Pad(sb, ind + 1); sb.Append('}');
        }
        sb.Append('\n'); Pad(sb, ind); sb.Append('}');
    }

    static void WriteStop(StringBuilder sb, PlanStop s, int ind)
    {
        sb.Append('{').Append('\n');
        Str(sb, ind + 1, "reason"); sb.Append(": "); Str(sb, s.Reason); sb.Append(",").Append('\n');
        Str(sb, ind + 1, "detail"); sb.Append(": "); Str(sb, s.Detail);
        if (s.CyclePath is { Count: > 0 }) { sb.Append(",").Append('\n'); Str(sb, ind + 1, "cyclePath"); sb.Append(": "); StrArr(sb, s.CyclePath, ind + 1); }
        sb.Append('\n'); Pad(sb, ind); sb.Append('}');
    }

    static void WriteUncertainty(StringBuilder sb, PlanUncertainty u, int ind)
    {
        sb.Append('{').Append('\n');
        Str(sb, ind + 1, "contradictions"); sb.Append(": ");
        if (u.Contradictions.Count == 0) { sb.Append("[]"); } else
        {
            sb.Append("[").Append('\n');
            for (int i = 0; i < u.Contradictions.Count; i++)
            {
                var c = u.Contradictions[i]; Pad(sb, ind + 2); sb.Append('{').Append('\n');
                Str(sb, ind + 3, "id"); sb.Append(": "); Str(sb, c.Id); sb.Append(",").Append('\n');
                Str(sb, ind + 3, "subject"); sb.Append(": "); Str(sb, c.Subject); sb.Append(",").Append('\n');
                Str(sb, ind + 3, "claims"); sb.Append(": ");
                sb.Append("[").Append('\n');
                for (int j = 0; j < c.Claims.Count; j++) { Pad(sb, ind + 4); sb.Append('{').Append('\n'); Str(sb, ind + 5, "repo"); sb.Append(": "); Str(sb, c.Claims[j].Repo); sb.Append(",").Append('\n'); Str(sb, ind + 5, "packageId"); sb.Append(": "); Str(sb, c.Claims[j].PackageId); sb.Append('\n'); Pad(sb, ind + 4); sb.Append('}').Append(j < c.Claims.Count - 1 ? "," : "").Append('\n'); }
                Pad(sb, ind + 3); sb.Append(']');
                if (c.DownstreamProvisional is { Count: > 0 }) { sb.Append(",").Append('\n'); Str(sb, ind + 3, "downstreamProvisional"); sb.Append(": "); StrArr(sb, c.DownstreamProvisional, ind + 3); }
                sb.Append('\n'); Pad(sb, ind + 2); sb.Append('}').Append(i < u.Contradictions.Count - 1 ? "," : "").Append('\n');
            }
            Pad(sb, ind + 1); sb.Append(']');
        }
        sb.Append(",").Append('\n');
        Str(sb, ind + 1, "gaps"); sb.Append(": ");
        if (u.Gaps.Count == 0) { sb.Append("[]"); } else
        {
            sb.Append("[").Append('\n');
            for (int i = 0; i < u.Gaps.Count; i++) { Pad(sb, ind + 2); sb.Append('{').Append('\n'); Str(sb, ind + 3, "subject"); sb.Append(": "); Str(sb, u.Gaps[i].Subject); sb.Append(",").Append('\n'); Str(sb, ind + 3, "detail"); sb.Append(": "); Str(sb, u.Gaps[i].Detail); sb.Append('\n'); Pad(sb, ind + 2); sb.Append('}').Append(i < u.Gaps.Count - 1 ? "," : "").Append('\n'); }
            Pad(sb, ind + 1); sb.Append(']');
        }
        if (u.Findings is { Count: > 0 })
        {
            sb.Append(",").Append('\n');
            Str(sb, ind + 1, "findings"); sb.Append(": ");
            sb.Append("[").Append('\n');
            for (int i = 0; i < u.Findings.Count; i++) { Pad(sb, ind + 2); sb.Append('{').Append('\n'); Str(sb, ind + 3, "subject"); sb.Append(": "); Str(sb, u.Findings[i].Subject); sb.Append(",").Append('\n'); Str(sb, ind + 3, "detail"); sb.Append(": "); Str(sb, u.Findings[i].Detail); sb.Append('\n'); Pad(sb, ind + 2); sb.Append('}').Append(i < u.Findings.Count - 1 ? "," : "").Append('\n'); }
            Pad(sb, ind + 1); sb.Append(']');
        }
        sb.Append('\n'); Pad(sb, ind); sb.Append('}');
    }

    static void StrArr(StringBuilder sb, List<string> items, int ind)
    {
        if (items.Count == 0) { sb.Append("[]"); return; }
        sb.Append("[").Append('\n');
        for (int i = 0; i < items.Count; i++) { Pad(sb, ind + 1); Str(sb, items[i]); sb.Append(i < items.Count - 1 ? "," : "").Append('\n'); }
        Pad(sb, ind); sb.Append(']');
    }

    static void Pad(StringBuilder sb, int ind) { for (int i = 0; i < ind * 2; i++) sb.Append(' '); }

    static void Str(StringBuilder sb, int ind, string key) { Pad(sb, ind); Str(sb, key); }

    static void Str(StringBuilder sb, string key)
    {
        sb.Append('"');
        foreach (var ch in key)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4"));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }
}

using System.Text;

namespace UpgradeAuthority;

// SPEC-004 §3: deterministic markdown rendering of a plan. Pure function of the
// plan model — same plan bytes ⇒ same report bytes (LF, trailing newline).
// Dynamic text never forges structure: renderer constants own every heading and
// bullet marker; dynamic text passes through Cell() (tables) or Text() (bullets).
public static class Report
{
    // GFM table cell: backslash-escape, pipe-escape, collapse line breaks to one space.
    static string Cell(string s) => s.Replace("\\", "\\\\").Replace("|", "\\|").Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
    // Bullet/line text: collapse line breaks (no forged bullets/headings from input).
    static string Text(string s) => s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');

    public static string Render(Plan p)
    {
        var sb = new StringBuilder();
        sb.Append("# Impact plan: ").Append(Text(p.Delta.PackageName)).Append(' ').Append(Text(p.Delta.OldVersion)).Append(" → ").Append(Text(p.Delta.NewVersion)).Append('\n');

        var affected = p.Repos.Count(r => r.Classification == "affected");
        var notAffected = p.Repos.Count(r => r.Classification == "not-affected");
        var unknown = p.Repos.Count(r => r.Classification == "unknown");
        sb.Append('\n').Append("## Summary").Append('\n');
        sb.Append("- Delta: ").Append(Text(p.Delta.PackageName)).Append(' ').Append(Text(p.Delta.ChangeType)).Append(' ')
          .Append(Text(p.Delta.OldVersion)).Append(" → ").Append(Text(p.Delta.NewVersion)).Append(" (").Append(Text(p.Delta.Ecosystem)).Append(')');
        if (p.Delta.Origin is not null) sb.Append(" — origin: ").Append(Text(p.Delta.Origin));
        sb.Append('\n');
        sb.Append("- Repositories: ").Append(affected).Append(" affected · ").Append(notAffected).Append(" not-affected · ").Append(unknown).Append(" unknown").Append('\n');
        if (p.Scope != null) // SPEC-017 §5: config echo — out-of-scope repos claim no classification, never not-affected
        {
            var excluded = p.Scope.OutOfScope ?? new List<string>();
            if (p.Scope.Mode == "include")
            {
                sb.Append("- Scope: include-mode — ").Append(p.Scope.IncludedCount ?? 0).Append(" repos in scope by config");
                if (excluded.Count > 0) sb.Append(", ").Append(excluded.Count).Append(" out of scope by config: ").Append(string.Join(", ", excluded.Select(Text)));
                sb.Append('\n');
            }
            else
                sb.Append("- Scope: ").Append(excluded.Count).Append(" repos out of scope by config: ").Append(string.Join(", ", excluded.Select(Text))).Append('\n');
        }
        if (p.Uncertainty.Findings is { Count: > 0 }) // SPEC-007: emitted only when findings exist — existing fixtures' bytes unchanged
            sb.Append("- Version disagreements: ").Append(p.Uncertainty.Findings.Count).Append(" — see Uncertainty").Append('\n');
        if (p.Stop is not null)
        {
            sb.Append("- Waves: none (plan stopped: ").Append(p.Stop.Reason).Append(")").Append('\n');
            sb.Append("  - stop: ").Append(Text(p.Stop.Detail)).Append('\n');
            if (p.Stop.CyclePath is { Count: > 0 })
                sb.Append("  - cycle path: ").Append(string.Join(" -> ", p.Stop.CyclePath)).Append('\n');
        }
        else
        {
            var ready = p.Waves.Count(w => w.Status == "ready");
            var cond = p.Waves.Count(w => w.Status == "conditional");
            var prov = p.Waves.Count(w => w.Status == "provisional");
            sb.Append("- Waves: ").Append(p.Waves.Count)
              .Append(" (").Append(ready).Append(" ready, ").Append(cond).Append(" conditional, ").Append(prov).Append(" provisional)").Append('\n');
        }

        sb.Append('\n').Append("## Waves").Append('\n');
        if (p.Waves.Count == 0)
            sb.Append("None.").Append('\n');
        foreach (var w in p.Waves)
        {
            sb.Append('\n').Append("### Wave ").Append(w.Index).Append(" — ").Append(w.Status);
            if (w.Status == "provisional") sb.Append(" · blocked on ").Append(w.BlockedOn);
            sb.Append('\n');
            // wave-level gates BEFORE the unit list so GFM attaches them to the wave, not the last unit
            if (w.Prerequisites.Count == 0)
                sb.Append("- Prerequisites: none").Append('\n');
            else
            {
                sb.Append("- Prerequisites:").Append('\n');
                foreach (var pr in w.Prerequisites)
                    sb.Append("  - ").Append(Text(pr)).Append('\n');
            }
            if (w.Condition is not null)
                sb.Append("- Condition: ").Append(Text(w.Condition)).Append('\n');
            foreach (var u in w.ReleaseUnits)
            {
                sb.Append("- **").Append(Text(u.Repo)).Append("**");
                if (u.Packages.Count > 0) sb.Append(" (publishes ").Append(string.Join(", ", u.Packages.Select(Text))).Append(')');
                sb.Append(" · ").Append(u.Basis);
                sb.Append('\n');
                if (u.Notes is { Count: > 0 })
                    foreach (var n in u.Notes)
                        sb.Append("  - note: ").Append(Text(n)).Append('\n');
                if (u.PublicationStatus is { Count: > 0 })
                    foreach (var kv in u.PublicationStatus)
                        sb.Append("  - publication: ").Append(Text(kv.Key)).Append('=').Append(kv.Value).Append('\n');
            }
        }

        sb.Append('\n').Append("## Repositories").Append('\n');
        sb.Append("| Repo | Classification | Ownership | Action | Evidence | Confidence | Why |").Append('\n');
        sb.Append("|---|---|---|---|---|---|---|").Append('\n');
        foreach (var r in p.Repos)
            sb.Append("| ").Append(Cell(r.Repo)).Append(" | ").Append(r.Classification).Append(" | ").Append(r.Ownership)
              .Append(" | ").Append(r.ActionType)
              .Append(" | ").Append(Cell(string.Join(", ", r.EvidenceKinds)))
              .Append(" | ").Append(r.Confidence.Rung).Append(" ×").Append(r.Confidence.Corroboration)
              .Append(" | ").Append(Cell(string.Join("; ", r.Reasons))).Append(" |").Append('\n');

        sb.Append('\n').Append("## Uncertainty").Append('\n');
        var hasNotes = p.Repos.Any(r => r.ScanNotes is { Count: > 0 });
        if (p.Uncertainty.Contradictions.Count == 0 && p.Uncertainty.Gaps.Count == 0 && p.Uncertainty.Findings is not { Count: > 0 } && !hasNotes)
            sb.Append("None.").Append('\n');
        else
        {
            if (p.Uncertainty.Contradictions.Count > 0)
            {
                sb.Append('\n').Append("### Contradictions").Append('\n');
                foreach (var c in p.Uncertainty.Contradictions)
                {
                    sb.Append("- ").Append(c.Id).Append(" (").Append(Text(c.Subject)).Append("): claims by ")
                      .Append(string.Join(", ", c.Claims.Select(x => $"{Text(x.Repo)} ({Text(x.PackageId)})"))).Append("; downstream provisional: ")
                      .Append(c.DownstreamProvisional is { Count: > 0 } ? string.Join(", ", c.DownstreamProvisional.Select(Text)) : "none").Append('\n');
                }
            }
            if (p.Uncertainty.Gaps.Count > 0)
            {
                sb.Append('\n').Append("### Gaps").Append('\n');
                foreach (var g in p.Uncertainty.Gaps)
                    sb.Append("- **").Append(Text(g.Subject)).Append("**: ").Append(Text(g.Detail)).Append('\n');
            }
            if (p.Uncertainty.Findings is { Count: > 0 })
            {
                sb.Append('\n').Append("### Findings").Append('\n');
                foreach (var f in p.Uncertainty.Findings)
                    sb.Append("- **").Append(Text(f.Subject)).Append("**: ").Append(Text(f.Detail)).Append('\n');
            }
        }
            if (hasNotes)
            {
                sb.Append('\n').Append("### Scan notes").Append('\n');
                foreach (var r in p.Repos.Where(r => r.ScanNotes is { Count: > 0 }).OrderBy(r => r.Repo, StringComparer.Ordinal))
                    sb.Append("- **").Append(Text(r.Repo)).Append("**: ").Append(Text(string.Join("; ", r.ScanNotes!))).Append('\n');
            }
        return sb.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
    }
}

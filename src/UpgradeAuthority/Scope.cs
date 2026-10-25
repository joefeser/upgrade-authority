using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// SPEC-017 §3: estate-scope.v0 — the operator's include/exclude declaration.
// One shared validator for every surface (scan-estate, ingest, ownership, fixture load):
// the same malformed file produces the same typed message everywhere (SPEC-014's
// "one implementation" doctrine). Names are repo checkout folder names; matching is
// exact/ordinal. A scope that validates to nothing behaves exactly like no scope.
public sealed class EstateScope
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "";
    [JsonPropertyName("include")] public List<string>? Include { get; set; }
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }

    [JsonIgnore] public List<string>? EffectiveInclude { get; set; } // null = absent/empty (dropped)
    [JsonIgnore] public List<string>? EffectiveExclude { get; set; }
}

public static class Scope
{
    static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    // Parse + validate. Returns (scope, error): scope null + error null = validates to nothing
    // (no filtering, no plan field). Errors are caller-prefixed "error: " material.
    public static (EstateScope? scope, string? error) ParseAndValidate(string text, string displayName)
    {
        EstateScope? raw;
        try { raw = JsonSerializer.Deserialize<EstateScope>(text, Opts); }
        catch (JsonException ex) { return (null, $"scope file {displayName}: invalid JSON ({ex.Message})"); }
        if (raw is null) return (null, $"scope file {displayName}: unparseable");
        if (raw.SchemaVersion != "estate-scope.v0")
            return (null, $"scope file {displayName}: schemaVersion mismatch — expected 'estate-scope.v0', got '{raw.SchemaVersion}'");

        List<string>? Clean(List<string>? list, string label)
        {
            if (list is null || list.Count == 0) return null;
            var cleaned = new List<string>();
            foreach (var e in list)
            {
                if (!cleaned.Contains(e, StringComparer.Ordinal)) cleaned.Add(e);
                else Console.Error.WriteLine($"warning: scope file {displayName}: duplicate '{e}' in {label} — collapsed");
            }
            return cleaned;
        }
        // empty entries are typed errors (an empty name matches nothing and hides a config mistake)
        if (RawHasEmpty(raw.Include) || RawHasEmpty(raw.Exclude))
            return (null, $"scope file {displayName}: empty repo name in include/exclude — every entry must be a folder name");
        var include = Clean(raw.Include, "include");
        var exclude = Clean(raw.Exclude, "exclude");
        // present-but-empty include beside a non-empty exclude: "exclude everything from nothing" — the
        // config contradicts itself (SPEC-017 §3)
        if (raw.Include is { Count: 0 } && exclude is { Count: > 0 })
            return (null, $"scope file {displayName}: include:[] names nothing — delete the file or list repos");
        if (include is not null && exclude is not null)
        {
            var both = include.FirstOrDefault(i => exclude.Contains(i, StringComparer.Ordinal));
            if (both is not null)
                return (null, $"scope file {displayName}: '{both}' is in both include and exclude — the config contradicts itself; fix the file");
        }
        if (include is null && exclude is null) return (null, null); // no-op scope
        return (new EstateScope { SchemaVersion = "estate-scope.v0", EffectiveInclude = include, EffectiveExclude = exclude }, null);
    }

    static bool RawHasEmpty(List<string>? list) => list is not null && list.Any(string.IsNullOrEmpty);

    public static bool IsInScope(string name, EstateScope? scope)
    {
        if (scope is null) return true;
        if (scope.EffectiveInclude is { } inc && !inc.Contains(name, StringComparer.Ordinal)) return false;
        if (scope.EffectiveExclude is { } exc && exc.Contains(name, StringComparer.Ordinal)) return false;
        return true;
    }

    // CLI entry: loads --scope <path> for ingest/ownership/scan-estate. rc != 0 → message printed, return it.
    public static (EstateScope? scope, int rc) LoadForCli(string? scopePath)
    {
        if (scopePath is null) return (null, 0);
        if (!File.Exists(scopePath)) { Console.Error.WriteLine($"error: --scope file not found: {scopePath}"); return (null, 1); }
        var (canonical, listErr) = ReadCanonicalText(scopePath);
        if (listErr is not null) { Console.Error.WriteLine($"error: {listErr}"); return (null, 5); }
        var (scope, err) = ParseAndValidate(canonical, Path.GetFileName(scopePath));
        if (err is not null) { Console.Error.WriteLine($"error: {err}"); return (null, 5); }
        return (scope, 0);
    }

    // Two carriers, one meaning (the drift --feed/--folder-feed precedent). JSON starting with '{'
    // passes through untouched; anything else is the onboarding-friendly PLAIN LIST — one repo
    // folder name per line, '#' comments, blank lines ignored — meaning INCLUDE-mode (found on the
    // real 130-repo validation, 2026-10-20: the operator knows the subset up front and wants to
    // write it down BEFORE the first run; hand-authoring 130-entry JSON is not a reasonable ask).
    // A list is normalized to canonical estate-scope.v0 JSON here, so the run's snapshot, the
    // fixture copy, and every downstream consumer (plan echo, digests, known-inputs) see the same
    // schema'd bytes regardless of which form was written. Exclude-mode stays JSON-only.
    public static (string canonical, string? error) ReadCanonicalText(string scopePath)
    {
        var text = File.ReadAllText(scopePath);
        if (text.TrimStart().StartsWith('{')) return (text, null);
        var names = new List<string>();
        foreach (var line in File.ReadLines(scopePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            names.Add(trimmed);
        }
        if (names.Count == 0)
            return ("", $"scope file {Path.GetFileName(scopePath)}: the list names nothing — a list file must list repos (delete the file to scan everything)");
        return (JsonSerializer.Serialize(new { schemaVersion = "estate-scope.v0", include = names }, Opts), null);
    }
}

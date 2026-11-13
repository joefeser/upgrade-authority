using System.Text.Json;
using System.Text.Json.Serialization;

namespace UpgradeAuthority;

// pegs.v0 — the operator's version ceilings and pins (docs/UPGRADE-PLAN-DESIGN.md). Three
// shapes: exact ("pegged at this version"), maxExclusive (hard ceiling, Moq < 4.20),
// forceMax ("always take the highest allowed, at least X" — LiteBus). Master (org-shared)
// + estate override; the estate may TIGHTEN a master peg, never loosen one — a looser
// estate entry is a typed refusal naming both sides, never a silent winner. No
// cross-package peer ranges: a peg binds exactly one package.
public sealed class PegsFile
{
    [JsonPropertyName("schemaVersion")] public string SchemaVersion { get; set; } = "pegs.v0";
    [JsonPropertyName("pegs")] public List<PegEntry> Pegs { get; set; } = new();
}

public sealed class PegEntry
{
    [JsonPropertyName("packageName")] public string PackageName { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = ""; // exact | maxExclusive | forceMax
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    // allowPrerelease: the operator opts the package INTO prerelease eligibility — without it
    // the ceiling is stable-only (never guessed). Deliberately OR-merged across master/estate:
    // either operator turning it on is a deliberate act (the conservative default is off
    // everywhere); this is the one flag the tighten-only law does not constrain, stated here.
    [JsonPropertyName("allowPrerelease")] public bool? AllowPrerelease { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
}

public static class Pegs
{
    public const string KindExact = "exact";
    public const string KindMaxExclusive = "maxExclusive";
    public const string KindForceMax = "forceMax";

    // A resolved peg: the binding bound (if any) plus the asserted floor (if any). The bound
    // narrows what update may choose; the floor is intent the driver must honor or refuse.
    public sealed record Resolved(string PackageName, string? BoundKind, string? BoundVersion, string? FloorVersion, bool AllowPrerelease = false);

    public static PegsFile Load(string path)
    {
        var file = JsonSerializer.Deserialize<PegsFile>(File.ReadAllText(path), JsonOpts)
            ?? throw new InvalidDataException($"pegs file is empty: {path}");
        if (file.SchemaVersion != "pegs.v0")
            throw new InvalidDataException($"unsupported pegs schemaVersion '{file.SchemaVersion}' in {path} (expected pegs.v0)");
        foreach (var peg in file.Pegs)
        {
            if (peg.PackageName.Length == 0) throw new InvalidDataException($"pegs entry with empty packageName in {path}");
            // a bare allowPrerelease entry (no bound, no floor) is legal: the flag alone opts the
            // package into prerelease eligibility without constraining it
            if (peg.Kind.Length == 0 && peg.AllowPrerelease == true) continue;
            if (peg.Kind is not (KindExact or KindMaxExclusive or KindForceMax))
                throw new InvalidDataException($"peg {peg.PackageName}: unknown kind '{peg.Kind}' (exact | maxExclusive | forceMax)");
            if (peg.Version.Length == 0) throw new InvalidDataException($"peg {peg.PackageName}: empty version");
            if (Apply.ParseCorePublic(peg.Version) is null)
                throw new InvalidDataException($"peg {peg.PackageName}: version '{peg.Version}' is not a plain stable version (ranges/floating/prerelease cannot bind a peg)");
        }
        return file;
    }

    // Master + estate → one resolved constraint per package. Within-file duplicates refuse
    // (ambiguous authority); across files the estate may only tighten. An exact peg AT an
    // exclusive ceiling is not "looser" — it is unsatisfiable (the ceiling excludes the
    // pegged version itself): a typed contradiction, never a silent winner. A floor must sit
    // inside its bound (equal to an exact peg, strictly under a maxExclusive ceiling).
    public static (Dictionary<string, Resolved> ByPackage, List<string> Notes) Merge(PegsFile master, PegsFile estate)
    {
        var notes = new List<string>();
        var byPackage = new Dictionary<string, Resolved>(StringComparer.OrdinalIgnoreCase); // LOCAL: never shared state across calls
        var masterBound = CollectBound(master, "master");
        var estateBound = CollectBound(estate, "estate");
        var masterFloors = CollectFloors(master, "master");
        var estateFloors = CollectFloors(estate, "estate");
        var masterAllowPre = CollectAllowPrerelease(master);
        var estateAllowPre = CollectAllowPrerelease(estate);
        foreach (var packageName in masterBound.Keys.Concat(estateBound.Keys).Concat(masterFloors.Keys).Concat(estateFloors.Keys)
            .Concat(masterAllowPre).Concat(estateAllowPre)
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var hasMasterBound = masterBound.TryGetValue(packageName, out var mb);
            var hasEstateBound = estateBound.TryGetValue(packageName, out var eb);
            string? boundKind = null; string? boundVersion = null;
            if (hasMasterBound && hasEstateBound)
            {
                var verdict = Tighter(eb!.Kind, eb.Version, mb!.Kind, mb.Version);
                if (verdict == 2)
                    throw new InvalidDataException($"peg contradiction for {packageName}: estate {eb.Kind} {eb.Version} cannot coexist with the master {mb.Kind} {mb.Version} (the ceiling excludes the pegged version itself)");
                if (verdict < 0)
                {
                    (boundKind, boundVersion) = (eb.Kind, eb.Version);
                    notes.Add($"{packageName}: estate tightens the master peg (estate {eb.Kind} {eb.Version} under master {mb.Kind} {mb.Version})");
                }
                else if (verdict > 0)
                    throw new InvalidDataException($"estate peg LOOSENS the master for {packageName}: estate {eb.Kind} {eb.Version} vs master {mb.Kind} {mb.Version} — tighten the estate entry or remove it (the estate may never loosen a master peg)");
                else
                {
                    (boundKind, boundVersion) = (mb!.Kind, mb.Version);
                    notes.Add($"{packageName}: estate {eb!.Kind} {eb.Version} matches the master {mb.Kind} {mb.Version}");
                }
            }
            else if (hasMasterBound) (boundKind, boundVersion) = (mb!.Kind, mb.Version);
            else if (hasEstateBound) (boundKind, boundVersion) = (eb!.Kind, eb.Version);
            var floor = masterFloors.TryGetValue(packageName, out var mf) ? mf : estateFloors.TryGetValue(packageName, out var ef) ? ef : null;
            var allowPre = masterAllowPre.Contains(packageName) || estateAllowPre.Contains(packageName);
            if (!masterAllowPre.Contains(packageName) && estateAllowPre.Contains(packageName))
                notes.Add($"{packageName}: estate enables prerelease eligibility (the master did not)");
            if (masterFloors.ContainsKey(packageName) && estateFloors.ContainsKey(packageName) && masterFloors[packageName] != estateFloors[packageName])
                throw new InvalidDataException($"peg conflict for {packageName}: forceMax floor declared twice (master {masterFloors[packageName]}, estate {estateFloors[packageName]})");
            if (floor is not null && boundKind is not null)
            {
                var floorVsBound = Apply.CompareCore(floor, boundVersion!);
                var unsatisfiable = boundKind == KindExact ? floorVsBound != 0 : floorVsBound >= 0;
                if (floorVsBound is not null && unsatisfiable)
                    throw new InvalidDataException($"peg contradiction for {packageName}: forceMax floor {floor} is not satisfiable under {boundKind} {boundVersion}");
            }
            byPackage[packageName] = new Resolved(packageName, boundKind, boundVersion, floor, allowPre);
        }
        return (byPackage, notes);
    }

    // <0 = (kindA, versionA) is tighter than B; 0 = equivalent bounds; 1 = looser; 2 = contradiction.
    private static int Tighter(string kindA, string versionA, string kindB, string versionB)
    {
        var cmp = Apply.CompareCore(versionA, versionB) ?? 0;
        if (kindA == KindExact && kindB == KindMaxExclusive) return cmp < 0 ? -1 : 2; // exact inside the ceiling = tighter; at/above it = excluded by the ceiling
        if (kindA == KindMaxExclusive && kindB == KindExact) return cmp <= 0 ? -1 : 2; // a ceiling at or below the pegged version tightens (it excludes the pegged version itself)
        return cmp < 0 ? -1 : (cmp == 0 ? 0 : 1); // same kind: the lower version binds tighter
    }

    private static Dictionary<string, PegEntry> CollectBound(PegsFile file, string label)
    {
        var map = new Dictionary<string, PegEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var peg in file.Pegs)
        {
            if (peg.Kind == KindForceMax || peg.Kind.Length == 0) continue; // bare allowPrerelease entries carry no bound
            if (map.ContainsKey(peg.PackageName))
                throw new InvalidDataException($"peg conflict in {label}: {peg.PackageName} bound twice ({map[peg.PackageName].Kind} {map[peg.PackageName].Version} and {peg.Kind} {peg.Version})");
            map[peg.PackageName] = peg;
        }
        return map;
    }

    private static HashSet<string> CollectAllowPrerelease(PegsFile file) =>
        file.Pegs.Where(p => p.AllowPrerelease == true).Select(p => p.PackageName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> CollectFloors(PegsFile file, string label)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var peg in file.Pegs)
        {
            if (peg.Kind != KindForceMax) continue;
            if (map.ContainsKey(peg.PackageName))
                throw new InvalidDataException($"peg conflict in {label}: {peg.PackageName} forceMax declared twice ({map[peg.PackageName]} and {peg.Version})");
            map[peg.PackageName] = peg.Version;
        }
        return map;
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true, Encoder = StableJsonEncoder.Instance };
}

using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace UpgradeAuthority;

// SPEC-025: the campaign manifest — the digest-bound provenance record that rides beside exported
// packets. Binds every wave packet to its estate, campaign, exact input snapshot, plan bytes,
// generator, and profile/schema pins, so a coordinator (WITS intake) can distinguish an identical
// resubmission from a REPLAN (same packetId, changed digest) and key Cases on campaignId.
public static class Manifest
{
    public const string Domain = "io.ua.campaign-manifest.v1";
    public const string SchemaVersion = "campaign-manifest.v1";

    // exactly the loader's known input names (Program.LoadEngine reads this set — nothing else);
    // any OTHER file in input/ is a typed refusal: unrecognized bytes are never blessed into the
    // snapshot and stray files can never fabricate a revision signal (SPEC-025 §3, round-1 finding 2)
    static readonly string[] KnownInputs =
    {
        "producer-evidence.v0.json", "ownership.v0.json", "package-evidence.v0.json", "delta.json",
        "lockfile-rows.v0.json", "lockfile-rows.v1.json", "lockfile-rows.v2.json",
        "scope.v0.json", "build-freshness.v0.json",
    };

    public static (string? manifest, List<(string File, string Text)> waves, int rc) Run(string fixtureDir)
    {
        Engine engine;
        Plan plan;
        try { engine = Program.LoadEngine(fixtureDir); plan = engine.BuildPlan(); }
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return (null, new(), 3); }

        var inputDir = Path.Combine(fixtureDir, "input");
        var present = Directory.GetFiles(inputDir).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var unknown = present.Where(f => !KnownInputs.Contains(f)).ToList();
        if (unknown.Count > 0)
            throw new UaException($"cannot snapshot input: unrecognized file(s) in input/ ({string.Join(", ", unknown)}) — the manifest hashes exactly the loader's known inputs; stray bytes are never blessed into the snapshot");

        var inputSha = new List<(string, object)>();
        foreach (var f in present) inputSha.Add((f, Sha256(File.ReadAllText(Path.Combine(inputDir, f)))));

        var repos = plan.Repos.Select(r => r.Repo).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var campaignId = $"ua-{plan.Delta.PackageName}-{plan.Delta.OldVersion}-to-{plan.Delta.NewVersion}-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{plan.Delta.PackageName}\0{plan.Delta.OldVersion}\0{plan.Delta.NewVersion}\0{string.Join("\0", repos)}"))).ToLowerInvariant();

        var waves = new List<(string File, string Text)>();
        var packetRows = new List<(string, object)>();
        foreach (var w in plan.Waves)
        {
            var text = Packet.Write(Packet.BuildForExport(engine, plan, w));
            waves.Add(($"wave-{w.Index}.json", text));
            var doc = System.Text.Json.JsonDocument.Parse(text).RootElement;
            packetRows.Add(($"wave-{w.Index}", new List<(string, object)>
            {
                ("packetId", doc.GetProperty("packetId").GetString()!),
                ("digest", doc.GetProperty("digest").GetProperty("value").GetString()!),
            }));
        }

        var generator = typeof(Manifest).Assembly.GetName().Version?.ToString() ?? "unknown";
        var body = new List<(string, object)>
        {
            ("schemaVersion", SchemaVersion),
            ("digest", new List<(string, object)>()), // placeholder — WriteRecord computes the envelope digest
            ("campaignId", campaignId),
            ("generator", generator),
            ("campaign", new List<(string, object)>
            {
                ("package", plan.Delta.PackageName),
                ("oldVersion", plan.Delta.OldVersion),
                ("newVersion", plan.Delta.NewVersion),
            }),
            ("estate", new List<(string, object)>
            {
                ("repos", repos),
                ("inputSha256", inputSha),
                ("planSha256", Sha256(Canonical.Write(plan))),
            }),
            ("profile", new List<(string, object)>
            {
                ("ref", Packet.ProfileRef),
                ("hacpSchemaSha256", SchemaPins()),
            }),
            ("packets", packetRows),
        };
        var manifest = Packet.WriteRecord(body, Domain);
        return (manifest, waves, 0);
    }

    // the pinned schema copies, EMBEDDED as build-time resources (published binaries carry no docs/ —
    // SPEC-025 round-1 finding 4); the selftest asserts embedded == committed
    static List<(string, object)> SchemaPins()
    {
        var pins = new List<(string, object)>();
        foreach (var name in new[] { "task-packet.schema.json", "common-defs.schema.json" })
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"UpgradeAuthority.contracts.{name}");
            if (stream is null) throw new UaException($"internal error: embedded schema resource missing: {name}");
            using var reader = new StreamReader(stream);
            pins.Add((name, Sha256(reader.ReadToEnd())));
        }
        return pins;
    }

    static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}

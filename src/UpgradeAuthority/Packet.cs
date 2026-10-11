using System.Security.Cryptography;
using System.Text;

namespace UpgradeAuthority;

// SPEC-023: the seam slice — a ua plan wave exported as a HACP v0.3-candidate task packet.
// Emitter only: read-only over the fixture, offline, byte-deterministic. The record is CLOSED
// (no keys outside the pinned base schema, docs/contracts/hacp/); ua specifics live in VALUES,
// in the extension profile doc (docs/contracts/ua-hacp-profile-v0.1.md), and in profileRefs.
// Every value traces to plan.v1 (re-derived in-memory; the planner is deterministic) or to a
// profile-doc constant — the profile doc is the adjudication record.
public static class Packet
{
    public const string ProfileRef = "ua-hacp-profile-v0.1";
    public const string DigestDomain = "io.hacp.task-packet.v0.3-candidate";
    public const string HumanRequestConst = "operator-invoked export; the committed delta sidecar is the request record; ua V0 has no separate human-request artifact";
    public const string LoopBoundaryConst = "a ua wave is single-pass by construction: one branch, one commit; an existing branch refuses - never force-updated";
    public const string ReadyStopConst = "gates clear; executor preserves idempotency-by-refusal - an existing branch refuses";
    public const string BoundaryConst = "evidence is stated, not verified live; package spellings are the estate's own; unknowns are listed, never defaulted";

    static readonly string[] ForbiddenEffectsConst =
    {
        "never merges",
        "never force-updates",
        "never downgrades",
        "edits only verified sites",
    };

    // ---- entry: returns (canonical, rc). rc != 0 → canonical null, message already printed. ----
    public static (string? canonical, int rc) Run(string fixtureDir, int waveIndex)
    {
        Engine engine;
        try { engine = Program.LoadEngine(fixtureDir); }
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return (null, 3); }
        Plan plan;
        try { plan = engine.BuildPlan(); }
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return (null, 3); }
        if (string.IsNullOrEmpty(plan.Delta.PackageName)) { Console.Error.WriteLine("error: delta carries no package name"); return (null, 3); }

        var wave = plan.Waves.FirstOrDefault(w => w.Index == waveIndex);
        if (wave is null)
        { Console.Error.WriteLine($"error: no wave {waveIndex} in the plan (waves: {string.Join(", ", plan.Waves.Select(w => w.Index))})"); return (null, 1); }

        string canonical;
        try { canonical = Write(Build(engine, plan, wave)); }
        catch (UaException ex) { Console.Error.WriteLine(ex.Message); return (null, 3); }
        return (canonical, 0);
    }

    // The packet body as a deterministic ordered structure — consumed by Write (house display
    // form) and Digest (JCS form). Dictionary insertion order IS the field order.
    static List<(string Key, object Value)> Build(Engine engine, Plan plan, PlanWave wave)
    {
        var packetId = $"ua-{plan.Delta.PackageName}-{wave.Index}"; // stable, claimable — aligns with branch-as-claim
        var body = new List<(string, object)>
        {
            ("recordKind", "hacp.v0_3_candidate.task_packet"),
            ("schemaVersion", "hacp-0.3-candidate"),
            ("packetId", packetId),
            ("digest", new List<(string, object)>()), // placeholder — computed over the record-minus-digest
            ("sourceContext", new List<(string, object)>
            {
                ("humanRequest", HumanRequestConst),
                ("sourceRefs", SortedSet(wave.ReleaseUnits.Select(u => u.Repo)
                    .Concat(plan.Repos.Select(r => $"scan:{r.Repo}")).ToArray())),
            }),
            ("requestedScope", SortedSet(wave.ReleaseUnits.SelectMany(u => new[] { u.Repo }.Concat(u.Packages)).ToArray())),
            ("loopPolicy", new List<(string, object)>
            {
                ("mode", "single_pass"),
                ("ceiling", 1),
                ("counter", 0),
                ("incrementTrigger", packetId),
                ("claimAttemptLimit", 1),
                ("automaticRetryAllowed", false),
                ("boundaryStatement", LoopBoundaryConst),
            }),
            ("forbiddenEffects", SortedSet(ForbiddenEffectsConst)),
            ("expectedEvidence", ExpectedEvidence(engine, plan, wave)),
            ("stopConditions", StopConditions(plan, wave)),
            ("profileRefs", new List<string> { ProfileRef }),
            ("boundaryStatement", BoundaryConst),
        };
        return body;
    }

    static List<string> ExpectedEvidence(Engine engine, Plan plan, PlanWave wave)
    {
        var manifest = Apply.BuildManifest(engine, plan); // the same derivation apply uses — never a second truth
        var items = new List<string>();
        foreach (var unit in wave.ReleaseUnits)
        {
            var mu = manifest.Waves.SelectMany(w => w.Units).FirstOrDefault(u => u.Repo == unit.Repo);
            if (mu?.Edits is { Count: > 0 })
                foreach (var e in mu.Edits)
                    items.Add($"{unit.Repo}: {e.Path} {e.PackageId} {e.OldVersion}->{e.NewVersion} ({e.Kind})");
            if (mu?.Note is { } mNote) items.Add($"{unit.Repo}: {mNote}");
            foreach (var note in unit.Notes ?? new List<string>()) items.Add($"{unit.Repo}: {note}");
            if (unit.PublicationStatus is { Count: > 0 })
                foreach (var kv in unit.PublicationStatus) items.Add($"{unit.Repo}: publicationStatus {kv.Key}={kv.Value}");
            if (mu is { Action: "republish-required" }) items.Add($"{unit.Repo}: republish-required");
            if (mu is { Action: "external-request-await" }) items.Add($"{unit.Repo}: external-request-await");
        }
        return SortedSet(items.ToArray());
    }

    static List<string> StopConditions(Plan plan, PlanWave wave)
    {
        var items = new List<string>();
        if (wave.Condition is { } cond) items.Add(cond);
        if (wave.BlockedOn is { } blocked)
        {
            // never a bare id — the contradiction's subject + claims ride the string (SPEC-023 §2)
            var c = plan.Uncertainty.Contradictions.FirstOrDefault(x => x.Id == blocked);
            if (c is not null)
                items.Add($"{c.Id} unresolved: {c.Subject} — {string.Join("; ", c.Claims.Select(cl => $"{cl.Repo} claims {cl.PackageId}"))}");
            else
                items.Add($"{blocked} unresolved");
        }
        foreach (var p in wave.Prerequisites) items.Add(p);
        if (plan.Stop is { } stop) items.Add($"plan stopped ({stop.Reason}): {stop.Detail}");
        if (items.Count == 0) items.Add(ReadyStopConst); // stringSet minItems is 1 — the ready-wave fallback constant
        return SortedSet(items.ToArray());
    }

    static List<string> SortedSet(string[] input) =>
        input.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();

    // ---- digest: sha256 of the JCS serialization of {"domain":<domain>,"record":<record-minus-digest>} ----
    public static string Digest(List<(string Key, object Value)> body)
    {
        var envelope = new List<(string, object)>
        {
            ("domain", DigestDomain),
            ("record", BodyWithoutDigest(body)),
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Jcs(envelope)))).ToLowerInvariant(); // the contract's value pattern is lowercase hex
    }

    static List<(string, object)> BodyWithoutDigest(List<(string Key, object Value)> body)
    {
        var copy = new List<(string, object)>();
        foreach (var kv in body)
            if (kv.Key != "digest") copy.Add(kv);
        return copy;
    }

    // ---- RFC 8785 (JCS), scoped: strings, integers, booleans, arrays, objects — NO floats ----
    // (SPEC-023 §5: ua's records carry none; the scope boundary is tested at its edge.)
    public static string Jcs(object node)
    {
        var sb = new StringBuilder();
        JcsWrite(node, sb);
        return sb.ToString();
    }

    static void JcsWrite(object node, StringBuilder sb)
    {
        switch (node)
        {
            case string s:
                JcsString(s, sb);
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case int or long:
                sb.Append(node.ToString()); // integral types serialize as-is; no ES6 double formatting exists in scope
                break;
            case List<string> list:
                sb.Append('[');
                for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); JcsString(list[i], sb); }
                sb.Append(']');
                break;
            case List<(string Key, object Value)> obj:
                // JCS §3.2.3: object members sorted by key in UTF-16 code-unit order; values recursive
                sb.Append('{');
                bool first = true;
                foreach (var kv in obj.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    JcsString(kv.Key, sb);
                    sb.Append(':');
                    JcsWrite(kv.Value, sb);
                }
                sb.Append('}');
                break;
            default:
                throw new UaException($"internal error: JCS serializer met an out-of-scope node ({node?.GetType().Name}) — SPEC-023 §5 declares strings/ints/bools/arrays/objects only");
        }
    }

    // JCS §3.2.2.2 serialization of strings: escape " and \; the short escapes \b \t \n \f \r;
    // other control chars as \u00XX; everything else literal (no \u escaping of non-ASCII).
    static void JcsString(string s, StringBuilder sb)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
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
    }

    // ---- house display form (2-space, LF, fixed key order — the emitted bytes; only the DIGEST uses JCS) ----
    public static string Write(List<(string Key, object Value)> body)
    {
        // fail-loud emission-time validation (round-2 advisory): the contract's shape caps
        const int StringSetItemCap = 1024, TextCap = 4096;
        foreach (var kv in body)
        {
            switch (kv.Value)
            {
                case List<string> set:
                    if (set.Count == 0) throw new UaException($"export-packet: {kv.Key} must carry at least one entry (stringSet minItems is 1)");
                    foreach (var item in set)
                        if (item.Length > StringSetItemCap)
                            throw new UaException($"export-packet: {kv.Key} entry exceeds the schema's {StringSetItemCap}-char stringSet cap ({item.Length} chars) — shorten the source or bump the profile version");
                    break;
                case string s when s.Length > TextCap && (kv.Key == "boundaryStatement"):
                    throw new UaException($"export-packet: boundaryStatement exceeds the schema's {TextCap}-char text cap ({s.Length} chars)");
            }
        }

        var digestRow = body.First(kv => kv.Key == "digest");
        var digestValue = Digest(body);
        body[body.IndexOf(digestRow)] = ("digest", new List<(string, object)>
        {
            ("algorithm", "sha256"),
            ("canonicalization", "json-rfc8785-jcs"),
            ("digestDomain", DigestDomain),
            ("value", digestValue),
        });

        var sb = new StringBuilder();
        sb.Append("{\n");
        for (int i = 0; i < body.Count; i++)
        {
            sb.Append("  ").Append(Q(body[i].Key)).Append(": ");
            AppendValue(body[i].Value, sb, 1);
            sb.Append(i < body.Count - 1 ? "," : "").Append('\n');
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    static void AppendValue(object v, StringBuilder sb, int depth)
    {
        var pad = new string(' ', (depth + 1) * 2);
        var padEnd = new string(' ', depth * 2);
        switch (v)
        {
            case string s:
                sb.Append(Q(s));
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case int or long:
                sb.Append(v.ToString());
                break;
            case List<string> list:
                if (list.Count == 0) { sb.Append("[]"); break; }
                sb.Append("[\n");
                for (int i = 0; i < list.Count; i++)
                { sb.Append(pad).Append(Q(list[i])).Append(i < list.Count - 1 ? "," : "").Append('\n'); }
                sb.Append(new string(' ', depth * 2)).Append(']');
                break;
            case List<(string Key, object Value)> obj:
                sb.Append("{\n");
                for (int i = 0; i < obj.Count; i++)
                {
                    sb.Append(pad).Append(Q(obj[i].Key)).Append(": ");
                    AppendValue(obj[i].Value, sb, depth + 1);
                    sb.Append(i < obj.Count - 1 ? "," : "").Append('\n');
                }
                sb.Append(new string(' ', depth * 2)).Append('}');
                break;
            default:
                throw new UaException($"internal error: packet writer met an out-of-scope node ({v?.GetType().Name})");
        }
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

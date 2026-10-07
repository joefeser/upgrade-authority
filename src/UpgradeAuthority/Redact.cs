using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UpgradeAuthority;

// SPEC-016: --sanitized — shareable console output. Wraps Console streams so failure output can be
// pasted off a work machine without carrying paths, hosts, credentials, or operator-listed
// proprietary names. Canonical artifact emissions (plan/report stdout) bypass the redactor.
public static class Redact
{
    public static bool Enabled;

    static readonly string[] AllowlistedHosts =
    {
        "github.com", "api.github.com", "nuget.org", "api.nuget.org", "example.invalid", "localhost", "127.0.0.1",
    };

    // rule order is load-bearing (SPEC-016 §2): URLs → paths (win/unix) → emails → bare hosts → UA_REDACT
    static readonly Regex UrlRe = new(@"(?:https?://[^\s""'<>]+|ssh://[^\s""'<>]+|[A-Za-z0-9._~-]+@[A-Za-z0-9._-]+:[^\s""'<>]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase); // https/ssh/scp, case-insensitive (PR37 Q3, R2-C1)
    static readonly Regex WinPathRe = new(@"(?:[A-Za-z]:\\[^""'<>|\n]*?|\\\\[^""'<>|\n]+?)(?=:|\s\s|[""'<>|\n]|$)", RegexOptions.Compiled); // lazy: stops at prose colon, double space, delimiter, EOL (PR37 Q1 + R2-C2)
    static readonly Regex UnixAbsRe = new(@"(?:^|(?<=[\s""'(=]))/[^""'<>|\n]*?[A-Za-z0-9._~+-](?=:\s|\s\s|[""'<>|\n]|$)", RegexOptions.Compiled); // lazy: stops at ": " prose separator / double space / delimiter / EOL (PR37 Q1 + R2-C2); trailing prose without punctuation over-redacts — conservative by design
    static readonly Regex EmailRe = new(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    // context-anchored bare hosts: quoted 'host' / trailing host:port shapes our own messages emit
    // quoted-host anchor: FQDN, host:port, IPv4, or a single-label DNS name (SPEC-016 R1b — GHE bases
// like 'ghe' print bare); quoting ('host') is the context that says "this token IS a host"
    static readonly Regex HostAnchorQuotedRe = new(@"(?<=(?:origin |api |)host '|base host '|origin is '|base is ')(?<host>[A-Za-z0-9][A-Za-z0-9.-]*(?::\d+)?|[0-9]{1,3}(?:\.[0-9]{1,3}){3}(?::\d+)?|[A-Za-z][A-Za-z0-9-]+)(?=')", RegexOptions.Compiled); // anchored to the host-bearing contexts our messages actually use (PR37 Q5 + R2-C3); quoted tokens elsewhere pass through
    // PR37 Q2: unquoted repo keys (normalized remotes: host/path or host:port/path) — scheme already stripped
    static readonly Regex RepoKeyRe = new(@"\b(?<host>[A-Za-z0-9][A-Za-z0-9.-]*\.[A-Za-z0-9.-]+(?::\d+)?|[A-Za-z0-9.-]+:\d+)/[A-Za-z0-9._~/-]+", RegexOptions.Compiled); // host MUST carry a dot or a port — 'src/Api/...' (directory names) never match

    static List<string>? _extraTokens;

    public static void Enable(string? extraList)
    {
        Enabled = true;
        _extraTokens = (extraList ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 0).ToList();
    }

    public static string Apply(string line)
    {
        if (!Enabled) return line;

        // R5 first: operator-listed proprietary names (UA_REDACT) — applied to the RAW text so
        // downstream hashes cover the token and markers never nest (e.g. a path leaf containing a
        // proprietary name folds into the path hash instead of producing [path:[tok#…]…]).
        if (_extraTokens is { Count: > 0 })
            foreach (var tok in _extraTokens)
                if (!string.IsNullOrEmpty(tok) && line.Contains(tok, StringComparison.OrdinalIgnoreCase))
                    line = Regex.Replace(line, Regex.Escape(tok), _ => $"[tok#{Hash(tok.ToLowerInvariant())}]", RegexOptions.IgnoreCase);

        // R1: URLs and scp remotes (before paths — URLs contain path-shaped parts)
        line = UrlRe.Replace(line, m =>
        {
            var raw = m.Value;
            var host = ExtractHost(raw);
            var carriesSecrets = raw.Contains('@') && !raw.Contains("://") // scp form: user@host:path — user is an identifier
                || raw.Contains('?') || raw.Contains('#') && raw.IndexOf('#') < raw.Length // query/fragment
                || Regex.IsMatch(raw, @"https?://[^/@\s]+@"); // http userinfo
            return Allowlisted(host) && !carriesSecrets && !raw.Contains('?') && !Fragment(raw) ? raw : $"[url#{Hash(raw)}]";
        });

        // R2/R3: absolute paths (Windows drive/UNC, unix leading /). Repo-relative paths never match.
        line = WinPathRe.Replace(line, m => PathMark(m.Value));
        line = UnixAbsRe.Replace(line, m => PathMark(m.Value));

        // R4: emails (before bare hosts — an internal-domain email's host would partially match R1b)
        line = EmailRe.Replace(line, m => $"[email#{Hash(m.Value)}]");

        // R1b: bare hosts, context-anchored (quoted 'host' or host:port shapes)
        line = HostAnchorQuotedRe.Replace(line, m =>
        {
            var host = m.Groups["host"].Value;
            return Allowlisted(host) ? host : $"[host#{Hash(host)}]";
        });

        // R1c: unquoted repo keys (host/path — remotes normalized, scheme gone) as ownership checklists
        // and conflict errors print them (PR37 Q2)
        line = RepoKeyRe.Replace(line, m =>
        {
            var key = m.Value;
            var host = key.Split('/')[0];
            return Allowlisted(host) ? key : $"[repo#{Hash(key)}]";
        });

        return line;
    }

    static string ExtractHost(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..];
            var cut = rest.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) rest = rest[..cut];
            if (rest.Contains('@')) rest = rest[(rest.LastIndexOf('@') + 1)..]; // strip userinfo
            return rest.Split(':')[0].ToLowerInvariant();
        }
        // scp: user@host:path
        var at = url.IndexOf('@');
        if (at >= 0)
        {
            var tail = url[(at + 1)..];
            return tail.Split(':')[0].ToLowerInvariant();
        }
        return url.ToLowerInvariant();
    }

    static bool Fragment(string raw) => Regex.IsMatch(raw, @"https?://[^\s#]+#\S");

    static string PathMark(string value)
    {
        var leaf = Leaf(value);
        return leaf.Length == 0 ? $"[path#{Hash(value)}]" : $"[path:{leaf}#{Hash(value)}]";
    }

    static bool Allowlisted(string host) => AllowlistedHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    static string Leaf(string path)
    {
        var clean = path.TrimEnd('/', '\\');
        var idx = Math.Max(clean.LastIndexOf('/'), clean.LastIndexOf('\\'));
        var leaf = idx >= 0 ? clean[(idx + 1)..] : clean;
        // a leaf that itself carried a redaction marker (UA_REDACT proprietary name) is not
        // shown verbatim — the path hash already carries correlation (no nested markers, ever)
        return leaf.Contains('[') ? "" : leaf;
    }

    public static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    // Wraps Console.Out/Console.Error; canonicalArtifact lets plan/report stdout bypass (SPEC-016 §3).
    public sealed class RedactingWriter(TextWriter inner, Func<bool> canonicalBypass, TextWriter? originalErr = null) : TextWriter
    {
        public TextWriter Original { get; } = inner;
        public TextWriter OriginalErr { get; } = originalErr ?? inner;
        public override Encoding Encoding => inner.Encoding;
        public bool CanonicalArtifact { get; set; } // set true for the duration of a canonical artifact write
        public override void Write(char value) => Write(value.ToString());
        public override void Write(string? value)
        {
            if (value is null) return;
            if (CanonicalArtifact || canonicalBypass()) { inner.Write(value); return; }
            inner.Write(Apply(value));
        }
        public override void WriteLine(string? value) => Write(value is null ? Environment.NewLine : value + Environment.NewLine);
        public override void WriteLine() => Write(Environment.NewLine);
        public override void Flush() => inner.Flush();
    }
}

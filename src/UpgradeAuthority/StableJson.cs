using System.Text.Encodings.Web;

namespace UpgradeAuthority;

// Pinned JSON string escaping for every artifact ua WRITES (found on a real Windows work
// machine, 2026-10-19): System.Text.Json's JavaScriptEncoder.Default escape table is
// runtime-versioned — newer .NET runtimes stopped escaping the backtick (U+0060), which
// silently changed artifact bytes between SDK bands on otherwise identical inputs. Goldens,
// byte-compare selftests, and campaign inputSha256 (raw-byte hashes over input/) all depend
// on those bytes, so artifact output must not float with the host runtime. This encoder
// reproduces the table the committed artifacts were produced under, on every runtime:
// printable ASCII passes through except & ' + < > `; everything outside 0x20–0x7E escapes
// as uppercase \uXXXX (a surrogate pair escapes as its two units, exactly like Default).
// ", \ and control characters are escaped here too (short-escape style) because the base
// TextEncoder routes full runs through TryEncodeUnicodeScalar once anything triggers.
sealed class StableJsonEncoder : JavaScriptEncoder
{
    public static readonly StableJsonEncoder Instance = new();
    StableJsonEncoder() { }

    // one UTF-16 unit escapes to at most "\uXXXX" (6); a whole astral scalar to 12 — cover both
    public override int MaxOutputCharactersPerInputCharacter => 12;

    public override bool WillEncode(int scalarValue) => NeedsEscape(scalarValue);

    static bool NeedsEscape(int c) =>
        c < 0x20 || c == 0x22 || c == 0x26 || c == 0x27 || c == 0x2B || c == 0x3C
        || c == 0x3E || c == 0x5C || c == 0x60 || c == 0x7F || c > 0x7E;

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        for (var i = 0; i < textLength; i++)
            if (NeedsEscape(text[i])) return i;
        return -1;
    }

    const string Hex = "0123456789ABCDEF"; // STJ emits uppercase \uXXXX hex — byte-identical goldens demand the same

    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int charsWritten)
    {
        charsWritten = 0;
        // short escapes match Utf8JsonWriter's own style so encoder-invoked runs render
        // byte-identically to runs the writer escapes itself
        switch (unicodeScalar)
        {
            case 0x08: return Write2(buffer, bufferLength, out charsWritten, '\\', 'b');
            case 0x09: return Write2(buffer, bufferLength, out charsWritten, '\\', 't');
            case 0x0A: return Write2(buffer, bufferLength, out charsWritten, '\\', 'n');
            case 0x0C: return Write2(buffer, bufferLength, out charsWritten, '\\', 'f');
            case 0x0D: return Write2(buffer, bufferLength, out charsWritten, '\\', 'r');
            case 0x22: return Write2(buffer, bufferLength, out charsWritten, '\\', '"');
            case 0x5C: return Write2(buffer, bufferLength, out charsWritten, '\\', '\\');
        }
        if (unicodeScalar > 0xFFFF)
        {
            if (bufferLength < 12) return false;
            var v = unicodeScalar - 0x10000;
            WriteEscape(buffer, ref charsWritten, 0xD800 + (v >> 10));
            WriteEscape(buffer, ref charsWritten, 0xDC00 + (v & 0x3FF));
            return true;
        }
        if (bufferLength < 6) return false;
        WriteEscape(buffer, ref charsWritten, unicodeScalar);
        return true;
    }

    static unsafe bool Write2(char* buffer, int bufferLength, out int charsWritten, char a, char b)
    {
        if (bufferLength < 2) { charsWritten = 0; return false; }
        buffer[0] = a; buffer[1] = b;
        charsWritten = 2;
        return true;
    }

    static unsafe void WriteEscape(char* buffer, ref int n, int value)
    {
        buffer[n++] = '\\';
        buffer[n++] = 'u';
        buffer[n++] = Hex[(value >> 12) & 0xF];
        buffer[n++] = Hex[(value >> 8) & 0xF];
        buffer[n++] = Hex[(value >> 4) & 0xF];
        buffer[n++] = Hex[value & 0xF];
    }
}

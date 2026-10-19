namespace UpgradeAuthority;

// Bounded-retry moves for the atomic-swap pattern (found on a real corporate Windows machine,
// 2026-10-19): AV/EDR real-time scanners briefly hold handles on freshly written temp files, so
// the swap's Directory.Move/Delete surfaces UnauthorizedAccessException/IOException for a few
// hundred milliseconds (git retries its index lock for the same reason). The writes are already
// safely on disk — only the move/delete is retried; after 3 backoff retries the original
// exception propagates unchanged. Swap leftovers are already ignored at discovery
// (Ingest.IsSwapDir), so cleanup inside a finally/catch must WARN, never throw and mask the
// original failure.
static class FsSwap
{
    public static void MoveDir(string src, string dst) => Retry($"move {Path.GetFileName(src)} -> {Path.GetFileName(dst)}", () => Directory.Move(src, dst));

    public static void DeleteDir(string dir)
    {
        try { if (!Directory.Exists(dir)) return; }
        catch (Exception ex) { Console.Error.WriteLine($"warning: swap delete: could not probe {dir} ({ex.Message}) — remove manually"); return; }
        Retry($"delete {Path.GetFileName(dir)}", () =>
        {
            // read-only payload (a scanner writing attribute-locked files) would make Delete throw
            // forever — clear attributes best-effort before each attempt
            try { foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); } catch { /* best effort */ }
            try { Directory.Delete(dir, true); }
            catch (DirectoryNotFoundException) { /* already gone — idempotent success */ }
        });
    }

    public static void MoveFile(string src, string dst) => Retry($"move {Path.GetFileName(src)} -> {Path.GetFileName(dst)}", () => File.Move(src, dst, true));

    // best-effort rollback inside a catch: the ORIGINAL install error must be what the caller sees;
    // a failed rollback leaves the previous output visible at its .old- sibling (discovery-ignored)
    public static void RollbackMove(string backup, string dst)
    {
        try { MoveDir(backup, dst); }
        catch (Exception rex) { Console.Error.WriteLine($"warning: rollback failed — previous output left at {backup} ({rex.Message}); the original error follows"); }
    }

    // cleanup on failure paths: a leftover temp is discovery-ignored — the ORIGINAL error must survive
    public static void TryDeleteDirQuiet(string dir)
    {
        try { DeleteDir(dir); }
        catch (Exception ex) { Console.Error.WriteLine($"warning: could not remove swap temp {dir} ({ex.Message}) — ignored at discovery; remove manually"); }
    }

    public static void TryDeleteFileQuiet(string file)
    {
        try { if (File.Exists(file)) File.Delete(file); }
        catch (Exception ex) { Console.Error.WriteLine($"warning: could not remove swap temp {file} ({ex.Message}) — remove manually"); }
    }

    static void Retry(string what, Action op)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { op(); return; }
            catch (Exception ex) when (attempt < 3 && ex is UnauthorizedAccessException or IOException)
            {
                Console.Error.WriteLine($"warning: swap {what} blocked by {ex.GetType().Name} — retrying ({attempt + 1}/3; real-time scanners commonly hold fresh temp files briefly)");
                System.Threading.Thread.Sleep(250 * (attempt + 1));
            }
        }
    }
}

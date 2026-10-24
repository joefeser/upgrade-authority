namespace UpgradeAuthority;

// Bounded-retry moves for the atomic-swap pattern (found on a real corporate Windows machine,
// 2026-10-19): AV/EDR real-time scanners briefly hold handles on freshly written temp files, so
// the swap's Directory.Move/Delete surfaces UnauthorizedAccessException/IOException (git retries
// its index lock for the same reason). The writes are already safely on disk — only the
// move/delete is retried, with an exponential budget (500ms..4s — a corporate scanner holding
// multi-MB artifacts can take SECONDS, which exhausted the original 3×250ms budget live on
// 2026-10-20); after the budget the original exception propagates unchanged. Swap leftovers are
// already ignored at discovery (Ingest.IsSwapDir), so cleanup inside a finally/catch must WARN,
// never throw and mask the original failure.
static class FsSwap
{
    public static void MoveDir(string src, string dst) => Retry($"move {Path.GetFileName(src)} -> {Path.GetFileName(dst)}", () => Directory.Move(src, dst));

    public static void DeleteDir(string dir) => Retry($"delete {Path.GetFileName(dir)}", () =>
    {
        // probe-free: attempt the delete and treat not-found as success, so an access error on an
        // existence probe can never silently skip cleanup (attribute clearing is best-effort —
        // read-only payload would otherwise make Delete throw forever)
        try { foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal); }
        catch (DirectoryNotFoundException) { return; } // already gone — idempotent success
        catch { /* best effort */ }
        try { Directory.Delete(dir, true); }
        catch (DirectoryNotFoundException) { /* already gone — idempotent success */ }
    });

    public static void MoveFile(string src, string dst) => Retry($"move {Path.GetFileName(src)} -> {Path.GetFileName(dst)}", () => File.Move(src, dst, true));

    // best-effort rollback inside a catch: the ORIGINAL install error must be what the caller sees;
    // nothing-moved-aside is a no-op; a failed rollback leaves the previous output visible at its
    // .old- sibling (discovery-ignored)
    public static void RollbackMove(string backup, string dst)
    {
        try { MoveDir(backup, dst); }
        catch (DirectoryNotFoundException) { return; } // nothing was moved aside — nothing to restore
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
        try { File.Delete(file); } // probe-free: absent file is a no-op success, not a silent skip
        catch (Exception ex) when (ex is not (FileNotFoundException or DirectoryNotFoundException)) { Console.Error.WriteLine($"warning: could not remove swap temp {file} ({ex.Message}) — remove manually"); }
    }

    static void Retry(string what, Action op)
    {
        // corporate real-time scanners can hold freshly written multi-MB artifacts for SECONDS —
        // the original 3×250ms budget exhausted live (2026-10-20, real estate ingest: IOException ×2
        // then UnauthorizedAccessException, run failed at the swap). Exponential budget now, and
        // the warning carries the exception MESSAGE (the type alone cannot distinguish
        // being-used-by-another-process from anything else).
        var delays = new[] { 500, 1000, 2000, 4000 };
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try { op(); return; }
                catch (Exception ex) when (attempt < delays.Length && ex is UnauthorizedAccessException or IOException)
                {
                    Console.Error.WriteLine($"warning: swap {what} blocked by {ex.GetType().Name}: {FirstLine(ex.Message)} — retrying in {delays[attempt]}ms (attempt {attempt + 1}/{delays.Length}); real-time scanners commonly hold freshly written files for a few seconds");
                    System.Threading.Thread.Sleep(delays[attempt]);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: swap {what} failed after {delays.Length} retries ({ex.GetType().Name}: {FirstLine(ex.Message)}) — likely a real-time scanner still holding the fresh files, or an editor/Explorer holding the destination. The previous output stays in place (rolled back); close anything viewing it or wait a moment, then re-run");
            throw;
        }
    }

    static string FirstLine(string text) => (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
}

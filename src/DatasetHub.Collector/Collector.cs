using System.Globalization;
using System.Security.Cryptography;
namespace DatasetHub;

public sealed class Collector(CollectorOptions options)
{
    public async Task<CollectionResult> RunAsync(DateOnly from, DateOnly to, CancellationToken token = default)
    {
        if (from > to) throw new ArgumentException("Invalid date range.");
        Directory.CreateDirectory(options.ArchiveRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
        // One writer process per archive AND database. Locks are released by the OS on a crash.
        using var archiveLock = new FileStream(Path.Combine(options.ArchiveRoot, ".collector.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var databaseLock = new FileStream(options.DatabasePath + ".collector.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var history = new History(options.DatabasePath);
        var result = new CollectionResult();
        foreach (var machine in options.Equipments.Where(x => x.Enabled))
        foreach (var pc in machine.Pcs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                RejectLink(pc.SourceRoot);
                foreach (var day in Directory.EnumerateDirectories(pc.SourceRoot))
                {
                    if (!DateOnly.TryParseExact(Path.GetFileName(day), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                        || date < from || date > to) continue;
                    try
                    {
                        RejectLink(day);
                        foreach (var hour in Directory.EnumerateDirectories(day))
                        {
                            try
                            {
                                RejectLink(hour);
                                // Hour names are intentionally not constrained to 00..23.
                                foreach (var file in Directory.EnumerateFiles(hour).Where(x => Path.GetExtension(x).Equals(".log", StringComparison.OrdinalIgnoreCase)))
                                {
                                    token.ThrowIfCancellationRequested();
                                    var relative = Path.GetRelativePath(pc.SourceRoot, file);
                                    try
                                    {
                                        var hash = await CollectAsync(machine.MachineId, pc.PcId, relative, file, history, result, token);
                                        if (hash != null)
                                        {
                                            var archive = Path.Combine(options.ArchiveRoot, machine.MachineId, pc.PcId, relative);
                                            try
                                            {
                                                var counts = await new StandardLogParser(history).ParseAsync(machine.MachineId, pc.PcId, relative, archive, hash, token);
                                                result.Parsed += counts.valid;
                                                result.InvalidRows += counts.invalid;
                                            }
                                            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                                            {
                                                history.ParseFailure(machine.MachineId, pc.PcId, relative, hash, e.Message);
                                                result.Failed++;
                                                Console.Error.WriteLine($"Parse failed {machine.MachineId}/{pc.PcId}/{relative}: {e.Message}");
                                            }
                                        }
                                    }
                                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                                    {
                                        history.Record(machine.MachineId, pc.PcId, relative, "Failed", error: e.Message);
                                        result.Failed++;
                                        Console.Error.WriteLine($"{machine.MachineId}/{pc.PcId}/{relative}: {e.Message}");
                                    }
                                }
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                            { LogScanError(hour, e); }
                        }
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    { LogScanError(day, e); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { LogScanError(pc.SourceRoot, e); }
            void LogScanError(string path, Exception e)
            {
                history.ScanError(machine.MachineId, pc.PcId, path, e.Message);
                result.Failed++;
                Console.Error.WriteLine($"Scan failed {path}: {e.Message}");
            }
        }
        return result;
    }

    private async Task<string?> CollectAsync(string machine, string pc, string relative, string source, History history, CollectionResult result, CancellationToken token)
    {
        RejectLink(source);
        var marker = source + options.CompletionSuffix;
        if (!File.Exists(marker))
        {
            history.Record(machine, pc, relative, "Deferred", error: "Completion marker missing.");
            result.Deferred++;
            return null;
        }
        RejectLink(marker);
        var destination = Path.Combine(options.ArchiveRoot, machine, pc, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // Reject existing links in the destination path as well.
        for (var dir = Path.GetDirectoryName(destination); dir != null; dir = Path.GetDirectoryName(dir)) RejectLink(dir);
        if (File.Exists(destination)) RejectLink(destination);
        var temporary = destination + ".partial";
        if (File.Exists(temporary)) RejectLink(temporary);
        history.Record(machine, pc, relative, "Collecting");
        try
        {
            var before = new FileInfo(source);
            var size = before.Length;
            var modified = before.LastWriteTimeUtc;
            // Windows sharing denies writers/deleters while this handle is open.
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var sourceHash = Convert.ToHexString(await SHA256.HashDataAsync(input, token));
            if (File.Exists(destination))
            {
                var existingHash = await HashAsync(destination, token);
                VerifySource();
                if (existingHash != sourceHash)
                {
                    history.Record(machine, pc, relative, "Conflict", error: "Existing archive differs from source; neither file overwritten.");
                    result.Failed++;
                    return null;
                }
                history.Record(machine, pc, relative, "Collected", size, sourceHash);
                if (File.Exists(temporary)) File.Delete(temporary);
                result.Skipped++;
                return sourceHash;
            }
            input.Position = 0;
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, token);
                await output.FlushAsync(token);
                output.Flush(flushToDisk: true);
            }
            if (new FileInfo(temporary).Length != size || await HashAsync(temporary, token) != sourceHash)
                throw new IOException("Size or SHA-256 verification failed.");
            VerifySource();
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: false);
            // A crash before this record is repaired by hashing the final file on the next scan.
            history.Record(machine, pc, relative, "Collected", size, sourceHash);
            result.Collected++;
            return sourceHash;
            void VerifySource()
            {
                var after = new FileInfo(source);
                if (!File.Exists(marker) || after.Length != size || after.LastWriteTimeUtc != modified)
                    throw new IOException("Source changed during collection.");
            }
        }
        finally
        {
            // A hard process stop can leave this file; the next run safely recreates it.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException($"Links/reparse points are not supported: {path}");
    }
}

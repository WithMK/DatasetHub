using System.Globalization;
using System.Security.Cryptography;
namespace DatasetHub;

public sealed record CollectionProgress(string MachineId, string PcId, string Path, string Message);

public sealed class Collector(CollectorOptions options, Action<CollectionProgress>? progress = null)
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
                                    progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, relative, "확인 중"));
                                    try
                                    {
                                        var snapshot = await CollectAsync(machine.MachineId, pc.PcId, relative, file, history, result, token);
                                        if (snapshot != null)
                                        {
                                            var hash = snapshot.Sha256;
                                            var archive = snapshot.ArchivePath;
                                            try
                                            {
                                                var counts = await new StandardLogParser(history).ParseAsync(machine.MachineId, pc.PcId, snapshot.ParseKey, archive, hash, token);
                                                result.Parsed += counts.valid;
                                                result.InvalidRows += counts.invalid;
                                                progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, relative, counts.invalid > 0 ? $"수집 완료 · 파싱 오류 {counts.invalid}행" : "수집·파싱 완료"));
                                            }
                                            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                                            {
                                                history.ParseFailure(machine.MachineId, pc.PcId, snapshot.ParseKey, hash, e.Message);
                                                result.Failed++;
                                                progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, relative, "파싱 실패: " + e.Message));
                                                Console.Error.WriteLine($"Parse failed {machine.MachineId}/{pc.PcId}/{relative}: {e.Message}");
                                            }
                                        }
                                    }
                                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                                    {
                                        history.Record(machine.MachineId, pc.PcId, relative, "Failed", error: e.Message);
                                        result.Failed++;
                                        progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, relative, "수집 실패: " + e.Message));
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
                progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, path, "폴더 접근 실패: " + e.Message));
                Console.Error.WriteLine($"Scan failed {path}: {e.Message}");
            }
        }
        return result;
    }

    private async Task<Snapshot?> CollectAsync(string machine, string pc, string relative, string source, History history, CollectionResult result, CancellationToken token)
    {
        RejectLink(source);
        if (options.CompletionSuffix.Length > 0)
        {
            var marker = source + options.CompletionSuffix;
            if (!File.Exists(marker))
            {
                history.Record(machine, pc, relative, "Deferred", error: "Completion marker missing.");
                result.Deferred++;
                return null;
            }
            RejectLink(marker);
        }
        var versions = Path.Combine(options.ArchiveRoot, machine, pc, ".snapshots", relative);
        Directory.CreateDirectory(versions);
        for (var dir = versions; dir != null; dir = Path.GetDirectoryName(dir)) RejectLink(dir);
        var temporary = Path.Combine(versions, "copy.partial");
        if (File.Exists(temporary)) RejectLink(temporary);
        history.Record(machine, pc, relative, "Collecting");
        try
        {
            var before = new FileInfo(source);
            long size = before.Length;
            var modified = before.LastWriteTimeUtc;
            // Do not deny the equipment writer access. A writer may still deny our read; retry next scan.
            await using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, token);
                await output.FlushAsync(token);
                output.Flush(flushToDisk: true);
            }
            if (new FileInfo(temporary).Length != size) throw new IOException("Source size changed during copy; retry next scan.");
            var hash = await HashAsync(temporary, token);
            var after = new FileInfo(source);
            if (after.Length != size || after.LastWriteTimeUtc != modified ||
                (options.CompletionSuffix.Length > 0 && !File.Exists(source + options.CompletionSuffix)))
                throw new IOException("Source changed during copy; retry next scan.");
            // Recheck the source after the copy, even when size and timestamp stayed the same.
            if (await HashAsync(source, token) != hash) throw new IOException("Source changed during copy; retry next scan.");
            token.ThrowIfCancellationRequested();
            var archive = Path.Combine(versions, hash + ".log");
            if (File.Exists(archive))
            {
                RejectLink(archive);
                if (await HashAsync(archive, token) != hash) throw new IOException("Stored snapshot checksum mismatch.");
                result.Skipped++;
            }
            else
            {
                File.Move(temporary, archive, overwrite: false);
                result.Collected++;
            }
            history.Record(machine, pc, relative, "Collected", size, hash);
            return new Snapshot(archive, relative + "@" + hash, hash);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException($"Links/reparse points are not supported: {path}");
    }
}

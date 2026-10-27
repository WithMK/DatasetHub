using System.Globalization;
using System.Security.Cryptography;
namespace DatasetHub;

public sealed record CollectionProgress(string MachineId, string PcId, string Path, string Message);
public sealed record SourceScanStatus(string MachineId, string PcId, string Path, int DateFolders, int Files, int Updated, int Unchanged, int Failed, DateTime? LatestWriteUtc);

public sealed class Collector(CollectorOptions options, Action<CollectionProgress>? progress = null)
{
    // Version retention is implemented but not enabled during the copy-only phase.
    private static readonly bool RetainVersions = false;
    public Task<CollectionResult> RunAllAsync(CancellationToken token = default) => RunAsync(DateOnly.MinValue, DateOnly.MaxValue, token);
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
        var mirrorOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var machine in options.Equipments.Where(x => x.Enabled))
        foreach (var pc in machine.Pcs)
        {
            token.ThrowIfCancellationRequested();
            var updatedBefore = result.Collected;
            var unchangedBefore = result.Skipped;
            var failedBefore = result.Failed;
            var dayCount = 0;
            var sourceFiles = 0;
            DateTime? latestWriteUtc = null;
            result.SourcesChecked++;
            progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, pc.SourceRoot, $"원본 경로 확인 · 기간 {from:yyyyMMdd}~{to:yyyyMMdd}"));
            try
            {
                RejectLink(pc.SourceRoot);
                foreach (var day in Directory.EnumerateDirectories(pc.SourceRoot))
                {
                    if (!DateOnly.TryParseExact(Path.GetFileName(day), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                        || date < from || date > to) continue;
                    dayCount++;
                    result.DateFoldersFound++;
                    try
                    {
                        RejectLink(day);
                        var dayFiles = 0;
                        // Log files are directly inside the yyyyMMdd day folder.
                        foreach (var file in Directory.EnumerateFiles(day).Where(x => Path.GetExtension(x).Equals(".log", StringComparison.OrdinalIgnoreCase)))
                        {
                            token.ThrowIfCancellationRequested();
                            dayFiles++;
                            sourceFiles++;
                            result.FilesFound++;
                            var written = File.GetLastWriteTimeUtc(file);
                            if (latestWriteUtc == null || written > latestWriteUtc) latestWriteUtc = written;
                            var relative = Path.GetRelativePath(pc.SourceRoot, file);
                            progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, relative, "확인 중"));
                            try
                            {
                                var owner = machine.MachineId + "/" + pc.PcId;
                                if (mirrorOwners.TryGetValue(relative, out var existingOwner) && existingOwner != owner)
                                    throw new IOException($"저장 경로 중복: {relative} ({existingOwner}, {owner}). 원본별 파일명이 고유해야 합니다.");
                                mirrorOwners[relative] = owner;
                                await CollectAsync(machine.MachineId, pc.PcId, relative, file, history, result, token);
                            }
                            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
                            {
                                history.Record(machine.MachineId, pc.PcId, relative, "Failed", error: e.Message);
                                result.Failed++;
                                progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, relative, "수집 실패: " + e.Message));
                                Console.Error.WriteLine($"{machine.MachineId}/{pc.PcId}/{relative}: {e.Message}");
                            }
                        }
                        progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, day, $"날짜 폴더 확인 완료 · .log {dayFiles}개"));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    { LogScanError(day, e); }
                }
                progress?.Invoke(new CollectionProgress(machine.MachineId, pc.PcId, pc.SourceRoot,
                    $"경로 확인 완료 · 날짜 폴더 {dayCount}개 · .log {sourceFiles}개" +
                    (dayCount == 0 ? " (선택 기간과 yyyyMMdd 폴더명을 확인하세요)" : sourceFiles == 0 ? " (날짜 폴더 바로 아래의 .log를 확인하세요)" : "")));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { LogScanError(pc.SourceRoot, e); }
            result.Sources.Add(new SourceScanStatus(machine.MachineId, pc.PcId, pc.SourceRoot,
                dayCount, sourceFiles, result.Collected - updatedBefore, result.Skipped - unchangedBefore,
                result.Failed - failedBefore, latestWriteUtc));
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

    private async Task CollectAsync(string machine, string pc, string relative, string source, History history, CollectionResult result, CancellationToken token)
    {
        RejectLink(source);
        if (options.CompletionSuffix.Length > 0)
        {
            var marker = source + options.CompletionSuffix;
            if (!File.Exists(marker))
            {
                history.Record(machine, pc, relative, "Deferred", error: "Completion marker missing.");
                result.Deferred++;
                progress?.Invoke(new CollectionProgress(machine, pc, relative, "완료 표시 대기: " + marker));
                return;
            }
            RejectLink(marker);
        }
        var mirror = Path.Combine(options.ArchiveRoot, relative);
        var mirrorDirectory = Path.GetDirectoryName(mirror)!;
        Directory.CreateDirectory(mirrorDirectory);
        RejectLink(mirrorDirectory);
        if (File.Exists(mirror)) RejectLink(mirror);
        var temporary = mirror + ".partial";
        if (File.Exists(temporary)) RejectLink(temporary);
        var previous = history.LastCopy(machine, pc, relative);
        try
        {
            var before = new FileInfo(source);
            long size = before.Length;
            var modified = before.LastWriteTimeUtc;
            if (previous is { } saved && File.Exists(mirror) && new FileInfo(mirror).Length == saved.size)
            {
                if (size == saved.size && modified == saved.modified)
                {
                    result.Skipped++;
                    progress?.Invoke(new CollectionProgress(machine, pc, relative, "변경 없음 · " + mirror));
                    return;
                }
                if (size > saved.size && await TailMatchesAsync(source, mirror, saved.size, token))
                {
                    history.Record(machine, pc, relative, "Collecting");
                    await AppendAsync(source, mirror, saved.size, size, modified, token);
                    history.Record(machine, pc, relative, "Collected", size, sourceModifiedUtc: modified);
                    result.Collected++;
                    result.AppendedBytes += size - saved.size;
                    progress?.Invoke(new CollectionProgress(machine, pc, relative, $"추가분 {size - saved.size:N0}바이트 복사 · " + mirror));
                    return;
                }
            }
            history.Record(machine, pc, relative, "Collecting");
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
            var unchanged = File.Exists(mirror) && await HashAsync(mirror, token) == hash;
            if (!unchanged)
            {
                if (RetainVersions && File.Exists(mirror))
                    await PreservePreviousVersionAsync(machine, pc, relative, mirror, token);
                // Same-directory move replaces the previous latest copy only after verification.
                File.Move(temporary, mirror, overwrite: true);
            }
            history.Record(machine, pc, relative, "Collected", size, hash, sourceModifiedUtc: modified);
            if (unchanged) result.Skipped++; else result.Collected++;
            progress?.Invoke(new CollectionProgress(machine, pc, relative,
                (unchanged ? "기존 복사본 확인 · " : "새 복사본 저장 · ") + mirror));
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    private static async Task<bool> TailMatchesAsync(string source, string mirror, long offset, CancellationToken token)
    {
        var count = (int)Math.Min(offset, 65536);
        if (count == 0) return true;
        var sourceTail = new byte[count];
        var mirrorTail = new byte[count];
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using var output = new FileStream(mirror, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        input.Position = offset - count;
        output.Position = offset - count;
        await input.ReadExactlyAsync(sourceTail, token);
        await output.ReadExactlyAsync(mirrorTail, token);
        return sourceTail.AsSpan().SequenceEqual(mirrorTail);
    }
    private static async Task AppendAsync(string source, string mirror, long offset, long targetSize, DateTime modified, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(mirror, FileMode.Open, FileAccess.Write,
            FileShare.Read, 131072, FileOptions.Asynchronous);
        if (output.Length != offset) throw new IOException("Destination size changed during append.");
        input.Position = offset;
        output.Position = offset;
        try
        {
            var buffer = new byte[131072];
            long remaining = targetSize - offset;
            while (remaining > 0)
            {
                var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                if (read == 0) throw new IOException("Source shrank during append.");
                await output.WriteAsync(buffer.AsMemory(0, read), token);
                remaining -= read;
            }
            await output.FlushAsync(token);
            output.Flush(flushToDisk: true);
            var after = new FileInfo(source);
            if (after.Length != targetSize || after.LastWriteTimeUtc != modified)
                throw new IOException("Source changed during append; retry next scan.");
        }
        catch
        {
            output.SetLength(offset);
            output.Flush(flushToDisk: true);
            throw;
        }
    }
    private async Task PreservePreviousVersionAsync(string machine, string pc, string relative, string previous, CancellationToken token)
    {
        var versions = Path.Combine(options.ArchiveRoot, machine, pc, ".snapshots", relative);
        Directory.CreateDirectory(versions);
        for (var dir = versions; dir != null; dir = Path.GetDirectoryName(dir)) RejectLink(dir);
        var priorHash = await HashAsync(previous, token);
        var archived = Path.Combine(versions, priorHash + ".log");
        if (File.Exists(archived))
        {
            RejectLink(archived);
            if (await HashAsync(archived, token) != priorHash) throw new IOException("Stored version checksum mismatch.");
        }
        else File.Copy(previous, archived);
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

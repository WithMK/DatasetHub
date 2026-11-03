using System.Security.Cryptography;

namespace DatasetHub;

// Image names are opaque. Only the extension selects candidates; the source tree is preserved.
internal sealed class ImageCollector(CollectorOptions options, History history, Action<CollectionProgress>? progress)
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".tif", ".tiff", ".jpg", ".jpeg", ".bmp" };

    public async Task ScanAsync(string machine, SourcePc pc, DateOnly from, DateOnly to, CollectionResult result, CancellationToken token)
    {
        var root = pc.ImageSourceRoot;
        progress?.Invoke(new CollectionProgress(machine, pc.PcId, root, "이미지 원본 경로 확인"));
        var folders = new Stack<string>();
        folders.Push(root);
        while (folders.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var folder = folders.Pop();
            try
            {
                RejectLink(folder);
                foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        RejectLink(entry);
                        if (Directory.Exists(entry)) { folders.Push(entry); continue; }
                        if (!Extensions.Contains(Path.GetExtension(entry))) continue;
                        var written = DateOnly.FromDateTime(File.GetLastWriteTime(entry));
                        if (written < from || written > to) continue;
                        result.ImagesFound++;
                        await CopyAsync(machine, pc, entry, result, token);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    { Failure(entry, e); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { Failure(folder, e); }
        }
        progress?.Invoke(new CollectionProgress(machine, pc.PcId, root, "이미지 경로 확인 완료"));

        void Failure(string path, Exception e)
        {
            result.Failed++;
            result.ImageFailed++;
            history.ScanError(machine, pc.PcId, path, e.Message);
            progress?.Invoke(new CollectionProgress(machine, pc.PcId, path, "이미지 수집 실패: " + e.Message));
        }
    }

    private async Task CopyAsync(string machine, SourcePc pc, string source, CollectionResult result, CancellationToken token)
    {
        var relative = Path.GetRelativePath(pc.ImageSourceRoot, source);
        var key = "Image/" + relative.Replace('\\', '/');
        var target = Path.Combine(options.ArchiveRoot, machine, "Image", pc.PcId, relative);
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        // Reject links in every destination component, not only the final file.
        for (var part = directory; part != null; part = Path.GetDirectoryName(part)) RejectLink(part);
        if (File.Exists(target)) RejectLink(target);
        var info = new FileInfo(source);
        var previous = history.LastCopy(machine, pc.PcId, key);
        if (previous is { } saved && saved.size == info.Length && saved.modified == info.LastWriteTimeUtc &&
            File.Exists(target) && new FileInfo(target).Length == info.Length)
        {
            result.ImagesSkipped++;
            return;
        }
        var temporary = target + ".partial";
        if (File.Exists(temporary)) RejectLink(temporary);
        history.Record(machine, pc.PcId, key, "Collecting");
        try
        {
            var size = info.Length;
            var modified = info.LastWriteTimeUtc;
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write,
                FileShare.None, 131072, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, token);
                await output.FlushAsync(token);
                output.Flush(flushToDisk: true);
            }
            var copiedHash = await HashAsync(temporary, token);
            var after = new FileInfo(source);
            if (new FileInfo(temporary).Length != size || after.Length != size || after.LastWriteTimeUtc != modified ||
                await HashAsync(source, token) != copiedHash)
                throw new IOException("Image changed during copy; retry next scan.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, overwrite: true);
            history.Record(machine, pc.PcId, key, "Collected", size, copiedHash, sourceModifiedUtc: modified);
            result.ImagesCopied++;
            progress?.Invoke(new CollectionProgress(machine, pc.PcId, relative, "이미지 복사 완료 · " + target));
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }
    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Links/reparse points are not supported: " + path);
    }
}

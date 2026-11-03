using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using DatasetHub.Core;
namespace DatasetHub.Infrastructure;
public sealed class ConfirmationService : IConfirmationService
{
    public bool Confirm(string message) => MessageBox.Show(Application.Current.MainWindow, message, "DatasetHub", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    public UnsavedDecision UnsavedChanges() => MessageBox.Show(Application.Current.MainWindow,
        "저장하지 않은 변경사항이 있습니다. 저장하시겠습니까?\n예: 저장 / 아니요: 변경 취소 / 취소: 현재 화면 유지",
        "설정 변경", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        { MessageBoxResult.Yes => UnsavedDecision.Save, MessageBoxResult.No => UnsavedDecision.Discard, _ => UnsavedDecision.Cancel };
}
public sealed class CloseGuard : ICloseGuard
{
    private readonly List<Func<Task<bool>>> guards = [];
    public void Register(Func<Task<bool>> guard) => guards.Add(guard);
    public async Task<bool> CanCloseAsync()
    {
        foreach (var guard in guards.ToArray()) if (!await guard()) return false;
        return true;
    }
}
public sealed class PathReader : IPathReader
{
    public PathAccessResult Read(string fullPath)
    {
        // MoveNext forces directory enumeration and reports missing paths / denied ACLs.
        using var entries = Directory.EnumerateFileSystemEntries(fullPath).GetEnumerator();
        if (!entries.MoveNext()) return new(fullPath, true, "연결 성공 · 빈 폴더 목록 읽기 확인 (파일 읽기는 미검증)");
        var sample = entries.Current;
        if (Directory.Exists(sample)) return new(fullPath, true, "연결 성공 · 폴더 목록 읽기 확인 (하위 파일 권한은 미검증)");
        using var stream = new FileStream(sample, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        stream.ReadByte();
        return new(fullPath, true, "연결 성공 · 폴더 목록 및 표본 파일 읽기 확인");
    }
}
public sealed class PathProbeService(IPathReader reader) : IPathProbeService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<PathAccessResult>>> pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim slots = new(4);
    public async Task<PathAccessResult> CheckAsync(string path, string configurationPath)
    {
        string full;
        try { full = Path.GetFullPath(path, Path.GetDirectoryName(Path.GetFullPath(configurationPath))!); }
        catch (Exception e) { return new(path, false, e.Message); }
        var entry = pending.GetOrAdd(full, key => new(() => ReadAsync(key)));
        var work = entry.Value;
        try { return await work.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { return new(full, false, "확인 시간 초과 (10초). 네트워크 응답을 확인하세요."); }
        finally
        {
            // A timed out Windows SMB call cannot be forcibly cancelled. Keep at most one per path.
            _ = work.ContinueWith(_ => pending.TryRemove(new KeyValuePair<string, Lazy<Task<PathAccessResult>>>(full, entry)), TaskScheduler.Default);
        }
    }
    private async Task<PathAccessResult> ReadAsync(string path)
    {
        await slots.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                try { return reader.Read(path); }
                catch (Exception e) { return new PathAccessResult(path, false, "연결 실패 · " + e.Message); }
            });
        }
        finally { slots.Release(); }
    }
}

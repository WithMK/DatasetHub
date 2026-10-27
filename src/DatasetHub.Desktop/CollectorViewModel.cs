using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using DatasetHub;

namespace DatasetHub.Desktop;

public sealed class CollectorViewModel : INotifyPropertyChanged
{
    private string configPath = ConfigFileLocator.ExamplePath;
    private DateTime? fromDate = DateTime.Today;
    private DateTime? toDate = DateTime.Today;
    private string status = "설정 파일을 확인하고 수집 시작을 누르세요.";
    private string summary = "";
    private bool useDateRange;
    private bool configValid;
    private bool autoRepeat;
    private int intervalMinutes = 5;
    private CancellationTokenSource? running;
    private readonly RelayCommand start;
    private readonly RelayCommand stop;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<string> Messages { get; } = [];
    public ObservableCollection<string> SourceStatuses { get; } = [];
    public string ConfigPath { get => configPath; set { configPath = value; Changed(); CheckConfig(); } }
    public DateTime? FromDate { get => fromDate; set { fromDate = value; Changed(); } }
    public DateTime? ToDate { get => toDate; set { toDate = value; Changed(); } }
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Summary { get => summary; private set { summary = value; Changed(); } }
    public bool AutoRepeat { get => autoRepeat; set { autoRepeat = value; Changed(); } }
    public bool UseDateRange { get => useDateRange; set { useDateRange = value; Changed(); } }
    public IReadOnlyList<int> IntervalChoices { get; } = [1, 5, 10, 30, 60];
    public int IntervalMinutes { get => intervalMinutes; set { intervalMinutes = value; Changed(); } }
    public ICommand StartCommand => start;
    public ICommand StopCommand => stop;

    public CollectorViewModel()
    {
        start = new RelayCommand(() => _ = RunAsync(), () => running == null && configValid);
        stop = new RelayCommand(RequestStop, () => running != null);
        CheckConfig();
    }
    private static CollectorOptions ReadOptions(string path)
    {
        var full = Path.GetFullPath(path);
        var options = JsonSerializer.Deserialize<CollectorOptions>(File.ReadAllText(full), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
        }) ?? throw new InvalidDataException("빈 설정 파일입니다.");
        options.Validate(Path.GetDirectoryName(full)!);
        return options;
    }
    private void CheckConfig()
    {
        try
        {
            var options = ReadOptions(configPath);
            configValid = true;
            Status = $"설정 확인 완료 · 설비 {options.Equipments.Count}대 · 저장 {options.ArchiveRoot}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or JsonException or NotSupportedException)
        {
            configValid = false;
            Status = "설정 오류: " + e.Message;
        }
        start.Refresh();
    }
    public void ShowBrowseError(Exception error)
    {
        Status = "파일 선택 창 오류: " + error.Message;
        Messages.Add(error.ToString());
    }
    public void RequestStop()
    {
        if (running == null) return;
        Status = "중지 요청 중… 현재 파일 작업이 끝나면 멈춥니다.";
        running.Cancel();
    }
    private async Task RunAsync()
    {
        if (running != null) return;
        if ((useDateRange && (fromDate == null || toDate == null || fromDate > toDate)) || string.IsNullOrWhiteSpace(configPath) ||
            (autoRepeat && intervalMinutes is < 1 or > 1440))
        { Status = "설정·기간 및 자동 반복 주기(1~1440분)를 확인하세요."; return; }
        var cts = new CancellationTokenSource();
        running = cts; start.Refresh(); stop.Refresh();
        Messages.Clear(); SourceStatuses.Clear(); Summary = ""; Status = "수집 중…";
        try
        {
            var options = ReadOptions(configPath);
            Messages.Add("복사본 저장 경로: " + options.ArchiveRoot);
            var ui = Application.Current.Dispatcher;
            var firstScan = true;
            do
            {
                // Without a manual date range, scan all dates initially; repeated scans cover recent days.
                var allDates = !useDateRange && (firstScan || !autoRepeat);
                var first = useDateRange ? DateOnly.FromDateTime(fromDate!.Value) : DateOnly.FromDateTime(DateTime.Today.AddDays(-2));
                var last = useDateRange ? DateOnly.FromDateTime(toDate!.Value) : DateOnly.FromDateTime(DateTime.Today);
                Status = allDates ? "전체 날짜 폴더 확인 중…" : $"{first:yyyy-MM-dd} ~ {last:yyyy-MM-dd} 확인 중…";
                var result = await Task.Run(() => new Collector(options, item =>
                {
                    ui.BeginInvoke(() =>
                    {
                        Messages.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {item.MachineId}/{item.PcId} {item.Path} · {item.Message}");
                        if (Messages.Count > 1000) Messages.RemoveAt(Messages.Count - 1);
                    });
                }).RunAsync(allDates ? DateOnly.MinValue : first, allDates ? DateOnly.MaxValue : last, cts.Token));
                SourceStatuses.Clear();
                foreach (var source in result.Sources)
                    SourceStatuses.Add($"{source.MachineId}/{source.PcId} · {source.Path} · 날짜 {source.DateFolders}개 · .log {source.Files}개 · 갱신 {source.Updated} · 변경 없음 {source.Unchanged} · 실패 {source.Failed} · 마지막 변경 {(source.LatestWriteUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "없음")}");
                Summary = $"원본 경로 {result.SourcesChecked}개 · 날짜 폴더 {result.DateFoldersFound}개 · 발견한 .log {result.FilesFound}개 · 신규/갱신 {result.Collected} · 추가분 {result.AppendedBytes:N0}바이트 · 변경 없음 {result.Skipped} · 완료 대기 {result.Deferred} · 실패 {result.Failed}";
                Status = result.Failed + result.Deferred > 0 ? "확인이 필요한 항목이 있습니다." :
                    result.FilesFound == 0 ? "확인 범위에 .log 파일이 없습니다. 경로와 날짜 폴더를 확인하세요." : "이번 확인 완료.";
                firstScan = false;
                if (!autoRepeat) break;
                Status += $" 다음 확인까지 {intervalMinutes}분 대기 중…";
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), cts.Token);
            } while (!cts.IsCancellationRequested);
        }
        catch (OperationCanceledException) { Status = "중단했습니다. 같은 기간으로 다시 실행하면 이어서 확인합니다."; }
        catch (Exception e) { Status = "실행 실패: " + e.Message; Messages.Add(e.ToString()); }
        finally { running = null; cts.Dispose(); start.Refresh(); stop.Refresh(); }
    }
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
internal sealed class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public void Execute(object? parameter) => execute();
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

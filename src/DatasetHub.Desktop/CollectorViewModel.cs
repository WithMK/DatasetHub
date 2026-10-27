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
    private string status = "설정 파일과 기간을 확인하세요.";
    private string summary = "";
    private bool autoRepeat;
    private int intervalMinutes = 5;
    private CancellationTokenSource? running;
    private readonly RelayCommand start;
    private readonly RelayCommand stop;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<string> Messages { get; } = [];
    public string ConfigPath { get => configPath; set { configPath = value; Changed(); } }
    public DateTime? FromDate { get => fromDate; set { fromDate = value; Changed(); } }
    public DateTime? ToDate { get => toDate; set { toDate = value; Changed(); } }
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Summary { get => summary; private set { summary = value; Changed(); } }
    public bool AutoRepeat { get => autoRepeat; set { autoRepeat = value; Changed(); } }
    public IReadOnlyList<int> IntervalChoices { get; } = [1, 5, 10, 30, 60];
    public int IntervalMinutes { get => intervalMinutes; set { intervalMinutes = value; Changed(); } }
    public ICommand StartCommand => start;
    public ICommand StopCommand => stop;

    public CollectorViewModel()
    {
        start = new RelayCommand(() => _ = RunAsync(), () => running == null);
        stop = new RelayCommand(RequestStop, () => running != null);
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
        if (fromDate == null || toDate == null || fromDate > toDate || string.IsNullOrWhiteSpace(configPath) ||
            (autoRepeat && intervalMinutes is < 1 or > 1440))
        { Status = "설정·기간 및 자동 반복 주기(1~1440분)를 확인하세요."; return; }
        var cts = new CancellationTokenSource();
        running = cts; start.Refresh(); stop.Refresh();
        Messages.Clear(); Summary = ""; Status = "수집 중…";
        try
        {
            var path = Path.GetFullPath(configPath);
            var options = JsonSerializer.Deserialize<CollectorOptions>(await File.ReadAllTextAsync(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
            }) ?? throw new InvalidDataException("빈 설정 파일입니다.");
            options.Validate(Path.GetDirectoryName(path)!);
            Messages.Add("복사본 저장 경로: " + options.ArchiveRoot);
            var ui = Application.Current.Dispatcher;
            do
            {
                // Automatic mode rolls with the date, covering today and the previous two days.
                var first = autoRepeat ? DateOnly.FromDateTime(DateTime.Today.AddDays(-2)) : DateOnly.FromDateTime(fromDate.Value);
                var last = autoRepeat ? DateOnly.FromDateTime(DateTime.Today) : DateOnly.FromDateTime(toDate.Value);
                Status = $"{first:yyyy-MM-dd} ~ {last:yyyy-MM-dd} 확인 중…";
                var result = await Task.Run(() => new Collector(options, item =>
                {
                    ui.BeginInvoke(() =>
                    {
                        Messages.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {item.MachineId}/{item.PcId} {item.Path} · {item.Message}");
                        if (Messages.Count > 1000) Messages.RemoveAt(Messages.Count - 1);
                    });
                }).RunAsync(first, last, cts.Token));
                Summary = $"원본 경로 {result.SourcesChecked}개 · 날짜 폴더 {result.DateFoldersFound}개 · 발견한 .log {result.FilesFound}개 · 새 복사본 {result.Collected} · 기존 확인 {result.Skipped} · 완료 대기 {result.Deferred} · 실패 {result.Failed}";
                Status = result.Failed + result.Deferred > 0 ? "확인이 필요한 항목이 있습니다." :
                    result.FilesFound == 0 ? "선택 기간에 .log 파일이 없습니다. 경로와 날짜 폴더를 확인하세요." : "이번 확인 완료.";
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

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
    private string configPath = "";
    private DateTime? fromDate = DateTime.Today.AddDays(-1);
    private DateTime? toDate = DateTime.Today.AddDays(-1);
    private string status = "설정 파일과 기간을 선택하세요.";
    private string summary = "";
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
    public ICommand StartCommand => start;
    public ICommand StopCommand => stop;

    public CollectorViewModel()
    {
        start = new RelayCommand(() => _ = RunAsync(), () => running == null);
        stop = new RelayCommand(RequestStop, () => running != null);
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
        if (fromDate == null || toDate == null || fromDate > toDate || string.IsNullOrWhiteSpace(configPath))
        { Status = "설정 파일과 올바른 시작일·종료일을 입력하세요."; return; }
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
            var first = DateOnly.FromDateTime(fromDate.Value);
            var last = DateOnly.FromDateTime(toDate.Value);
            var ui = Application.Current.Dispatcher;
            var result = await Task.Run(() => new Collector(options, item =>
            {
                ui.BeginInvoke(() =>
                {
                    Messages.Add($"[{DateTime.Now:HH:mm:ss}] {item.MachineId}/{item.PcId} {item.Path} · {item.Message}");
                    if (Messages.Count > 1000) Messages.RemoveAt(0);
                });
            }).RunAsync(first, last, cts.Token));
            Summary = $"신규 {result.Collected} · 기존 확인 {result.Skipped} · 완료 대기 {result.Deferred} · 실패 {result.Failed} · 파싱 {result.Parsed}행 · 형식 오류 {result.InvalidRows}행";
            Status = result.Failed + result.Deferred + result.InvalidRows == 0 ? "수집이 완료되었습니다." : "확인이 필요한 항목이 있습니다. 같은 기간을 다시 실행해 재점검할 수 있습니다.";
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

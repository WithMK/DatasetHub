using System.IO;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using DatasetHub;
using DatasetHub.Core;
using DatasetHub.Modules.Collector;
using Prism.Events;

namespace DatasetHub.Desktop;

public sealed class CollectorViewModel : INotifyPropertyChanged
{
    private string configPath;
    private readonly IConfigurationService configuration;
    private readonly ICollectorService collector;
    private readonly IAppLogger logger;
    private readonly IEventAggregator events;
    private readonly ICollectionScheduleState schedules;
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
    public string Status { get => status; private set { status = value; Changed(); events.GetEvent<StatusEvent>().Publish(value); } }
    public string Summary { get => summary; private set { summary = value; Changed(); } }
    public bool AutoRepeat { get => autoRepeat; set { autoRepeat = value; Changed(); } }
    public bool UseDateRange { get => useDateRange; set { useDateRange = value; Changed(); } }
    public IReadOnlyList<int> IntervalChoices { get; } = [1, 5, 10, 30, 60];
    public int IntervalMinutes { get => intervalMinutes; set { intervalMinutes = value; Changed(); } }
    public ICommand StartCommand => start;
    public ICommand StopCommand => stop;

    public CollectorViewModel(IConfigurationService configuration, ICollectorService collector, IAppLogger logger, IEventAggregator events, ICollectionScheduleState schedules)
    {
        this.schedules = schedules;
        this.configuration = configuration; this.collector = collector; this.logger = logger; this.events = events;
        configPath = configuration.DefaultPath;
        events.GetEvent<ShutdownEvent>().Subscribe(RequestStop);
        start = new RelayCommand(() => _ = RunAsync(), () => running == null && configValid);
        stop = new RelayCommand(RequestStop, () => running != null);
        events.GetEvent<ConfigurationSavedEvent>().Subscribe(path =>
        {
            if (running != null) return; // active run owns its immutable options snapshot
            try { if (Path.GetFullPath(configPath).Equals(path, StringComparison.OrdinalIgnoreCase)) CheckConfig(); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { ShowBrowseError(error); }
        }, ThreadOption.UIThread);
        CheckConfig();
    }
    private CollectorOptions ReadOptions(string path)
    {
        var options = configuration.Read<CollectorOptions>(path);
        options.Validate(Path.GetDirectoryName(Path.GetFullPath(path))!);
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
        if ((useDateRange && (fromDate == null || toDate == null || fromDate > toDate)) || (autoRepeat && intervalMinutes is < 1 or > 1440))
        { Status = "기간과 주기를 확인하세요."; return; }
        var automatic = autoRepeat; var manual = useDateRange; var interval = intervalMinutes;
        var selectedFrom = fromDate; var selectedTo = toDate;
        using var cts = new CancellationTokenSource(); running = cts; start.Refresh(); stop.Refresh();
        var ui = Application.Current.Dispatcher;
        void Present(Action action) { if (!ui.HasShutdownStarted) _ = ui.BeginInvoke(action); }
        try
        {
            var options = ReadOptions(configPath);
            Messages.Clear(); SourceStatuses.Clear(); Summary = "";
            // Schedule ownership and waits run off the WPF synchronization context.
            await Task.Run(async () =>
            {
                if (automatic) schedules.Start(options.DatabasePath);
                try
                {
                    var firstScan = true;
                    do
                    {
                        var allDates = !manual && (firstScan || !automatic);
                        var first = manual ? DateOnly.FromDateTime(selectedFrom!.Value) : DateOnly.FromDateTime(DateTime.Today.AddDays(-2));
                        var last = manual ? DateOnly.FromDateTime(selectedTo!.Value) : DateOnly.FromDateTime(DateTime.Today);
                        Present(() => Status = "수집 및 복구 확인 중…");
                        try
                        {
                            var result = await collector.RunAsync(options, allDates ? DateOnly.MinValue : first, allDates ? DateOnly.MaxValue : last,
                                item => Present(() =>
                                {
                                    Messages.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {item.MachineId}/{item.PcId} {item.Path} · {item.Message}");
                                    if (Messages.Count > 1000) Messages.RemoveAt(Messages.Count - 1);
                                }), cts.Token, !manual).ConfigureAwait(false);
                            Present(() => ShowResult(result));
                        }
                        catch (OperationCanceledException) when (cts.IsCancellationRequested) { throw; }
                        catch (Exception error)
                        {
                            logger.Write("수집 회차 실패",error);
                            Present(() => { Status = "수집 실패: " + error.Message; Messages.Insert(0,Status); });
                            if (!automatic) break;
                        }
                        firstScan = false;
                        if (!automatic) break;
                        schedules.Waiting(options.DatabasePath,DateTime.UtcNow.AddMinutes(interval));
                        Present(() => Status += $" 다음 확인까지 {interval}분 대기 중…");
                        await Task.Delay(TimeSpan.FromMinutes(interval),cts.Token).ConfigureAwait(false);
                    } while (!cts.IsCancellationRequested);
                }
                finally { if (automatic) schedules.Stop(options.DatabasePath); }
            });
        }
        catch (OperationCanceledException) { Status = "중지 완료 · 다음 시작 시 미완료 이력을 재확인합니다."; }
        catch (Exception error) { Status = "실행 실패: " + error.Message; logger.Write(Status,error); }
        finally { running = null; start.Refresh(); stop.Refresh(); }
    }
    private void ShowResult(CollectionResult result)
    {
                SourceStatuses.Clear();
                foreach (var source in result.Sources)
                {
                    if (!string.IsNullOrWhiteSpace(source.Path))
                        SourceStatuses.Add($"{source.MachineId}/{source.PcId} 로그 · {source.Path} · 날짜 {source.DateFolders}개 · .log {source.Files}개 · 갱신 {source.Updated} · 변경 없음 {source.Unchanged} · 마지막 변경 {(source.LatestWriteUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "없음")}");
                    if (!string.IsNullOrWhiteSpace(source.ImagePath))
                        SourceStatuses.Add($"{source.MachineId}/{source.PcId} 이미지 · {source.ImagePath} · 발견 {source.ImageFiles} · 복사 {source.ImagesCopied} · 변경 없음 {source.ImagesSkipped} · 안정화 대기 {source.ImagesDeferred} · 실패 {source.ImageFailed}");
                }
                Summary = $"로그 {result.FilesFound}개 · 신규/갱신 {result.Collected} · 추가분 {result.AppendedBytes:N0}바이트 · 이미지 {result.ImagesFound}개 · 복사 {result.ImagesCopied} · 기존 {result.ImagesSkipped} · 안정화 대기 {result.ImagesDeferred} · 실패 {result.Failed}";
                Status = result.Failed + result.Deferred > 0 ? "확인이 필요한 항목이 있습니다." :
                    result.ImagesDeferred > 0 ? "이미지 저장 안정화 대기 중입니다. 다음 확인에서 다시 검사합니다." :
                    result.FilesFound + result.ImagesFound == 0 ? "확인 범위에 로그·이미지가 없습니다. 경로를 확인하세요." : "이번 확인 완료.";
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

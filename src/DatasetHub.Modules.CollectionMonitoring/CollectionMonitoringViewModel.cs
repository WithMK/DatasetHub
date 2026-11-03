using System.Collections.ObjectModel;
using System.Windows.Data;
using System.Windows.Threading;
using DatasetHub.Core;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
namespace DatasetHub.Modules.CollectionMonitoring;
public sealed class CollectionMonitoringViewModel : BindableBase, INavigationAware
{
    private readonly ICollectionMonitoringService service;
    private readonly IAppLogger logger;
    private readonly IConfirmationService dialogs;
    private readonly DispatcherTimer timer;
    private string configPath, search = "", filter = "전체", status = "설정 파일을 지정하고 새로고침하세요.", summary = "";
    private bool refreshing;
    private CancellationTokenSource? retry;
    private SourceCollectionState? selected;
    private int detailVersion;
    private int graceMinutes = 2;
    private DateTime? from = DateTime.Today, to = DateTime.Today;
    public ObservableCollection<SourceCollectionState> Sources { get; } = [];
    public System.ComponentModel.ICollectionView SourcesView { get; }
    public ObservableCollection<CollectionRunEntry> Runs { get; } = [];
    public ObservableCollection<CollectionErrorEntry> Errors { get; } = [];
    public string ConfigPath { get => configPath; set { if (SetProperty(ref configPath, value)) { detailVersion++; Sources.Clear(); Runs.Clear(); Errors.Clear(); Selected = null; Summary = ""; } } }
    public string Search { get => search; set { SetProperty(ref search, value); SourcesView.Refresh(); } }
    public string StateFilter { get => filter; set { SetProperty(ref filter, value); SourcesView.Refresh(); } }
    public IReadOnlyList<string> Filters { get; } = ["전체", "정상", "수집 중", "지연", "실패", "이력 없음", "비활성", "중단"];
    public int DelayGraceMinutes { get => graceMinutes; set => SetProperty(ref graceMinutes, value); }
    public DateTime? FromDate { get => from; set => SetProperty(ref from, value); }
    public DateTime? ToDate { get => to; set => SetProperty(ref to, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Summary { get => summary; private set => SetProperty(ref summary, value); }
    public SourceCollectionState? Selected { get => selected; set { if (SetProperty(ref selected, value)) { _ = LoadDetailsAsync(); RetryPcCommand?.RaiseCanExecuteChanged(); RetryMachineCommand?.RaiseCanExecuteChanged(); } } }
    public DelegateCommand RefreshCommand { get; }
    public DelegateCommand RetryPcCommand { get; }
    public DelegateCommand RetryMachineCommand { get; }
    public DelegateCommand StopCommand { get; }
    public CollectionMonitoringViewModel(ICollectionMonitoringService service, IConfigurationService configuration, IAppLogger logger, IConfirmationService dialogs, IEventAggregator events)
    {
        this.service = service; this.logger = logger; this.dialogs = dialogs; configPath = configuration.DefaultPath;
        SourcesView = CollectionViewSource.GetDefaultView(Sources);
        SourcesView.Filter = item => item is SourceCollectionState row && (filter == "전체" || row.State == filter) &&
            (row.MachineId.Contains(search, StringComparison.OrdinalIgnoreCase) || row.PcId.Contains(search, StringComparison.OrdinalIgnoreCase));
        RefreshCommand = new(async () => await RefreshAsync());
        RetryPcCommand = new(async () => await RetryAsync(false), CanRetry);
        RetryMachineCommand = new(async () => await RetryAsync(true), CanRetry);
        StopCommand = new(() => retry?.Cancel(), () => retry != null);
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += async (_, _) => await RefreshAsync();
        events.GetEvent<ConfigurationSavedEvent>().Subscribe(async path =>
        {
            try { if (string.Equals(System.IO.Path.GetFullPath(configPath), path, StringComparison.OrdinalIgnoreCase)) await RefreshAsync(); }
            catch (Exception error) { logger.Write("설정 저장 알림 경로 확인 실패", error); }
        }, ThreadOption.UIThread);
        events.GetEvent<ShutdownEvent>().Subscribe(() => { timer.Stop(); retry?.Cancel(); });
    }
    private bool CanRetry() => retry == null && Selected != null && Selected.State != "비활성";
    public async Task RefreshAsync()
    {
        if (refreshing) return;
        refreshing = true; var path = ConfigPath;
        try
        {
            var snapshot = await service.ReadAsync(path, TimeSpan.FromMinutes(DelayGraceMinutes));
            if (path != ConfigPath) return;
            var key = Selected;
            Sources.Clear(); foreach (var row in snapshot.Sources) Sources.Add(row);
            Selected = Sources.FirstOrDefault(x => x.MachineId == key?.MachineId && x.PcId == key.PcId) ?? Sources.FirstOrDefault();
            Summary = $"설비 {snapshot.EquipmentCount}대 / PC {snapshot.PcCount}대 · 정상 {snapshot.Sources.Count(x => x.State == "정상")} · 실패·지연 {snapshot.Sources.Count(x => x.State is "실패" or "지연")} · 마지막 전체 실행 {snapshot.LastFullRunUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "없음"}";
            Status = "조회 완료 · 5초 자동 갱신 · 실적은 각 PC의 마지막 실행 기준입니다.";
        }
        catch (Exception error) { Status = "조회 실패: " + error.Message; logger.Write(Status, error); }
        finally { refreshing = false; }
    }
    private async Task LoadDetailsAsync()
    {
        var version = ++detailVersion; var row = Selected; var path = ConfigPath;
        Runs.Clear(); Errors.Clear(); if (row == null) return;
        try
        {
            var runs = await service.RunsAsync(path, row.MachineId, row.PcId);
            var errors = await service.ErrorsAsync(path, row.MachineId, row.PcId);
            if (version != detailVersion || path != ConfigPath) return;
            foreach (var run in runs) Runs.Add(run); foreach (var error in errors) Errors.Add(error);
        }
        catch (Exception error) { if (version == detailVersion) Status = "상세 조회 실패: " + error.Message; }
    }
    private async Task RetryAsync(bool machine)
    {
        if (!CanRetry() || Selected == null) return;
        if (FromDate == null || ToDate == null || FromDate > ToDate) { Status = "재수집 기간을 확인하세요."; return; }
        var row = Selected; var path = ConfigPath;
        if (!dialogs.Confirm($"{row.MachineId}/{(machine ? "전체 PC" : row.PcId)}의 지정 기간을 다시 확인합니까? 변경 없는 파일은 건너뜁니다.")) return;
        retry = new CancellationTokenSource(); RetryPcCommand.RaiseCanExecuteChanged(); RetryMachineCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged();
        try { await service.RetryAsync(path, row.MachineId, machine ? null : row.PcId, DateOnly.FromDateTime(FromDate.Value), DateOnly.FromDateTime(ToDate.Value), retry.Token); await RefreshAsync(); Status = "재수집 실행 종료 · PC별 상태와 오류 이력을 확인하세요."; }
        catch (OperationCanceledException) { Status = "재수집을 중지했습니다."; }
        catch (Exception error) { Status = "재수집 실패 (다른 수집 실행 중인지 확인): " + error.Message; logger.Write(Status, error); }
        finally { retry.Dispose(); retry = null; RetryPcCommand.RaiseCanExecuteChanged(); RetryMachineCommand.RaiseCanExecuteChanged(); StopCommand.RaiseCanExecuteChanged(); }
    }
    public void OnNavigatedTo(NavigationContext context) { timer.Start(); _ = RefreshAsync(); }
    public void OnNavigatedFrom(NavigationContext context) => timer.Stop();
    public bool IsNavigationTarget(NavigationContext context) => true;
}

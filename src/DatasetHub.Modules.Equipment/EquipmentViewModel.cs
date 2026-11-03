using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using DatasetHub.Core;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
namespace DatasetHub.Modules.Equipment;
public sealed class EquipmentViewModel : BindableBase, IConfirmNavigationRequest
{
    private readonly IEquipmentConfigurationService configurations;
    private readonly IConfirmationService confirmations;
    private readonly IPathProbeService probes;
    private readonly IAppLogger logger;
    private readonly IEventAggregator events;
    private EquipmentDocument? document;
    private string configPath;
    private string status = "설정 파일 경로를 확인하고 불러오기를 누르세요.";
    private bool isDirty, isBusy, confirming;
    private EquipmentEditor? selectedEquipment;
    private PcEditor? selectedPc;
    public ObservableCollection<EquipmentEditor> Equipments { get; } = [];
    public string ConfigPath { get => configPath; set => SetProperty(ref configPath, value); }
    public string LoadedPath => document?.Path ?? "불러온 파일 없음";
    public string Status { get => status; private set { SetProperty(ref status, value); events.GetEvent<StatusEvent>().Publish(value); } }
    public bool IsDirty { get => isDirty; private set => SetProperty(ref isDirty, value); }
    public bool IsBusy { get => isBusy; private set { SetProperty(ref isBusy, value); RaisePropertyChanged(nameof(CanEdit)); Refresh(); } }
    public bool CanEdit => document != null && !IsBusy;
    public EquipmentEditor? SelectedEquipment { get => selectedEquipment; set { SetProperty(ref selectedEquipment, value); SelectedPc = value?.Pcs.FirstOrDefault(); Refresh(); } }
    public PcEditor? SelectedPc { get => selectedPc; set { SetProperty(ref selectedPc, value); Refresh(); } }
    public DelegateCommand LoadCommand { get; }
    public DelegateCommand SaveCommand { get; }
    public DelegateCommand AddEquipmentCommand { get; }
    public DelegateCommand DeleteEquipmentCommand { get; }
    public DelegateCommand AddPcCommand { get; }
    public DelegateCommand DeletePcCommand { get; }
    public DelegateCommand CheckConnectionsCommand { get; }
    public EquipmentViewModel(IEquipmentConfigurationService configurations, IConfigurationService defaults,
        IConfirmationService confirmations, IPathProbeService probes, ICloseGuard closeGuard, IAppLogger logger, IEventAggregator events)
    {
        this.configurations = configurations; this.confirmations = confirmations; this.probes = probes; this.logger = logger; this.events = events;
        configPath = defaults.DefaultPath;
        LoadCommand = new(async () => await LoadAsync(), () => !IsBusy);
        SaveCommand = new(async () => await SaveAsync(), () => CanEdit);
        AddEquipmentCommand = new(AddEquipment, () => CanEdit);
        DeleteEquipmentCommand = new(DeleteEquipment, () => CanEdit && SelectedEquipment != null);
        AddPcCommand = new(AddPc, () => CanEdit && SelectedEquipment != null);
        DeletePcCommand = new(DeletePc, () => CanEdit && SelectedPc != null);
        CheckConnectionsCommand = new(async () => await CheckConnectionsAsync(), () => CanEdit);
        closeGuard.Register(ConfirmLeaveAsync);
    }
    private void Refresh()
    {
        LoadCommand?.RaiseCanExecuteChanged(); SaveCommand?.RaiseCanExecuteChanged(); AddEquipmentCommand?.RaiseCanExecuteChanged();
        DeleteEquipmentCommand?.RaiseCanExecuteChanged(); AddPcCommand?.RaiseCanExecuteChanged(); DeletePcCommand?.RaiseCanExecuteChanged(); CheckConnectionsCommand?.RaiseCanExecuteChanged();
    }
    private void Changed() => IsDirty = true;
    private static JsonObject NewObject() => new(new JsonNodeOptions { PropertyNameCaseInsensitive = true });
    private void AddEquipment()
    {
        var id = Unique("MACHINE", Equipments.Select(x => x.MachineId));
        var data = NewObject(); data["machineId"] = id; data["enabled"] = true; data["pcs"] = new JsonArray();
        var item = new EquipmentEditor(data, Changed, isNew: true); Equipments.Add(item); SelectedEquipment = item; Changed();
    }
    private void DeleteEquipment()
    {
        if (SelectedEquipment == null || !confirmations.Confirm("선택 설비를 설정에서 삭제합니까? 기존 archive와 수집 이력은 삭제되지 않습니다.")) return;
        Equipments.Remove(SelectedEquipment); SelectedEquipment = Equipments.FirstOrDefault(); Changed();
    }
    private void AddPc()
    {
        if (SelectedEquipment == null) return;
        var data = NewObject(); data["pcId"] = Unique("PC", SelectedEquipment.Pcs.Select(x => x.PcId)); data["sourceRoot"] = ""; data["imageSourceRoot"] = "";
        var pc = new PcEditor(data, Changed); SelectedEquipment.Pcs.Add(pc); SelectedPc = pc;
    }
    private void DeletePc()
    {
        if (SelectedEquipment == null || SelectedPc == null || !confirmations.Confirm("선택 PC를 설정에서 삭제합니까? 수집 데이터는 삭제되지 않습니다.")) return;
        SelectedEquipment.Pcs.Remove(SelectedPc); SelectedPc = SelectedEquipment.Pcs.FirstOrDefault();
    }
    private static string Unique(string prefix, IEnumerable<string> values)
    {
        var used = values.ToHashSet(StringComparer.OrdinalIgnoreCase); var n = 1;
        while (used.Contains($"{prefix}{n:00}")) n++;
        return $"{prefix}{n:00}";
    }
    private EquipmentDocument Snapshot()
    {
        if (document == null) throw new InvalidOperationException("먼저 설정을 불러오세요.");
        var json = (JsonObject)document.Json.DeepClone();
        json["equipments"] = new JsonArray(Equipments.Select(x => (JsonNode)x.Export()).ToArray());
        return document with { Json = json };
    }
    private void Apply(EquipmentDocument loaded)
    {
        document = loaded;
        Equipments.Clear();
        foreach (var item in loaded.Json["equipments"]?.AsArray() ?? [])
            Equipments.Add(new EquipmentEditor((JsonObject)item!.DeepClone(), Changed));
        SelectedEquipment = Equipments.FirstOrDefault(); IsDirty = false;
        RaisePropertyChanged(nameof(LoadedPath)); RaisePropertyChanged(nameof(CanEdit)); Refresh();
    }
    public async Task<bool> LoadAsync()
    {
        if (IsBusy) return false;
        var requested = ConfigPath;
        if (!await ConfirmLeaveAsync()) return false;
        IsBusy = true;
        try { Apply(await configurations.LoadAsync(requested)); ConfigPath = LoadedPath; Status = $"불러오기 완료 · 설비 {Equipments.Count}대"; return true; }
        catch (Exception error) { Report("불러오기 실패", error); return false; }
        finally { IsBusy = false; }
    }
    public async Task<bool> SaveAsync()
    {
        if (!CanEdit) return false;
        try
        {
            var snapshot = Snapshot(); configurations.Validate(snapshot);
            var renamed = Equipments.Where(x => x.OriginalMachineId.Length > 0 && x.OriginalMachineId != x.MachineId).ToArray();
            if (renamed.Length > 0 && !confirmations.Confirm("MachineId 변경: " + string.Join(", ", renamed.Select(x => x.OriginalMachineId + " → " + x.MachineId)) +
                "\n변경된 ID는 다른 설비 식별자로 취급됩니다. 기존 이력과 archive는 유지되며 자동 이동/연결되지 않습니다. 저장합니까?")) return false;
            IsBusy = true;
            Apply(await configurations.SaveAsync(snapshot));
            Status = "저장 완료 · 현재 수집은 시작 시 설정을 유지합니다. 중지 후 다음 수집 시작부터 적용됩니다.";
            logger.Write("설비 설정 저장: " + LoadedPath);
            events.GetEvent<ConfigurationSavedEvent>().Publish(LoadedPath); return true;
        }
        catch (Exception error) { Report("저장 실패", error); return false; }
        finally { IsBusy = false; }
    }
    public async Task CheckConnectionsAsync()
    {
        if (!CanEdit || document == null) return;
        IsBusy = true;
        try
        {
            var tasks = Equipments.SelectMany(x => x.Pcs).Select(async pc =>
            {
                pc.Connection = "확인 중…";
                var paths = new[] { ("로그", pc.SourceRoot), ("이미지", pc.ImageSourceRoot) }.Where(x => !string.IsNullOrWhiteSpace(x.Item2));
                var results = await Task.WhenAll(paths.Select(async pair =>
                {
                    try { var result = await probes.CheckAsync(pair.Item2, document.Path); return $"{pair.Item1}: {result.Message} [{result.Path}]"; }
                    catch (Exception error) { return $"{pair.Item1}: 연결 실패 · {error.Message}"; }
                }));
                pc.Connection = results.Length == 0 ? "미설정 · 로그 또는 이미지 경로가 필요합니다." : string.Join("\n", results);
            });
            await Task.WhenAll(tasks); Status = "연결 확인 완료 · PC별 결과를 확인하세요. 실제 파일 복사는 하지 않았습니다.";
        }
        catch (Exception error) { Report("연결 확인 실패", error); }
        finally { IsBusy = false; }
    }
    public async Task<bool> ConfirmLeaveAsync()
    {
        if (IsBusy || confirming) return false;
        if (!IsDirty) return true;
        confirming = true;
        try
        {
            switch (confirmations.UnsavedChanges())
            {
                case UnsavedDecision.Save: return await SaveAsync();
                case UnsavedDecision.Discard: if (document != null) Apply(document); return true;
                default: return false;
            }
        }
        finally { confirming = false; }
    }
    private void Report(string action, Exception error) { Status = action + ": " + error.Message; logger.Write(action, error); }
    public async void ConfirmNavigationRequest(NavigationContext navigationContext, Action<bool> continuationCallback) => continuationCallback(await ConfirmLeaveAsync());
    public bool IsNavigationTarget(NavigationContext navigationContext) => true;
    public void OnNavigatedTo(NavigationContext navigationContext) { }
    public void OnNavigatedFrom(NavigationContext navigationContext) { }
}

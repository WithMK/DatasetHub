using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Prism.Mvvm;
namespace DatasetHub.Modules.Equipment;
public sealed class PcEditor : BindableBase
{
    private readonly JsonObject data;
    private readonly Action changed;
    private string connection = "미확인";
    public PcEditor(JsonObject data, Action changed) { this.data = data; this.changed = changed; }
    private string Read(string name) => data[name]?.GetValue<string>() ?? "";
    private void Write(string name, string value) { if (Read(name) == value) return; data[name] = value; RaisePropertyChanged(char.ToUpperInvariant(name[0]) + name[1..]); Connection = "미확인"; changed(); }
    public string PcId { get => Read("pcId"); set => Write("pcId", value); }
    public string SourceRoot { get => Read("sourceRoot"); set => Write("sourceRoot", value); }
    public string ImageSourceRoot { get => Read("imageSourceRoot"); set => Write("imageSourceRoot", value); }
    public string Connection { get => connection; set => SetProperty(ref connection, value); }
    public JsonObject Export() => (JsonObject)data.DeepClone();
}
public sealed class EquipmentEditor : BindableBase
{
    private readonly JsonObject data;
    private readonly Action changed;
    public string OriginalMachineId { get; }
    public ObservableCollection<PcEditor> Pcs { get; } = [];
    public EquipmentEditor(JsonObject data, Action changed, bool isNew = false)
    {
        this.data = data; this.changed = changed;
        OriginalMachineId = isNew ? "" : MachineId;
        foreach (var pc in data["pcs"]?.AsArray() ?? []) Pcs.Add(new PcEditor((JsonObject)pc!, changed));
        Pcs.CollectionChanged += (_, _) => changed();
    }
    public string MachineId
    {
        get => data["machineId"]?.GetValue<string>() ?? "";
        set { if (MachineId == value) return; data["machineId"] = value; RaisePropertyChanged(); changed(); }
    }
    public bool Enabled
    {
        get => data["enabled"]?.GetValue<bool>() ?? false;
        set { if (Enabled == value) return; data["enabled"] = value; RaisePropertyChanged(); changed(); }
    }
    public JsonObject Export()
    {
        var copy = (JsonObject)data.DeepClone();
        copy["pcs"] = new JsonArray(Pcs.Select(x => (JsonNode)x.Export()).ToArray());
        return copy;
    }
}

using System.Text.Json.Nodes;
namespace DatasetHub.Core;

// A detached editable document. Original field spelling and relative paths are preserved.
public sealed record EquipmentDocument(string Path, string Revision, JsonObject Json);
public interface IEquipmentConfigurationService
{
    Task<EquipmentDocument> LoadAsync(string path);
    Task<EquipmentDocument> SaveAsync(EquipmentDocument document);
    void Validate(EquipmentDocument document);
}
public enum UnsavedDecision { Save, Discard, Cancel }
public interface IConfirmationService
{
    bool Confirm(string message);
    UnsavedDecision UnsavedChanges();
}
public interface ICloseGuard
{
    void Register(Func<Task<bool>> guard);
    Task<bool> CanCloseAsync();
}
public sealed record PathAccessResult(string Path, bool Success, string Message);
public interface IPathProbeService
{
    Task<PathAccessResult> CheckAsync(string path, string configurationPath);
}
public interface IPathReader { PathAccessResult Read(string fullPath); }

public sealed class ConfigurationSavedEvent : Prism.Events.PubSubEvent<string> { }

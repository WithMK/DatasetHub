using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using DatasetHub.Core;
using Prism.Events;
using Prism.Regions;
namespace DatasetHub.Infrastructure;
public sealed class MenuRegistry : IMenuRegistry
{
    private readonly ObservableCollection<MenuEntry> items = [];
    public ReadOnlyObservableCollection<MenuEntry> Items { get; }
    public MenuRegistry() => Items = new(items);
    public void Register(MenuEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Target) || items.Any(x => x.Target == entry.Target))
            throw new ArgumentException("화면 이름은 비어 있거나 중복될 수 없습니다.");
        items.Add(entry);
    }
}
public sealed class ConfigurationService : IConfigurationService
{
    public string DefaultPath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            var root = dir.Parent?.Name.Equals("run", StringComparison.OrdinalIgnoreCase) == true ? dir.Parent.Parent!.FullName : dir.FullName;
            return Path.Combine(root, "config", "collector.local-example.json");
        }
    }
    public T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.GetFullPath(path)), new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    }) ?? throw new InvalidDataException("빈 설정 파일입니다.");
}
public sealed class FileLogger : IAppLogger
{
    private readonly object gate = new();
    public void Write(string message, Exception? error = null)
    {
        try
        {
            lock (gate)
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DatasetHub", "logs");
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, $"desktop-{DateTime.Today:yyyyMMdd}.log"), $"{DateTime.Now:O} {message} {error}{Environment.NewLine}");
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Trace.WriteLine(e); }
    }
}
public sealed class AppDialog : IAppDialog
{
    public void ShowError(string message) => MessageBox.Show(message, "DatasetHub", MessageBoxButton.OK, MessageBoxImage.Error);
}
public sealed class AppNavigation(IRegionManager regions, IAppLogger logger, IAppDialog dialogs, IEventAggregator events) : IAppNavigation
{
    public void Navigate(string target) => regions.RequestNavigate(RegionNames.Main, target, result =>
    {
        if (result.Result == true) events.GetEvent<StatusEvent>().Publish($"화면: {target}");
        else
        {
            logger.Write($"화면 전환 실패: {target}", result.Error);
            dialogs.ShowError($"화면 전환 실패: {target}\n{result.Error?.Message}");
        }
    });
}

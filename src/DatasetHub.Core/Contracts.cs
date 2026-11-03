using System.Collections.ObjectModel;
using Prism.Events;
namespace DatasetHub.Core;
public sealed record MenuEntry(string Title, string Target);
public interface IMenuRegistry { ReadOnlyObservableCollection<MenuEntry> Items { get; } void Register(MenuEntry entry); }
public interface IAppNavigation { void Navigate(string target); }
public interface IConfigurationService { string DefaultPath { get; } T Read<T>(string path); }
public interface IAppLogger { void Write(string message, Exception? error = null); }
public interface IAppDialog { void ShowError(string message); }
public sealed class StatusEvent : PubSubEvent<string> { }
public sealed class ShutdownEvent : PubSubEvent { }
public static class RegionNames { public const string Main = "MainRegion"; }

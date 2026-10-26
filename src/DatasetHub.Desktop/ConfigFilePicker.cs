using System.Windows;

namespace DatasetHub.Desktop;

public interface IConfigFileDialog
{
    bool? Show();
    string FileName { get; }
}

internal sealed class ConfigFileDialog(Window owner, string currentPath) : IConfigFileDialog
{
    private readonly ConfigPathWindow window = new(currentPath) { Owner = owner };
    public bool? Show() => window.ShowDialog();
    public string FileName => window.SelectedPath ?? "";
}

public static class ConfigFilePicker
{
    public static bool Select(IConfigFileDialog dialog, Action<string> setPath)
    {
        if (dialog.Show() != true) return false;
        if (string.IsNullOrWhiteSpace(dialog.FileName)) return false;
        setPath(dialog.FileName);
        return true;
    }
}

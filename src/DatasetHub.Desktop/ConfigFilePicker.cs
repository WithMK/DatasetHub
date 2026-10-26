using Microsoft.Win32;

namespace DatasetHub.Desktop;

public interface IConfigFileDialog
{
    bool? Show();
    string FileName { get; }
}

internal sealed class ConfigFileDialog : IConfigFileDialog
{
    private readonly OpenFileDialog dialog = new()
    {
        Filter = "JSON 설정 (*.json)|*.json|모든 파일 (*.*)|*.*",
        CheckFileExists = true
    };
    public bool? Show() => dialog.ShowDialog();
    public string FileName => dialog.FileName;
}

public static class ConfigFilePicker
{
    // Cancel, Escape and window-close all return without changing the current path.
    public static bool Select(IConfigFileDialog dialog, Action<string> setPath)
    {
        if (dialog.Show() != true) return false;
        if (string.IsNullOrWhiteSpace(dialog.FileName)) return false;
        setPath(dialog.FileName);
        return true;
    }
}

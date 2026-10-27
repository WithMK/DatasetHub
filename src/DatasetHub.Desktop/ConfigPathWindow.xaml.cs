using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DatasetHub.Desktop;

public partial class ConfigPathWindow : Window
{
    private sealed record Entry(string Path, bool Folder)
    {
        public override string ToString() => (Folder ? "[폴더] " : "[JSON] ") + System.IO.Path.GetFileName(Path);
    }

    public string? SelectedPath { get; private set; }

    public ConfigPathWindow(string currentPath)
    {
        InitializeComponent();
        SelectedFilePath.Text = currentPath;
        var directory = !string.IsNullOrWhiteSpace(currentPath) ? System.IO.Path.GetDirectoryName(currentPath) : null;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            directory = ConfigFileLocator.DirectoryPath;
        Navigate(Directory.Exists(directory) ? directory : AppContext.BaseDirectory);
    }

    private void Navigate(string directory)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(directory);
            var items = Directory.GetDirectories(full).OrderBy(x => x).Select(x => new Entry(x, true))
                .Concat(Directory.GetFiles(full, "*.json").OrderBy(x => x).Select(x => new Entry(x, false)))
                .ToArray();
            Entries.ItemsSource = items;
            FolderPath.Text = full;
            ErrorText.Text = "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ErrorText.Text = "폴더를 열 수 없습니다: " + e.Message;
        }
    }
    private void Up_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(FolderPath.Text);
        if (parent != null) Navigate(parent.FullName);
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Navigate(FolderPath.Text);
    private void Entries_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Entries.SelectedItem is Entry { Folder: false } selected) SelectedFilePath.Text = selected.Path;
    }
    private void Entries_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Entries.SelectedItem is not Entry entry) return;
        if (entry.Folder) Navigate(entry.Path);
        else SelectCurrentFile();
    }
    private void Select_Click(object sender, RoutedEventArgs e) => SelectCurrentFile();
    private void SelectCurrentFile()
    {
        try
        {
            var path = System.IO.Path.GetFullPath(SelectedFilePath.Text);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            { ErrorText.Text = "존재하는 JSON 설정 파일을 선택하세요."; return; }
            SelectedPath = path;
            DialogResult = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { ErrorText.Text = "파일을 선택할 수 없습니다: " + e.Message; }
    }
    public void CancelSelection() => DialogResult = false;
    private void Cancel_Click(object sender, RoutedEventArgs e) => CancelSelection();
}

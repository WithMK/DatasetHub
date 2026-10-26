using System.Windows;
using Microsoft.Win32;
namespace DatasetHub.Desktop;
public partial class MainWindow : Window
{
    private readonly CollectorViewModel viewModel = new();
    public MainWindow() { InitializeComponent(); DataContext = viewModel; }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "JSON 설정 (*.json)|*.json|모든 파일 (*.*)|*.*" };
        if (dialog.ShowDialog(this) == true) viewModel.ConfigPath = dialog.FileName;
    }
    protected override void OnClosed(EventArgs e) { viewModel.RequestStop(); base.OnClosed(e); }
}

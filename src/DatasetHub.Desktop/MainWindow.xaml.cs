using System.Windows;
namespace DatasetHub.Desktop;
public partial class MainWindow : Window
{
    private readonly CollectorViewModel viewModel = new();
    public MainWindow() { InitializeComponent(); DataContext = viewModel; }
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        try { ConfigFilePicker.Select(new ConfigFileDialog(this, viewModel.ConfigPath), path => viewModel.ConfigPath = path); }
        catch (Exception ex)
        {
            // Keep the main window open if the in-app selector encounters an error.
            viewModel.ShowBrowseError(ex);
        }
    }
    protected override void OnClosed(EventArgs e) { viewModel.RequestStop(); base.OnClosed(e); }
}

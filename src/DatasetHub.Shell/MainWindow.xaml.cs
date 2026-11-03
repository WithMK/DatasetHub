using System.Windows;
namespace DatasetHub.Shell;
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
}

using System.Windows;
using System.Windows.Controls;
namespace DatasetHub.Desktop;
public partial class CollectorView : UserControl
{
    public CollectorView() => InitializeComponent();
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var vm = (CollectorViewModel)DataContext;
        try { ConfigFilePicker.Select(new ConfigFileDialog(Window.GetWindow(this), vm.ConfigPath), path => vm.ConfigPath = path); }
        catch (Exception error) { vm.ShowBrowseError(error); }
    }
}

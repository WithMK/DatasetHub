using DatasetHub.Core;
using Prism.Ioc;
using Prism.Regions;
using Prism.Events;
using System.Windows.Threading;
using DatasetHub.Desktop;

var current = "previous.json";
void Set(string path) => current = path;
if (ConfigFilePicker.Select(new Stub(false, "new.json"), Set) || current != "previous.json") throw new Exception("Cancel changed path.");
if (ConfigFilePicker.Select(new Stub(null, "new.json"), Set) || current != "previous.json") throw new Exception("Close changed path.");
if (ConfigFilePicker.Select(new Stub(true, ""), Set) || current != "previous.json") throw new Exception("Empty filename changed path.");
if (!ConfigFilePicker.Select(new Stub(true, "new.json"), Set) || current != "new.json") throw new Exception("Selection failed.");
Console.WriteLine("PASS file dialog cancel/close/selection");
// Exercise the actual WPF modal cancel path on an STA thread.
Exception? windowError = null;
var uiThread = new Thread(() =>
{
    try
    {
        var dialog = new ConfigPathWindow("previous.json");
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(dialog.CancelSelection);
        if (dialog.ShowDialog() != false) throw new Exception("WPF modal cancel did not return false.");
        var app = new App();
        app.InitializeComponent();
        app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            try
            {
                ((IContainerRegistry)app.Container).RegisterInstance<IConfirmationService>(new EquipmentTests.TestConfirmation());
                var menu = app.Container.Resolve<IMenuRegistry>();
                if (menu.Items.Count != 3) throw new Exception("Static modules did not register menus.");
                try { menu.Register(menu.Items[0]); throw new Exception("Duplicate menu accepted."); }
                catch (ArgumentException) { }
                var regions = app.Container.Resolve<IRegionManager>();
                object Navigate(string target)
                {
                    bool? success = null;
                    regions.RequestNavigate(RegionNames.Main, target, result => success = result.Result);
                    if (success != true) throw new Exception("Navigation failed: " + target);
                    return regions.Regions[RegionNames.Main].ActiveViews.Single();
                }
                var first = (System.Windows.FrameworkElement)Navigate("Collector");
                if (first.DataContext is not CollectorViewModel) throw new Exception("Collector auto wiring failed.");
                var example = (System.Windows.FrameworkElement)Navigate("FrameworkExample");
                var vm = (DatasetHub.Modules.Example.ExampleViewModel)example.DataContext;
                vm.PublishCommand.Execute();
                // UIThread events are posted to the dispatcher; drain before asserting.
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
                var shell = (DatasetHub.Shell.ShellViewModel)app.MainWindow.DataContext;
                if (!shell.Status.Contains("예제 모듈")) throw new Exception("EventAggregator did not update Shell.");
                var returned = (System.Windows.FrameworkElement)Navigate("Collector");
                if (!ReferenceEquals(first.DataContext, returned.DataContext)) throw new Exception("Collector state lost on navigation.");
                var equipment = (System.Windows.FrameworkElement)Navigate("Equipment");
                if (equipment.DataContext is not DatasetHub.Modules.Equipment.EquipmentViewModel) throw new Exception("Equipment auto wiring failed.");
                Navigate("Collector");
                await EquipmentTests.RunAsync(app, regions);
                Console.WriteLine("PASS Prism modules, duplicate menu guard, Region navigation, DI, auto wiring, events, retained collector state");
            }
            catch (Exception e) { windowError = e; }
            finally { app.Shutdown(); }
        }));
        app.Run();
    }
    catch (Exception e) { windowError = e; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
if (!uiThread.Join(TimeSpan.FromSeconds(90))) throw new Exception("WPF modal dialog failed to close.");
if (windowError != null) throw windowError;
Console.WriteLine("PASS WPF modal cancel closes selector only");

record Stub(bool? Result, string FileName) : IConfigFileDialog
{
    public bool? Show() => Result;
}


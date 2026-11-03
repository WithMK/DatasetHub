using System.Windows;
using DatasetHub.Core;
using DatasetHub.Infrastructure;
using DatasetHub.Modules.Collector;
using DatasetHub.Modules.Example;
using Prism.DryIoc;
using Prism.Events;
using Prism.Ioc;
using Prism.Modularity;
namespace DatasetHub.Desktop;
public partial class App : PrismApplication
{
    protected override Window CreateShell() => Container.Resolve<DatasetHub.Shell.MainWindow>();
    protected override void RegisterTypes(IContainerRegistry registry)
    {
        registry.RegisterSingleton<IMenuRegistry, MenuRegistry>();
        registry.RegisterSingleton<IConfigurationService, ConfigurationService>();
        registry.RegisterSingleton<IAppLogger, FileLogger>();
        registry.RegisterSingleton<IAppDialog, AppDialog>();
        registry.RegisterSingleton<IAppNavigation, AppNavigation>();
    }
    protected override void ConfigureModuleCatalog(IModuleCatalog catalog)
    {
        catalog.AddModule<CollectorModule>();
        catalog.AddModule<ExampleModule>();
    }
    protected override void OnInitialized()
    {
        base.OnInitialized();
        DispatcherUnhandledException += (_, e) =>
        {
            Container.Resolve<IAppLogger>().Write("처리되지 않은 UI 오류", e.Exception);
            Container.Resolve<IAppDialog>().ShowError(e.Exception.Message);
            // Unknown UI state: shut down rather than silently continue.
            e.Handled = true;
            Shutdown(1);
        };
        MainWindow.Closing += (_, _) => Container.Resolve<IEventAggregator>().GetEvent<ShutdownEvent>().Publish();
        MainWindow.Dispatcher.BeginInvoke(() => Container.Resolve<IAppNavigation>().Navigate(Container.Resolve<IMenuRegistry>().Items[0].Target));
    }
}

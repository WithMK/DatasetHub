using System.Windows;
using DatasetHub.Core;
using DatasetHub.Infrastructure;
using DatasetHub.Modules.Collector;
using DatasetHub.Modules.Example;
using DatasetHub.Modules.Equipment;
using Prism.DryIoc;
using Prism.Events;
using Prism.Ioc;
using Prism.Modularity;
namespace DatasetHub.Desktop;
public partial class App : PrismApplication
{
    private bool closeApproved, checkingClose;
    protected override Window CreateShell() => Container.Resolve<DatasetHub.Shell.MainWindow>();
    protected override void RegisterTypes(IContainerRegistry registry)
    {
        registry.RegisterSingleton<IEquipmentConfigurationService, EquipmentConfigurationService>();
        registry.RegisterSingleton<IConfirmationService, ConfirmationService>();
        registry.RegisterSingleton<ICloseGuard, CloseGuard>();
        registry.RegisterSingleton<IPathReader, PathReader>();
        registry.RegisterSingleton<IPathProbeService, PathProbeService>();
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
        catalog.AddModule<EquipmentModule>();
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
        MainWindow.Closing += async (_, e) =>
        {
            if (closeApproved) { Container.Resolve<IEventAggregator>().GetEvent<ShutdownEvent>().Publish(); return; }
            e.Cancel = true;
            if (checkingClose) return;
            checkingClose = true;
            try
            {
                if (await Container.Resolve<ICloseGuard>().CanCloseAsync())
                {
                    closeApproved = true;
                    // Defer Close: the original Closing callback may have completed synchronously.
                    MainWindow.Dispatcher.BeginInvoke(MainWindow.Close);
                }
            }
            catch (Exception error) { Container.Resolve<IAppLogger>().Write("종료 확인 실패", error); }
            finally { checkingClose = false; }
        };
        MainWindow.Dispatcher.BeginInvoke(() => Container.Resolve<IAppNavigation>().Navigate(Container.Resolve<IMenuRegistry>().Items[0].Target));
    }
}

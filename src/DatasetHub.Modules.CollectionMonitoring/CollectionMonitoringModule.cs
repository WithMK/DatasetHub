using System.Windows.Controls;
using DatasetHub.Core;
using Prism.Ioc;
using Prism.Modularity;
namespace DatasetHub.Modules.CollectionMonitoring;
public sealed class CollectionMonitoringModule : IModule
{
    public void RegisterTypes(IContainerRegistry registry)
    {
        registry.RegisterSingleton<CollectionMonitoringViewModel>();
        registry.RegisterForNavigation<CollectionMonitoringView, CollectionMonitoringViewModel>("CollectionMonitoringView");
    }
    public void OnInitialized(IContainerProvider provider) => provider.Resolve<IMenuRegistry>().Register(new("수집 모니터링", "CollectionMonitoringView"));
}
public partial class CollectionMonitoringView : UserControl { public CollectionMonitoringView() => InitializeComponent(); }

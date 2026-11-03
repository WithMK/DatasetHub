using DatasetHub.Core;
using DatasetHub.Desktop;
using Prism.Ioc;
using Prism.Modularity;
namespace DatasetHub.Modules.Collector;
public sealed class CollectorModule : IModule
{
    public void RegisterTypes(IContainerRegistry registry)
    {
        registry.RegisterSingleton<ICollectorService, CollectorService>();
        registry.RegisterSingleton<CollectorViewModel>();
        registry.RegisterForNavigation<CollectorView, CollectorViewModel>("Collector");
    }
    public void OnInitialized(IContainerProvider provider) => provider.Resolve<IMenuRegistry>().Register(new("데이터 수집", "Collector"));
}

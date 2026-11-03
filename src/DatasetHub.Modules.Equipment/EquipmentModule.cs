using DatasetHub.Core;
using Prism.Ioc;
using Prism.Modularity;
namespace DatasetHub.Modules.Equipment;
public sealed class EquipmentModule : IModule
{
    public void RegisterTypes(IContainerRegistry registry)
    {
        registry.RegisterSingleton<EquipmentViewModel>();
        registry.RegisterForNavigation<EquipmentView, EquipmentViewModel>("Equipment");
    }
    public void OnInitialized(IContainerProvider provider) => provider.Resolve<IMenuRegistry>().Register(new("설비 관리", "Equipment"));
}

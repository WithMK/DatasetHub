using System.Windows.Controls;
using DatasetHub.Core;
using Prism.Commands;
using Prism.Events;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Mvvm;
namespace DatasetHub.Modules.Example;
public sealed class ExampleModule : IModule
{
    public void RegisterTypes(IContainerRegistry registry) => registry.RegisterForNavigation<ExampleView, ExampleViewModel>("FrameworkExample");
    public void OnInitialized(IContainerProvider provider) => provider.Resolve<IMenuRegistry>().Register(new("모듈 예제", "FrameworkExample"));
}
public sealed class ExampleViewModel : BindableBase
{
    public DelegateCommand PublishCommand { get; }
    public ExampleViewModel(IEventAggregator events) => PublishCommand = new(() => events.GetEvent<StatusEvent>().Publish("예제 모듈에서 공통 상태 이벤트를 전달했습니다."));
}
public partial class ExampleView : UserControl { public ExampleView() => InitializeComponent(); }

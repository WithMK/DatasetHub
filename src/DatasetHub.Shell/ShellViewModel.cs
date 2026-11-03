using DatasetHub.Core;
using Prism.Commands;
using Prism.Events;
using Prism.Mvvm;
namespace DatasetHub.Shell;
public sealed class ShellViewModel : BindableBase
{
    private string status = "준비";
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public IMenuRegistry Menu { get; }
    public DelegateCommand<string> NavigateCommand { get; }
    public ShellViewModel(IMenuRegistry menu, IAppNavigation navigation, IEventAggregator events)
    {
        Menu = menu;
        NavigateCommand = new(navigation.Navigate);
        events.GetEvent<StatusEvent>().Subscribe(text => Status = text, ThreadOption.UIThread);
    }
}

using DatasetHub.Desktop;

var current = "previous.json";
void Set(string path) => current = path;
if (ConfigFilePicker.Select(new Stub(false, "new.json"), Set) || current != "previous.json") throw new Exception("Cancel changed path.");
if (ConfigFilePicker.Select(new Stub(null, "new.json"), Set) || current != "previous.json") throw new Exception("Close changed path.");
if (ConfigFilePicker.Select(new Stub(true, ""), Set) || current != "previous.json") throw new Exception("Empty filename changed path.");
if (!ConfigFilePicker.Select(new Stub(true, "new.json"), Set) || current != "new.json") throw new Exception("Selection failed.");
Console.WriteLine("PASS file dialog cancel/close/selection");
record Stub(bool? Result, string FileName) : IConfigFileDialog
{
    public bool? Show() => Result;
}

// Exercise the actual WPF modal cancel path on an STA thread.
Exception? windowError = null;
var uiThread = new Thread(() =>
{
    try
    {
        var dialog = new ConfigPathWindow("previous.json");
        dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(dialog.CancelSelection);
        if (dialog.ShowDialog() != false) throw new Exception("WPF modal cancel did not return false.");
    }
    catch (Exception e) { windowError = e; }
});
uiThread.SetApartmentState(ApartmentState.STA);
uiThread.Start();
if (!uiThread.Join(TimeSpan.FromSeconds(15))) throw new Exception("WPF modal dialog failed to close.");
if (windowError != null) throw windowError;
Console.WriteLine("PASS WPF modal cancel closes selector only");

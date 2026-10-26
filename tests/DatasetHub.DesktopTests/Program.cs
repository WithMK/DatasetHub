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

using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using DatasetHub;
using DatasetHub.Core;
using DatasetHub.Desktop;
using DatasetHub.Infrastructure;
using DatasetHub.Modules.Equipment;
using Prism.Events;
using Prism.Ioc;
using Prism.Regions;

internal static class EquipmentTests
{
    private static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    public static async Task RunAsync(App app, IRegionManager regions)
    {
        var root = Path.Combine(Path.GetTempPath(), "DatasetHub-Equipment-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logRoot = Path.Combine(root, "source");
            var images = Path.Combine(root, "images");
            Directory.CreateDirectory(Path.Combine(logRoot, "20261103"));
            Directory.CreateDirectory(Path.Combine(images, "20261103"));
            await File.WriteAllTextAsync(Path.Combine(logRoot, "20261103", "test.log"), "original log");
            await File.WriteAllTextAsync(Path.Combine(images, "20261103", "test.png"), "opaque image");
            var path = Path.Combine(root, "collector.json");
            var raw = """
            {"destinationRoot":"archive","databasePath":"history.db","completionSuffix":"","imageStableSeconds":0,
             "equipments":[{"machineId":"M01","enabled":true,"pcs":[{"pcId":"CTRL","sourceRoot":"source","imageSourceRoot":"images"}]}]}
            """;
            await File.WriteAllTextAsync(path, raw, new UTF8Encoding(true));
            var store = new EquipmentConfigurationService();
            var doc = await store.LoadAsync(path);
            Check(doc.Json["destinationRoot"]!.GetValue<string>() == "archive", "legacy destinationRoot and UTF-8 BOM load");
            var snapshot = new ConfigurationService().Read<CollectorOptions>(path);
            snapshot.Validate(root);
            var dialog = new TestConfirmation(); var close = new CloseGuard(); var events = new EventAggregator();
            var probe = new PathProbeService(new PathReader());
            var vm = new EquipmentViewModel(store, new ConfigurationService(), dialog, probe, close, new FileLogger(), events) { ConfigPath = path };
            Check(await vm.LoadAsync(), "equipment configuration load");
            vm.AddEquipmentCommand.Execute();
            var added = vm.SelectedEquipment!; added.MachineId = "M02"; added.Enabled = false;
            vm.AddPcCommand.Execute(); vm.SelectedPc!.PcId = "INSPECT"; vm.SelectedPc.ImageSourceRoot = "images";
            Check(vm.Equipments.Count == 2 && added.Pcs.Count == 1 && vm.IsDirty, "equipment and image-only PC create/edit");
            // Arbitrary PC count, and independent PcId namespaces across machines.
            vm.AddPcCommand.Execute(); vm.SelectedPc!.PcId = "CTRL"; vm.SelectedPc.SourceRoot = "source";
            Check(await vm.SaveAsync(), "save mixed image-only log-only and dual-source PCs");
            var saved = await store.LoadAsync(path);
            Check(saved.Json["destinationRoot"]!.GetValue<string>() == "archive" && saved.Json["archiveRoot"] == null &&
                saved.Json["equipments"]![0]!["pcs"]![0]!["sourceRoot"]!.GetValue<string>() == "source" &&
                saved.Json["imageStableSeconds"]!.GetValue<int>() == 0, "save preserves legacy field and relative paths and optional settings");
            vm.SelectedEquipment = vm.Equipments[1]; vm.SelectedEquipment.MachineId = "m01";
            var before = await File.ReadAllBytesAsync(path);
            var duplicateSaved = await vm.SaveAsync();
            var afterDuplicate = await File.ReadAllBytesAsync(path);
            Check(!duplicateSaved && before.SequenceEqual(afterDuplicate), "duplicate MachineId case insensitive; invalid save leaves bytes intact");
            vm.SelectedEquipment.MachineId = "M02";
            vm.SelectedEquipment.Pcs[1].PcId = "inspect";
            Check(!await vm.SaveAsync(), "duplicate PcId within equipment rejected");
            vm.SelectedEquipment.Pcs[1].PcId = "CTRL";
            vm.SelectedEquipment.Pcs[1].SourceRoot = "bad*path";
            Check(!await vm.SaveAsync(), "invalid source path rejected");
            vm.SelectedEquipment.Pcs[1].SourceRoot = "source";
            vm.SelectedEquipment.Pcs[1].ImageSourceRoot = "";
            vm.SelectedEquipment.Pcs[1].SourceRoot = "";
            Check(!await vm.SaveAsync(), "PC requires at least one source");
            vm.SelectedEquipment.Pcs[1].SourceRoot = "source";
            vm.SelectedEquipment.MachineId = "RENAMED"; dialog.Accept = false;
            Check(!await vm.SaveAsync() && dialog.LastMessage.Contains("MachineId"), "MachineId rename requires warning confirmation");
            dialog.Accept = true; Check(await vm.SaveAsync(), "confirmed MachineId rename saves");
            Check(snapshot.Equipments.Count == 1 && snapshot.Equipments[0].MachineId == "M01", "running collector snapshot unaffected by save");
            var next = new ConfigurationService().Read<CollectorOptions>(path); next.Validate(root);
            Check(next.Equipments.Count == 2 && next.Equipments[1].MachineId == "RENAMED", "next collector execution reads updated configuration");
            var first = await new Collector(next).RunAllAsync();
            var second = await new Collector(next).RunAllAsync();
            Check(first.Collected == 1 && second.ImagesCopied == 1, "saved configuration collects text and images with existing engine");
            vm.SelectedEquipment = vm.Equipments[1]; vm.SelectedPc = vm.SelectedEquipment.Pcs[1];
            vm.DeletePcCommand.Execute(); Check(vm.SelectedEquipment.Pcs.Count == 1, "PC delete");
            vm.DeleteEquipmentCommand.Execute(); Check(vm.Equipments.Count == 1, "equipment delete");
            Check(await vm.SaveAsync() && File.Exists(Path.Combine(root, "archive", "M01", "20261103", "test.log")) && File.Exists(Path.Combine(root, "history.db")), "settings delete never deletes archive or database");
            vm.SelectedEquipment!.Enabled = false;
            dialog.Decision = UnsavedDecision.Cancel;
            Check(!await close.CanCloseAsync() && vm.IsDirty, "unsaved exit cancelled");
            dialog.Decision = UnsavedDecision.Discard;
            Check(await close.CanCloseAsync() && !vm.IsDirty && vm.SelectedEquipment!.Enabled, "discard restores saved state");
            vm.SelectedEquipment!.Enabled = false; dialog.Decision = UnsavedDecision.Save;
            Check(await close.CanCloseAsync() && !vm.IsDirty, "unsaved exit save");
            var stale = await store.LoadAsync(path);
            await File.AppendAllTextAsync(path, " ");
            try { await store.SaveAsync(stale); throw new Exception("external edit overwritten"); }
            catch (IOException) { Check(true, "external config modification detected"); }
            Check((await File.ReadAllTextAsync(path)).EndsWith(" "), "conflict preserves external file");
            Check(!Directory.GetFiles(root, "*.tmp").Any(), "temporary config files cleaned");
            // Windows read locks make atomic replace fail; the previous document stays intact.
            var locked = await store.LoadAsync(path);
            using (var lockFile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { await store.SaveAsync(locked); throw new Exception("locked replacement succeeded"); }
                catch (IOException) { Check(true, "locked destination save fails safely"); }
            }
            Check(!Directory.GetFiles(root, "*.tmp").Any(), "failed replace cleans temporary file");
            Check((await probe.CheckAsync("source", path)).Success, "local folder read probe");
            Check(!(await probe.CheckAsync("missing", path)).Success, "missing folder probe isolated failure");
            var denied = new PathProbeService(new DeniedReader());
            Check(!(await denied.CheckAsync("source", path)).Success, "read permission failure reported");
            var share = Environment.GetEnvironmentVariable("DATASETHUB_TEST_SHARE");
            if (!string.IsNullOrWhiteSpace(share))
            {
                Check((await probe.CheckAsync(share, path)).Success, "real UNC share read success");
                Check(!(await probe.CheckAsync(Path.Combine(share, "missing"), path)).Success, "real UNC share missing path failure");
            }
            else Console.WriteLine("SKIP real UNC share (DATASETHUB_TEST_SHARE not set)");
            var slow = new ControlledProbe();
            var asyncVm = new EquipmentViewModel(store, new ConfigurationService(), dialog, slow, new CloseGuard(), new FileLogger(), events) { ConfigPath = path };
            await asyncVm.LoadAsync();
            var checking = asyncVm.CheckConnectionsAsync();
            Check(asyncVm.IsBusy && !checking.IsCompleted, "path checking asynchronous; dispatcher remains available");
            slow.Complete(); await checking;
            Check(!asyncVm.IsBusy && asyncVm.Equipments[0].Pcs[0].Connection.Contains("연결 실패"), "per-path failure does not abort other path checks");
            // Real RegionManager + a dirty VM: cancel and discard through Prism's callback.
            var editor = app.Container.Resolve<EquipmentViewModel>(); editor.ConfigPath = path;
            Check(await editor.LoadAsync(), "registered Equipment VM loads configuration");
            bool? navigated = null;
            regions.RequestNavigate(RegionNames.Main, "Equipment", result => navigated = result.Result);
            Check(navigated == true, "Equipment view navigation");
            editor.SelectedEquipment!.Enabled = !editor.SelectedEquipment.Enabled;
            ((TestConfirmation)app.Container.Resolve<IConfirmationService>()).Decision = UnsavedDecision.Cancel;
            navigated = null;
            regions.RequestNavigate(RegionNames.Main, "Collector", result => navigated = result.Result);
            Check(navigated == false && editor.IsDirty, "real RegionManager honours unsaved cancel");
            app.MainWindow.Close();
            Check(app.MainWindow.IsVisible && editor.IsDirty, "real Window Closing honours unsaved cancel");
            ((TestConfirmation)app.Container.Resolve<IConfirmationService>()).Decision = UnsavedDecision.Discard;
            navigated = null;
            regions.RequestNavigate(RegionNames.Main, "Collector", result => navigated = result.Result);
            Check(navigated == true && !editor.IsDirty, "real RegionManager discard permits navigation");
            // Test VM with injected dialogs exercises IConfirmNavigationRequest without interactive modal dialogs.
            asyncVm.SelectedEquipment!.Enabled = !asyncVm.SelectedEquipment.Enabled;
            dialog.Decision = UnsavedDecision.Cancel;
            var completion = new TaskCompletionSource<bool>();
            asyncVm.ConfirmNavigationRequest(null!, value => completion.SetResult(value));
            Check(!await completion.Task, "Prism unsaved navigation callback cancels");
            dialog.Decision = UnsavedDecision.Discard;
            completion = new TaskCompletionSource<bool>();
            asyncVm.ConfirmNavigationRequest(null!, value => completion.SetResult(value));
            Check(await completion.Task && !asyncVm.IsDirty, "Prism unsaved navigation callback discards");
            regions.RequestNavigate(RegionNames.Main, "Collector");
            Console.WriteLine("All Equipment checks passed.");
        }
        finally { Directory.Delete(root, true); }
    }
    internal sealed class TestConfirmation : IConfirmationService
    {
        public bool Accept { get; set; } = true;
        public string LastMessage { get; private set; } = "";
        public UnsavedDecision Decision { get; set; } = UnsavedDecision.Discard;
        public bool Confirm(string message) { LastMessage = message; return Accept; }
        public UnsavedDecision UnsavedChanges() => Decision;
    }
    private sealed class DeniedReader : IPathReader { public PathAccessResult Read(string fullPath) => throw new UnauthorizedAccessException("test denied"); }
    private sealed class ControlledProbe : IPathProbeService
    {
        private readonly TaskCompletionSource gate = new();
        public void Complete() => gate.SetResult();
        public async Task<PathAccessResult> CheckAsync(string path, string configurationPath)
        {
            await gate.Task;
            if (path == "source") throw new IOException("isolated test failure");
            return new(path, true, "연결 성공");
        }
    }
}

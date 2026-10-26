using DatasetHub;
using Microsoft.Data.Sqlite;

var root = Path.Combine(Path.GetTempPath(), "DatasetHub-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var source = Path.Combine(root, "source");
    var hour = Path.Combine(source, "20261026", "10-hour");
    Directory.CreateDirectory(hour);
    var original = Path.Combine(hour, "sample.log");
    await File.WriteAllBytesAsync(original, [0, 1, 2, 255, 10, 13]);
    await File.WriteAllTextAsync(original + ".done", "");
    var options = new CollectorOptions
    {
        ArchiveRoot = Path.Combine(root, "archive"), DatabasePath = Path.Combine(root, "history.db"),
        Equipments = [new() { MachineId = "TEST", Enabled = true, Pcs = [new() { PcId = "CTRL", SourceRoot = source }] }]
    };
    options.Validate(root);
    var date = new DateOnly(2026, 10, 26);
    Task<CollectionResult> Run() => new Collector(options).RunAsync(date, date);
    void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    var first = await Run();
    Check(first.Collected == 1 && first.Failed == 0, "first collection");
    var destination = Path.Combine(options.ArchiveRoot, "TEST", "CTRL", "20261026", "10-hour", "sample.log");
    Check(File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(destination)), "binary equality");
    Check((await Run()).Skipped == 1, "repeat does not duplicate");
    // Simulate final rename committed but history missing.
    using (var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
    {
        db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM Files"; cmd.ExecuteNonQuery();
    }
    Check((await Run()).Skipped == 1, "reconstruct missing history from final file");
    File.Delete(destination);
    await File.WriteAllTextAsync(destination + ".partial", "interrupted copy");
    Check((await Run()).Collected == 1, "recover stale partial and missing archive");
    await File.WriteAllTextAsync(Path.Combine(hour, "pending.log"), "still writing");
    Check((await Run()).Deferred == 1, "missing marker deferred");
    await File.WriteAllTextAsync(Path.Combine(hour, "pending.log.done"), "");
    Check((await Run()).Collected == 1, "late completion discovered");
    await File.WriteAllTextAsync(original, "changed source");
    var archivedBytes = File.ReadAllBytes(destination);
    Check((await Run()).Failed == 1 && archivedBytes.SequenceEqual(File.ReadAllBytes(destination)), "conflict preserves archive");
    options.Equipments.Insert(0, new() { MachineId = "OFFLINE", Enabled = true, Pcs = [new() { PcId = "CTRL", SourceRoot = Path.Combine(root, "missing") }] });
    var offline = await Run();
    Check(offline.Failed == 2 && offline.Skipped == 1, "offline PC does not block next equipment");
    using (var held = new FileStream(Path.Combine(options.ArchiveRoot, ".collector.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    {
        var blocked = false;
        try { await Run(); } catch (IOException) { blocked = true; }
        Check(blocked, "overlapping run blocked");
    }
    using var cts = new CancellationTokenSource(); cts.Cancel();
    var cancelled = false;
    try { await new Collector(options).RunAsync(date, date, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "cancellation propagated");
    Console.WriteLine("All smoke checks passed.");
    return 0;
}
finally { Directory.Delete(root, recursive: true); }

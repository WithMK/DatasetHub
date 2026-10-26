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
    var fields = new[] { "2026-10-26 10:00:00.123", "TEST", "Operation", "LOT", "RECIPE", "PRODUCT", "Run", "Scope", "Operation", "Event", "한글 Data" };
    await File.WriteAllTextAsync(original, "SEM_LOG_Version=2.0\n" + string.Join('\t', fields) + "\n");
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
    Check(first.Collected == 1 && first.Parsed == 1 && first.Failed == 0, "first collection and parsing");
    var destination = Path.Combine(options.ArchiveRoot, "TEST", "CTRL", "20261026", "10-hour", "sample.log");
    Check(File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(destination)), "binary equality");
    Check((await Run()).Skipped == 1, "repeat does not duplicate");
    Check(Count("LogRows") == 1, "parsed row unique");
    // Simulate final rename committed but history missing.
    using (var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
    {
        db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM Files"; cmd.ExecuteNonQuery();
    }
    Check((await Run()).Skipped == 1, "reconstruct missing history from final file");
    File.Delete(destination);
    await File.WriteAllTextAsync(destination + ".partial", "interrupted copy");
    Check((await Run()).Collected == 1, "recover stale partial and missing archive");
    await File.WriteAllTextAsync(Path.Combine(hour, "pending.log"), string.Join('\t', fields));
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
    var mixed = Path.Combine(hour, "mixed.log");
    await File.WriteAllTextAsync(mixed, string.Join('\t', fields) + "\nmalformed\n" + string.Join('\t', fields.Take(10).Append("Data\tunexpected")) + "\n");
    await File.WriteAllTextAsync(mixed + ".done", "");
    var withErrors = await Run();
    Check(withErrors.InvalidRows == 2 && Count("LogRows", "AND RelativePath LIKE '%mixed.log'") == 3, "malformed and extra-tab rows recorded");
    Check((await Run()).InvalidRows == 2, "repeat retains parse errors");
    using (var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
    {
        db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM ParseState WHERE RelativePath LIKE '%mixed.log'"; cmd.ExecuteNonQuery();
    }
    Check((await Run()).InvalidRows == 2 && Count("LogRows", "AND RelativePath LIKE '%mixed.log'") == 3, "interrupted parsing replays without duplicates");
    int Count(string table, string condition = "")
    {
        using var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"); db.Open();
        using var cmd = db.CreateCommand(); cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE 1=1 {condition}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    using var cts = new CancellationTokenSource(); cts.Cancel();
    var cancelled = false;
    try { await new Collector(options).RunAsync(date, date, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "cancellation propagated");
    Console.WriteLine("All smoke checks passed.");
    return 0;
}
finally { Directory.Delete(root, recursive: true); }

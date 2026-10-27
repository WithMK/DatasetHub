using DatasetHub;
using Microsoft.Data.Sqlite;

var root = Path.Combine(Path.GetTempPath(), "DatasetHub-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    var source = Path.Combine(root, "source");
    var hour = Path.Combine(source, "20261026");
    Directory.CreateDirectory(hour);
    var original = Path.Combine(hour, "2026102610_TEST.log");
    var fields = new[] { "2026-10-26 10:00:00.123", "TEST", "Operation", "LOT", "RECIPE", "PRODUCT", "Run", "Scope", "Operation", "Event", "한글 Data" };
    var firstLine = string.Join('\t', fields);
    await File.WriteAllTextAsync(original, "SEM_LOG_Version=2.0\n" + firstLine + "\n");
    var options = new CollectorOptions
    {
        ArchiveRoot = Path.Combine(root, "archive"), DatabasePath = Path.Combine(root, "history.db"),
        Equipments = [new() { MachineId = "TEST", Enabled = true, Pcs = [new() { PcId = "CTRL", SourceRoot = source }] }]
    };
    options.Validate(root);
    var configured = System.Text.Json.JsonSerializer.Deserialize<CollectorOptions>("""{"destinationRoot":"../copies","databasePath":"../history.db","equipments":[]}""")!;
    configured.Validate(Path.Combine(root, "config"));
    if (configured.ArchiveRoot != Path.Combine(root, "copies")) throw new Exception("configured destinationRoot");
    var day = new DateOnly(2026, 10, 26);
    Task<CollectionResult> Run() => new Collector(options).RunAsync(day, day);
    void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    string SnapshotDir(string file) => Path.Combine(options.ArchiveRoot, "TEST", "CTRL", ".snapshots", "20261026", file);
    string[] Snapshots(string file) => Directory.GetFiles(SnapshotDir(file), "*.log");
    int Count(string table, string condition = "")
    {
        using var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"); db.Open();
        using var cmd = db.CreateCommand(); cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE 1=1 {condition}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    var first = await Run();
    Check(first.Collected == 1 && first.Parsed == 1 && first.Failed == 0 &&
          first.SourcesChecked == 1 && first.DateFoldersFound == 1 && first.FilesFound == 1,
        "matching date folder reports discovered log and snapshot");
    var empty = await new Collector(options).RunAsync(day.AddDays(-1), day.AddDays(-1));
    Check(empty.SourcesChecked == 1 && empty.DateFoldersFound == 0 && empty.FilesFound == 0, "no matching date is reported");
    Check(Snapshots("2026102610_TEST.log").Length == 1 && File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(Snapshots("2026102610_TEST.log")[0])), "snapshot bytes equal source");
    Check((await Run()).Skipped == 1 && Count("LogRows") == 1, "unchanged source skipped without duplicate rows");
    using (var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
    { db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM Files"; cmd.ExecuteNonQuery(); }
    Check((await Run()).Skipped == 1, "missing collection history recovered");
    var prior = Snapshots("2026102610_TEST.log")[0];
    await File.AppendAllTextAsync(original, firstLine + "\n");
    var appended = await Run();
    Check(appended.Collected == 1 && appended.Parsed == 2 && Snapshots("2026102610_TEST.log").Length == 2, "growing file creates a new snapshot");
    Check(File.ReadAllText(prior).EndsWith(firstLine + "\n") && Count("LogRows") == 3, "old version and rows retained");
    await File.WriteAllTextAsync(original, "SEM_LOG_Version=2.0\n" + firstLine + "\nchanged invalid line\n");
    var rewritten = await Run();
    Check(rewritten.Collected == 1 && rewritten.InvalidRows == 1 && Snapshots("2026102610_TEST.log").Length == 3, "same-path rewrite creates independent version");
    var oldSnapshot = prior;
    File.Delete(oldSnapshot);
    await File.WriteAllTextAsync(Path.Combine(SnapshotDir("2026102610_TEST.log"), "copy.partial"), "interrupted");
    await Run();
    Check(Snapshots("2026102610_TEST.log").Length == 2, "stale partial cleaned without replacing other versions");
    var live = Path.Combine(hour, "2026102611_TEST.log");
    await File.WriteAllTextAsync(live, firstLine + "\n");
    options.CompletionSuffix = ".done";
    Check((await Run()).Deferred >= 1 && !Directory.Exists(SnapshotDir("2026102611_TEST.log")), "optional marker mode defers unfinished file");
    await File.WriteAllTextAsync(live + ".done", "");
    Check((await Run()).Collected == 1 && Snapshots("2026102611_TEST.log").Length == 1, "optional marker mode collects after signal");
    options.CompletionSuffix = "";
    var bad = Path.Combine(hour, "2026102612_TEST.log");
    await File.WriteAllTextAsync(bad, firstLine + "\nmalformed\n" + firstLine + "\textra\n");
    Check((await Run()).InvalidRows >= 2 && Count("LogRows", "AND RelativePath LIKE '%2026102612_TEST.log@%'") == 3, "bad rows recorded by snapshot key");
    Check((await Run()).InvalidRows >= 2 && Count("LogRows", "AND RelativePath LIKE '%2026102612_TEST.log@%'") == 3, "repeat does not duplicate parse rows");
    options.Equipments.Insert(0, new() { MachineId = "OFFLINE", Enabled = true, Pcs = [new() { PcId = "CTRL", SourceRoot = Path.Combine(root, "missing") }] });
    Check((await Run()).Failed >= 1, "offline machine does not block other equipment");
    using (var held = new FileStream(Path.Combine(options.ArchiveRoot, ".collector.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
    { var blocked = false; try { await Run(); } catch (IOException) { blocked = true; } Check(blocked, "overlapping scan blocked"); }
    using var cts = new CancellationTokenSource(); cts.Cancel();
    var cancelled = false; try { await new Collector(options).RunAsync(day, day, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "cancellation propagated");
    Console.WriteLine("All smoke checks passed.");
    return 0;
}
finally { Directory.Delete(root, recursive: true); }

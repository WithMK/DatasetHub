using DatasetHub;

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
    var configured = System.Text.Json.JsonSerializer.Deserialize<CollectorOptions>(
        """{"destinationRoot":"../copies","databasePath":"../history.db","equipments":[]}""",
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    configured.Validate(Path.Combine(root, "config"));
    if (configured.ArchiveRoot != Path.Combine(root, "copies")) throw new Exception("configured destinationRoot");
    var duplicate = new CollectorOptions
    {
        DestinationRoot = Path.Combine(root, "copies"), DatabasePath = Path.Combine(root, "other.db"),
        Equipments = [new() { MachineId = "TRIM001" }, new() { MachineId = "trim001" }]
    };
    var duplicateRejected = false;
    try { duplicate.Validate(root); } catch (ArgumentException e) when (e.Message.Contains("machineId 중복")) { duplicateRejected = true; }
    if (!duplicateRejected) throw new Exception("duplicate machineId rejected while loading configuration");
    var day = new DateOnly(2026, 10, 26);
    Task<CollectionResult> Run() => new Collector(options).RunAsync(day, day);
    void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    string Mirror(string file) => Path.Combine(options.ArchiveRoot, "TEST", "20261026", file);
    var first = await Run();
    Check(first.Collected == 1 && first.Failed == 0 &&
          first.SourcesChecked == 1 && first.DateFoldersFound == 1 && first.FilesFound == 1,
        "matching date folder reports discovered log and copy");
    var empty = await new Collector(options).RunAsync(day.AddDays(-1), day.AddDays(-1));
    Check(empty.SourcesChecked == 1 && empty.DateFoldersFound == 0 && empty.FilesFound == 0, "no matching date is reported");
    Check(File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(Mirror("2026102610_TEST.log"))), "destination retains source directory and filename");
    Check(!File.Exists(Path.Combine(options.ArchiveRoot, "20261026", "2026102610_TEST.log")), "destination is inside machine folder");
    Check((await Run()).Skipped == 1, "unchanged source skipped without duplicate rows");
    File.Delete(Mirror("2026102610_TEST.log"));
    Check((await Run()).Collected == 1 && File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(Mirror("2026102610_TEST.log"))), "missing destination copied again");
    await File.AppendAllTextAsync(original, firstLine + "\n");
    var appended = await Run();
    Check(appended.Collected == 1 && appended.AppendedBytes == System.Text.Encoding.UTF8.GetByteCount(firstLine + "\n"), "growing file copies only appended bytes");
    Check(File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(Mirror("2026102610_TEST.log"))), "destination updates to latest source bytes");
    await File.WriteAllTextAsync(original, "SEM_LOG_Version=2.0\n" + firstLine + "\nchanged invalid line\n");
    File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddSeconds(2));
    var rewritten = await Run();
    Check(rewritten.Collected == 1 && File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(Mirror("2026102610_TEST.log"))), "same-path rewrite replaces prior content");
    var rewrittenBytes = await File.ReadAllBytesAsync(original);
    rewrittenBytes[0] = (byte)'X';
    await File.WriteAllBytesAsync(original, [.. rewrittenBytes, (byte)'!']);
    File.SetLastWriteTimeUtc(original, DateTime.UtcNow.AddSeconds(4));
    var prefixChanged = await Run();
    Check(prefixChanged.Collected == 1 && prefixChanged.AppendedBytes == 0 &&
          File.ReadAllBytes(original).SequenceEqual(File.ReadAllBytes(Mirror("2026102610_TEST.log"))), "changed prefix with growth triggers full replacement");
    Check(!Directory.Exists(Path.Combine(options.ArchiveRoot, "TEST", "CTRL", ".snapshots")), "version folder is disabled");
    await File.WriteAllTextAsync(Mirror("2026102610_TEST.log") + ".partial", "interrupted");
    Check((await Run()).Skipped == 1 && !File.Exists(Mirror("2026102610_TEST.log") + ".partial"), "stale partial cleaned");
    var live = Path.Combine(hour, "2026102611_TEST.log");
    await File.WriteAllTextAsync(live, firstLine + "\n");
    options.CompletionSuffix = ".done";
    Check((await Run()).Deferred >= 1 && !File.Exists(Mirror("2026102611_TEST.log")), "optional marker mode defers unfinished file");
    await File.WriteAllTextAsync(live + ".done", "");
    Check((await Run()).Collected == 1 && File.Exists(Mirror("2026102611_TEST.log")), "optional marker mode collects after signal");
    options.CompletionSuffix = "";
    var bad = Path.Combine(hour, "2026102612_TEST.log");
    await File.WriteAllTextAsync(bad, firstLine + "\nmalformed\n" + firstLine + "\textra\n");
    Check((await Run()).Collected == 1 && File.ReadAllBytes(bad).SequenceEqual(File.ReadAllBytes(Mirror("2026102612_TEST.log"))), "unstructured log copied unchanged");
    Check((await Run()).Skipped >= 1, "unstructured log accepted on repeat");
    var older = Path.Combine(source, "20261024");
    Directory.CreateDirectory(older);
    await File.WriteAllTextAsync(Path.Combine(older, "2026102410_TEST.log"), "older log");
    var fullScan = await new Collector(options).RunAllAsync();
    Check(fullScan.DateFoldersFound == 2 && fullScan.FilesFound >= 4 &&
          File.Exists(Path.Combine(options.ArchiveRoot, "TEST", "20261024", "2026102410_TEST.log")), "date-free scan discovers older folders");
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

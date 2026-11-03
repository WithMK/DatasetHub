using System.IO;
using System.Text.Json;
using DatasetHub;
using DatasetHub.Core;
using DatasetHub.Infrastructure;
using Microsoft.Data.Sqlite;
internal static class MonitoringTests
{
    private static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Console.WriteLine("PASS " + text); }
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "DatasetHub-Monitor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "logs"); var images = Path.Combine(root, "images"); var empty = Path.Combine(root, "empty");
            Directory.CreateDirectory(Path.Combine(source, "20261103")); Directory.CreateDirectory(Path.Combine(images, "20261103")); Directory.CreateDirectory(empty);
            var log = Path.Combine(source, "20261103", "test.log"); var image = Path.Combine(images, "20261103", "test.png");
            await File.WriteAllTextAsync(log, "hello"); await File.WriteAllBytesAsync(image, [1,2,3]);
            var options = new CollectorOptions { ArchiveRoot = Path.Combine(root, "archive"), DatabasePath = Path.Combine(root, "history.db"), ImageStableSeconds = 0,
                Equipments = [new Equipment { MachineId = "M1", Enabled = true, Pcs = [new SourcePc { PcId = "DUAL", SourceRoot = source, ImageSourceRoot = images }, new SourcePc { PcId = "EMPTY", SourceRoot = empty }, new SourcePc { PcId = "OFFLINE", SourceRoot = Path.Combine(root,"offline") }] },
                  new Equipment { MachineId = "M2", Enabled = false, Pcs = [new SourcePc { PcId = "DISABLED", SourceRoot = empty }] }] };
            var path = Path.Combine(root, "config.json");
            async Task Save() => await File.WriteAllTextAsync(path, JsonSerializer.Serialize(options));
            await Save(); var schedules = new CollectionScheduleState(); var service = new CollectionMonitoringService(new ConfigurationService(), schedules);
            var before = await service.ReadAsync(path, TimeSpan.Zero);
            Check(before.EquipmentCount == 2 && before.PcCount == 4 && before.Sources.Count(x => x.State == "이력 없음") == 3 && before.Sources[^1].State == "비활성", "monitor configured sources and no-history/disabled states without database");
            // Previous schema: no SourceModifiedUtc, no execution tables, one retained error.
            using (var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
            {
                db.Open(); using var cmd = db.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE Files(MachineId TEXT NOT NULL,PcId TEXT NOT NULL,RelativePath TEXT NOT NULL,Status TEXT NOT NULL,Size INTEGER,Sha256 TEXT,Error TEXT,Attempts INTEGER NOT NULL DEFAULT 1,UpdatedUtc TEXT NOT NULL,PRIMARY KEY(MachineId,PcId,RelativePath));
                    INSERT INTO Files VALUES('LEGACY','PC','old.log','Collected',10,NULL,NULL,1,'2026-01-01T00:00:00.0000000Z');
                    CREATE TABLE ScanErrors(Id INTEGER PRIMARY KEY,MachineId TEXT NOT NULL,PcId TEXT NOT NULL,Path TEXT NOT NULL,Error TEXT NOT NULL,UpdatedUtc TEXT NOT NULL);
                    INSERT INTO ScanErrors VALUES(1,'M1','OFFLINE','old','legacy error','2026-01-01T00:00:00.0000000Z');
                    """; cmd.ExecuteNonQuery();
            }
            Check((await service.ReadAsync(path, TimeSpan.Zero)).Sources[0].State == "이력 없음", "old database readable without fabricated run dates");
            Check((await service.ErrorsAsync(path,"M1","OFFLINE")).Single().Operation == "기존 이력", "legacy scan errors visible before migration");
            await new Collector(options).RunAllAsync();
            var first = await service.ReadAsync(path, TimeSpan.Zero); var dual = first.Sources[0];
            Check(dual.State == "정상" && dual.LastAttemptUtc != null && dual.LastSuccessUtc != null && dual.Log.Copied == 1 && dual.Log.Bytes == 5 && dual.Image.Deferred == 1, "real attempt timestamps and separate log/image metrics");
            Check(first.Sources[1].State == "정상" && first.Sources[1].Log.Found == 0 && first.Sources[2].State == "실패", "zero-new-file success distinct from source failure");
            Check((await service.ErrorsAsync(path,"M1","OFFLINE")).Count == 2, "migration retains legacy errors and appends run-linked failure");
            await new Collector(options).RunAllAsync();
            var second = await service.ReadAsync(path, TimeSpan.Zero);
            Check(second.Sources[0].Image.Copied == 1 && second.Sources[0].Image.Bytes == 3 && second.Sources[0].Log.Unchanged == 1 && second.Sources[0].Log.Bytes == 0, "image transfer and unchanged log statistics");
            Directory.CreateDirectory(Path.Combine(root,"offline"));
            await new Collector(options).RunAllAsync();
            var recovered = await service.ReadAsync(path, TimeSpan.Zero);
            Check(recovered.Sources[2].State == "정상" && recovered.Sources[2].LastError.Length > 0, "failure recovery retains historical error but restores normal status");
            schedules.Start(options.DatabasePath); schedules.Waiting(options.DatabasePath, DateTime.UtcNow.AddMinutes(-10));
            // A due time later than the successful scan represents a missed subsequent cycle.
            using (var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
            {
                db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE SourceCollectionRuns SET CompletedUtc='2020-01-01T00:00:00.0000000Z' WHERE Outcome='Success'"; cmd.ExecuteNonQuery();
                cmd.CommandText = "SELECT COUNT(*) FROM Files WHERE MachineId='LEGACY'"; Check(Convert.ToInt32(cmd.ExecuteScalar()) == 1, "automatic schema migration preserves existing file history");
            }
            Check((await service.ReadAsync(path, TimeSpan.Zero)).Sources[0].State == "지연", "delay only while active Desktop schedule misses completion deadline");
            schedules.Stop(options.DatabasePath);
            Check((await service.ReadAsync(path, TimeSpan.Zero)).Sources[0].State == "정상", "stopped or restarted Desktop does not imply communication outage");
            options.Equipments[0].Pcs.Add(new SourcePc { PcId = "NEW", SourceRoot = empty }); options.Equipments[1].Enabled = true; await Save();
            var changed = await service.ReadAsync(path, TimeSpan.Zero);
            Check(changed.PcCount == 5 && changed.Sources.Single(x=>x.PcId=="NEW").State == "이력 없음" && changed.Sources.Single(x=>x.PcId=="DISABLED").State == "이력 없음", "equipment settings changes reflected by monitoring");
            var lastFull = changed.LastFullRunUtc;
            await service.RetryAsync(path,"M2","DISABLED",new DateOnly(2026,11,3),new DateOnly(2026,11,3),default);
            Check((await service.ReadAsync(path,TimeSpan.Zero)).Sources.Single(x=>x.PcId=="DISABLED").State == "정상" && (await service.ReadAsync(path,TimeSpan.Zero)).LastFullRunUtc == lastFull, "selective retry reuses engine without altering full-run timestamp");
            using (var locked = new FileStream(options.DatabasePath+".collector.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                try { await service.RetryAsync(path,"M2",null,new DateOnly(2026,11,3),new DateOnly(2026,11,3),default); throw new Exception("concurrent collection accepted"); }
                catch (IOException) { Check(true,"manual retry respects existing collector lock"); }
            }
            // Observe the database while a real source is running, before completing it.
            var observedRunning = false;
            await new Collector(options, p =>
            {
                if (observedRunning) return;
                observedRunning = service.ReadAsync(path,TimeSpan.Zero).GetAwaiter().GetResult().Sources.Any(x=>x.State=="수집 중");
            }).RunAllAsync();
            Check(observedRunning,"active collector run visible through SQLite without archive traversal");
            using var cancel = new CancellationTokenSource();
            try { await new Collector(options, _=>cancel.Cancel()).RunAllAsync(cancel.Token); }
            catch (OperationCanceledException) { }
            Check((await service.RunsAsync(path,"M1","DUAL"))[0].Outcome == "Cancelled", "cancelled run recorded separately from communication failure");
            using (var db = new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
            {
                db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE SourceCollectionRuns SET Outcome='Running' WHERE Outcome='Cancelled'"; cmd.ExecuteNonQuery();
            }
            Check((await service.ReadAsync(path,TimeSpan.Zero)).Sources[0].State == "중단", "stale running record without writer lock shown interrupted");
            await new Collector(options).RunAllAsync();
            Check((await service.RunsAsync(path,"M1","DUAL")).Any(x=>x.Outcome=="Interrupted"),"next writer reconciles interrupted run without deleting history");
            Console.WriteLine("All monitoring checks passed.");
        }
        finally { Directory.Delete(root,true); }
    }
}

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DatasetHub;
using Microsoft.Data.Sqlite;
internal static class ReliabilityTests
{
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);}
    public static async Task ChildAsync(string config,string signal)
    {
        var options=JsonSerializer.Deserialize<CollectorOptions>(await File.ReadAllTextAsync(config))!;
        await new ReliableCollector(options,p=> { if(p.Message.StartsWith("원본 경로 확인")){File.WriteAllText(signal,"running");Thread.Sleep(Timeout.Infinite);} }).RunAsync(DateOnly.MinValue,DateOnly.MaxValue);
    }
    public static async Task RunAsync()
    {
        var root=Path.Combine(Path.GetTempPath(),"DatasetHub-Reliability-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var source=Path.Combine(root,"source");Directory.CreateDirectory(source);
            var options=new CollectorOptions { ArchiveRoot=Path.Combine(root,"archive"),DatabasePath=Path.Combine(root,"history.db"),
              Reliability=new ReliabilityOptions { MaxRetries=1,RetryBaseSeconds=1,RetryMaxSeconds=1,NoProgressTimeoutSeconds=2,JobTimeoutSeconds=20 },
              Equipments=[new Equipment { MachineId="M",Enabled=true,Pcs=[new SourcePc { PcId="P",SourceRoot=source }] }] };
            var day=new DateOnly(2026,11,3);
            var initial=await new ReliableCollector(options).RunAsync(day,day);
            Check(initial.Failed==0 && initial.FilesFound==0,"reliable empty-source cycle succeeds");
            var journal=new ReliabilityJournal(options.DatabasePath);
            Directory.Delete(source);
            var recovered=await new ReliableCollector(options,p=>
            {
                if(p.Message.StartsWith("폴더 접근 실패"))
                {
                    var old=Path.Combine(source,"20200101");Directory.CreateDirectory(old);File.WriteAllText(Path.Combine(old,"old.log"),"recovered old log");
                }
            }).RunAsync(day,day);
            Check(recovered.Failed==0 && File.Exists(Path.Combine(options.ArchiveRoot,"M","20200101","old.log")),"network recovery retries past-date data rather than only recent timestamps");
            Check(journal.Get("NeedsRecovery")=="false","recovery verified and persistent recovery marker cleared");
            using(var db=new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
            {
                db.Open();using var c=db.CreateCommand();c.CommandText="SELECT COUNT(*) FROM ReliabilityIncidents WHERE Kind='Collection' AND RecoveredUtc IS NOT NULL";
                Check(Convert.ToInt32(c.ExecuteScalar())==1,"incident recovery timestamp persisted");
            }
            var repeat=await new ReliableCollector(options).RunAsync(DateOnly.MinValue,DateOnly.MaxValue);
            Check(repeat.Collected==0 && repeat.Skipped==1,"reliable repeated scan does not duplicate files");
            // Known failed original removed after an earlier failed copy.
            using(var db=new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
            {
                db.Open();using var c=db.CreateCommand();c.CommandText="INSERT INTO Files(MachineId,PcId,RelativePath,Status,Error,UpdatedUtc) VALUES('M','P','20200101/gone.log','Failed','copy failed','2020-01-01T00:00:00Z')";c.ExecuteNonQuery();
            }
            await new ReliableCollector(options).RunAsync(day,day);
            Check(journal.Get("Recovery")!.Contains("복구 불가"),"deleted unfinished source reports unrecoverable reason");
            await new ReliableCollector(options).RunAsync(day,day);
            using(var db=new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
            { db.Open();using var c=db.CreateCommand();c.CommandText="SELECT COUNT(*) FROM ReliabilityIncidents WHERE Kind LIKE 'Missing:%' AND RecoveredUtc IS NULL";Check(Convert.ToInt32(c.ExecuteScalar())==1,"repeated missing-source fault deduplicated"); }
            File.WriteAllText(Path.Combine(source,"20200101","gone.log"),"restored");
            await new ReliableCollector(options).RunAsync(day,day);
            Check(journal.MissingFiles().Count==0,"restored missing source resolved only after collection");
            // Timeout during a controlled non-cancellable operation. The retry cannot overlap it.
            options.Reliability.NoProgressTimeoutSeconds=1;var blocked=0;var attempts=0;var overlap=false;
            await new ReliableCollector(options,p=>
            {
                if(!p.Message.StartsWith("원본 경로 확인"))return;
                attempts++;
                if(Interlocked.CompareExchange(ref blocked,1,0)!=0)overlap=true;
                if(attempts==1)Thread.Sleep(2200);
                Interlocked.Exchange(ref blocked,0);
            }).RunAsync(DateOnly.MinValue,DateOnly.MaxValue);
            Check(attempts==2 && !overlap,"hang cancellation waits for actual completion before retry");
            options.Reliability.NoProgressTimeoutSeconds=2;
            options.Equipments[0].Pcs.Add(new SourcePc { PcId="OFF1",SourceRoot=Path.Combine(root,"offline1") });
            options.Equipments[0].Pcs.Add(new SourcePc { PcId="OFF2",SourceRoot=Path.Combine(root,"offline2") });
            var watch=Stopwatch.StartNew();var failed=await new ReliableCollector(options).RunAsync(day,day);
            Check(failed.Failed==2 && watch.Elapsed.TotalSeconds<15,"multiple PC failures isolated and retry count bounded");
            using(var db=new SqliteConnection($"Data Source={options.DatabasePath};Pooling=False"))
            { db.Open();using var c=db.CreateCommand();c.CommandText="SELECT COUNT(*) FROM ReliabilityIncidents WHERE Kind='Collection' AND RecoveredUtc IS NULL";Check(Convert.ToInt32(c.ExecuteScalar())==2,"individual PC incidents retained"); }
            options.Equipments[0].Pcs.RemoveRange(1,2);
            // Real child process owns engine locks and an unfinished run when killed.
            var config=Path.Combine(root,"config.json");await File.WriteAllTextAsync(config,JsonSerializer.Serialize(options));
            var signal=Path.Combine(root,"signal");
            var partial=Path.Combine(options.ArchiveRoot,"M","20200101","old.log.partial");File.WriteAllText(partial,"incomplete");
            // Force a changed source so recovery must verify and replace the stale partial.
            File.AppendAllText(Path.Combine(source,"20200101","old.log")," changed");
            // Force the full-copy path (a crash can leave the destination longer than the committed metadata).
            File.AppendAllText(Path.Combine(options.ArchiveRoot,"M","20200101","old.log"),"uncommitted bytes");
            var start=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false };
            start.ArgumentList.Add("--reliability-child");start.ArgumentList.Add(config);start.ArgumentList.Add(signal);
            using(var child=Process.Start(start)!)
            {
                try
                {
                    var deadline=DateTime.UtcNow.AddSeconds(15);
                    while(!File.Exists(signal) && !child.HasExited && DateTime.UtcNow<deadline)await Task.Delay(50);
                    Check(File.Exists(signal),"child collector reached active run before forced termination");
                    child.Kill(entireProcessTree:true);await child.WaitForExitAsync();
                }
                finally { if(!child.HasExited){child.Kill(true);await child.WaitForExitAsync();} }
            }
            await new ReliableCollector(options).RunAsync(day,day);
            Check(!File.Exists(partial) && File.ReadAllText(Path.Combine(source,"20200101","old.log"))==File.ReadAllText(Path.Combine(options.ArchiveRoot,"M","20200101","old.log")),"process-kill recovery verifies and replaces partial/uncommitted copy");
            // Diagnostics write failure is swallowed, engine independent fallback remains usable.
            var bad=new ReliabilityJournal(Path.Combine(root,"not-created","bad.db"));bad.Initialize();bad.Fault("M","P","Test","failure");
            Check((await new Collector(options).RunAllAsync()).Failed==0,"diagnostic store failure does not stop collector");
            Console.WriteLine("All reliability checks passed.");
        }
        finally { Directory.Delete(root,true); }
    }
}

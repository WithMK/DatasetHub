using System.Diagnostics;
using System.Globalization;
namespace DatasetHub;

// Supervises the existing engine. A timeout requests cancellation; it never detaches an active writer.
public sealed class ReliableCollector(CollectorOptions options, Action<CollectionProgress>? progress = null, string scope = "All")
{
    public async Task<CollectionResult> RunAsync(DateOnly from,DateOnly to,CancellationToken token=default)
    {
        if (!options.Reliability.Enabled) return await new Collector(options,progress,scope).RunAsync(from,to,token).ConfigureAwait(false);
        options.Reliability.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(options.DatabasePath)!);
        using var supervisorLock = new FileStream(options.DatabasePath+".reliability.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        using (var probe = new FileStream(options.DatabasePath+".collector.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) { }
        var journal=new ReliabilityJournal(options.DatabasePath); journal.Initialize();
        var all = scope == "All";
        var dirty=journal.Get("NeedsRecovery") != "false";
        var lastFull=DateTime.TryParse(journal.Get("LastFullUtc"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var date)?date:DateTime.MinValue;
        var full=all && (dirty || DateTime.UtcNow-lastFull > TimeSpan.FromHours(options.Reliability.FullScanIntervalHours));
        // Persist before work so a killed process triggers reconciliation next time.
        if(all) journal.Set("NeedsRecovery","true");
        var effectiveFrom=full?DateOnly.MinValue:from; var effectiveTo=full?DateOnly.MaxValue:to;
        if(all)
        {
            // Include old pending/failed dates without hashing every archived file.
            foreach(var entry in journal.Unfinished())
            {
                var relative=entry.Relative.StartsWith("Image/",StringComparison.Ordinal)?entry.Relative[6..]:entry.Relative;
                var day=relative.Replace('\\','/').Split('/')[0];
                if(DateOnly.TryParseExact(day,"yyyyMMdd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var pendingDate))
                { if(pendingDate<effectiveFrom)effectiveFrom=pendingDate; if(pendingDate>effectiveTo)effectiveTo=pendingDate; }
            }
        }
        CollectionResult? result=null;
        for(var attempt=0;attempt<=options.Reliability.MaxRetries;attempt++)
        {
            token.ThrowIfCancellationRequested();
            journal.Set("Recovery",full || attempt>0 ? $"복구 확인 중 · 시도 {attempt+1}" : "정기 확인 중");
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
            using var watching=new CancellationTokenSource();
            var started=Stopwatch.GetTimestamp(); long lastProgress=started; string currentMachine="", currentPc="";
            var timedOut=0; var missing=false;
            var monitor=Task.Run(async ()=>
            {
                try
                {
                    while(!watching.IsCancellationRequested)
                    {
                        journal.Set("WorkerHeartbeatUtc",DateTime.UtcNow.ToString("O"));
                        journal.Set("CurrentSource",currentMachine+"/"+currentPc);
                        journal.Set("ProgressUtc",DateTime.UtcNow.AddSeconds(-Stopwatch.GetElapsedTime(Interlocked.Read(ref lastProgress)).TotalSeconds).ToString("O"));
                        if(Stopwatch.GetElapsedTime(started).TotalSeconds>options.Reliability.JobTimeoutSeconds ||
                           Stopwatch.GetElapsedTime(Interlocked.Read(ref lastProgress)).TotalSeconds>options.Reliability.NoProgressTimeoutSeconds)
                        {
                            Interlocked.Exchange(ref timedOut,1);
                            journal.Fault(currentMachine,currentPc,"Timeout","작업 무진행/시간 초과. 취소 요청 후 실제 종료 대기; 중복 실행 금지.");
                            journal.Set("Recovery","시간 초과 · 취소 요청 후 작업 종료 대기");
                            timeout.Cancel(); break;
                        }
                        await Task.Delay(500,watching.Token).ConfigureAwait(false);
                    }
                }
                catch(OperationCanceledException) { }
                catch(Exception e) { Trace.WriteLine(e); } // monitor failure must not stop the engine
            });
            Exception? failure=null;
            try
            {
                result=await Task.Run(async () => { var collected = await new Collector(options,item=>
                {
                    if(item.MachineId.Length>0){currentMachine=item.MachineId;currentPc=item.PcId;}Interlocked.Exchange(ref lastProgress,Stopwatch.GetTimestamp());
                    try { progress?.Invoke(item); } catch(Exception e) { Trace.WriteLine(e); }
                },scope).RunAsync(effectiveFrom,effectiveTo,timeout.Token).ConfigureAwait(false);
                    if(collected.Failed==0)missing=CheckUnfinished(journal);
                    return collected; },CancellationToken.None).ConfigureAwait(false);
            }
            catch(OperationCanceledException) when(!token.IsCancellationRequested && timedOut!=0) { failure=new TimeoutException("수집 시간 초과 후 작업이 종료되었습니다."); }
            catch(Exception e) when(e is not OperationCanceledException) { failure=e; }
            finally { watching.Cancel(); await monitor.ConfigureAwait(false); }
            token.ThrowIfCancellationRequested();
            if(timedOut!=0 && failure==null)failure=new TimeoutException("시간 초과 요청 이후 종료된 작업: 복구 확인 필요.");
            if(failure==null && result!=null)
            {
                foreach(var source in result.Sources)
                {
                    if(source.Failed>0)journal.Fault(source.MachineId,source.PcId,"Collection",$"파일/경로 실패 {source.Failed}건. ScanErrors 확인.");
                    else { journal.Resolve(source.MachineId,source.PcId,"Collection"); journal.Resolve(source.MachineId,source.PcId,"Timeout"); }
                }
                if(result.Failed==0)
                {
                    // Unknown files deleted during an outage cannot be inferred. Known unfinished paths can.
                    journal.Resolve(currentMachine,currentPc,"Execution");
                    if(all) { journal.Set("NeedsRecovery",missing?"true":"false"); if(full)journal.Set("LastFullUtc",DateTime.UtcNow.ToString("O")); }
                    journal.Set("Recovery",missing?"복구 불가 원본 있음 · 장애 이력 확인":result.ImagesDeferred+result.Deferred>0?"복구 탐색 완료 · 안정화/완료표시 대기 있음":"확인 완료");
                    return result;
                }
            }
            else journal.Fault(currentMachine,currentPc,"Execution",failure?.Message??"실행 실패");
            if(attempt==options.Reliability.MaxRetries)
            {
                journal.Set("Recovery","자동 재시도 한도 도달 · 다음 정기 주기에 재확인");
                if(failure!=null)throw failure;
                return result!;
            }
            if(all) { effectiveFrom=DateOnly.MinValue;effectiveTo=DateOnly.MaxValue;full=true; }
            var delay=options.Reliability.RetryDelay(attempt);
            journal.Set("Recovery",$"재시도 대기 · {delay.TotalSeconds:F0}초 · {attempt+1}/{options.Reliability.MaxRetries}");
            await Task.Delay(delay,token).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Unreachable retry state.");
    }
    private bool CheckUnfinished(ReliabilityJournal journal)
    {
        var missing=false;
        foreach(var item in journal.Unfinished().Concat(journal.MissingFiles()).Distinct())
        {
            var machine=options.Equipments.FirstOrDefault(x=>x.Enabled && x.MachineId==item.Machine);
            var pc=machine?.Pcs.FirstOrDefault(x=>x.PcId==item.Pc); if(pc==null)continue;
            var image=item.Relative.StartsWith("Image/",StringComparison.Ordinal);var root=image?pc.ImageSourceRoot:pc.SourceRoot;
            if(string.IsNullOrWhiteSpace(root))continue;
            var relative=image?item.Relative[6..]:item.Relative;
            var path=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(Path.TrimEndingDirectorySeparator(root)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;
            try { File.GetAttributes(path); if(journal.Collected(item.Machine,item.Pc,item.Relative)) journal.Resolve(item.Machine,item.Pc,"Missing:"+item.Relative); }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException)
            { missing=true;journal.Fault(item.Machine,item.Pc,"Missing:"+item.Relative,"알려진 미완료 원본 확인 불가: "+path+" · "+e.Message); }
        }
        return missing;
    }
}

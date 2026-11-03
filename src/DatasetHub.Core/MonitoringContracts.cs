namespace DatasetHub.Core;
public sealed record CollectionMetrics(int Found = 0, int Copied = 0, int Unchanged = 0, int Failed = 0, int Deferred = 0, long Bytes = 0)
{
    public override string ToString() => $"{Found} / {Copied} / {Unchanged} / {Failed} / {Deferred} / {Bytes:N0} B";
}
public sealed record SourceCollectionState(string MachineId, string PcId, string Target, string State,
    DateTime? LastAttemptUtc, DateTime? LastSuccessUtc, CollectionMetrics Log, CollectionMetrics Image, string LastError)
{
    public string Health { get; init; } = "Idle";
    public string Connection { get; init; } = "미확인";
    public string LastConnectionSuccess { get; init; } = "";
    public string LastConnectionFailure { get; init; } = "";
    public string LastFailure { get; init; } = "";
    public int ConsecutiveFailures { get; init; }
    public string NextScheduled { get; init; } = "수동/중지";
    public string Duration { get; init; } = "";
    public string Recovery { get; init; } = "";
    public string Incident { get; init; } = "";
    public string Freshness { get; init; } = "";
    public int CollectedFiles => Log.Copied + Image.Copied;
    public string LastAttempt => LastAttemptUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "없음";
    public string LastSuccess => LastSuccessUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "없음";
}
public sealed record CollectionRunEntry(string RunId, string MachineId, string PcId, DateTime StartedUtc, DateTime? CompletedUtc, string Outcome, CollectionMetrics Log, CollectionMetrics Image, string Error = "");
public sealed record CollectionErrorEntry(DateTime OccurredUtc, string MachineId, string PcId, string Path, string Error, string Operation, string RunId);
public sealed record MonitoringSnapshot(int EquipmentCount, int PcCount, DateTime? LastFullRunUtc, IReadOnlyList<SourceCollectionState> Sources);
public interface ICollectionMonitoringService
{
    Task<MonitoringSnapshot> ReadAsync(string configurationPath, TimeSpan delayGrace);
    Task<IReadOnlyList<CollectionRunEntry>> RunsAsync(string configurationPath, string machine, string pc);
    Task<IReadOnlyList<CollectionErrorEntry>> ErrorsAsync(string configurationPath, string machine, string pc);
    Task RetryAsync(string configurationPath, string machine, string? pc, DateOnly from, DateOnly to, CancellationToken token);
}
public sealed record ScheduleState(DateTime StartedUtc, DateTime DueUtc);
public interface ICollectionScheduleState
{
    void Start(string databasePath);
    void Waiting(string databasePath, DateTime dueUtc);
    void Stop(string databasePath);
    ScheduleState? Get(string databasePath);
}

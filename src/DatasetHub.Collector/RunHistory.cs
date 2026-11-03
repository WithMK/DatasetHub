using System.Text.Json;
namespace DatasetHub;
internal sealed partial class History
{
    public string? RunId { get; private set; }
    public string Operation { get; set; } = "Log";
    private void InitializeRuns()
    {
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS CollectionRuns (
              Id TEXT PRIMARY KEY, StartedUtc TEXT NOT NULL, CompletedUtc TEXT, Outcome TEXT NOT NULL,
              FromDate TEXT NOT NULL, ToDate TEXT NOT NULL, Scope TEXT NOT NULL, Error TEXT);
            CREATE TABLE IF NOT EXISTS SourceCollectionRuns (
              RunId TEXT NOT NULL, MachineId TEXT NOT NULL, PcId TEXT NOT NULL,
              StartedUtc TEXT NOT NULL, CompletedUtc TEXT, Outcome TEXT NOT NULL, StatsJson TEXT,
              PRIMARY KEY (RunId,MachineId,PcId));
            CREATE INDEX IF NOT EXISTS IX_SourceRuns_SourceTime ON SourceCollectionRuns(MachineId COLLATE NOCASE,PcId COLLATE NOCASE,StartedUtc DESC);
            CREATE INDEX IF NOT EXISTS IX_CollectionRuns_Time ON CollectionRuns(StartedUtc DESC);
            """;
        cmd.ExecuteNonQuery();
        var columns = new HashSet<string>();
        cmd.CommandText = "PRAGMA table_info(ScanErrors)";
        using (var reader = cmd.ExecuteReader()) while (reader.Read()) columns.Add(reader.GetString(1));
        foreach (var name in new[] { "RunId", "Operation" })
            if (!columns.Contains(name)) { cmd.CommandText = $"ALTER TABLE ScanErrors ADD COLUMN {name} TEXT"; cmd.ExecuteNonQuery(); }
        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_ScanErrors_SourceTime ON ScanErrors(MachineId COLLATE NOCASE,PcId COLLATE NOCASE,UpdatedUtc DESC)";
        cmd.ExecuteNonQuery(); tx.Commit();
    }
    public void BeginRun(DateOnly from, DateOnly to, string scope)
    {
        // Existing writer locks prove that old unfinished runs no longer have a writer.
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE CollectionRuns SET Outcome='Interrupted' WHERE Outcome='Running'; UPDATE SourceCollectionRuns SET Outcome='Interrupted' WHERE Outcome='Running';";
        cmd.ExecuteNonQuery();
        RunId = Guid.NewGuid().ToString("N");
        cmd.CommandText = "INSERT INTO CollectionRuns(Id,StartedUtc,Outcome,FromDate,ToDate,Scope) VALUES($id,$time,'Running',$from,$to,$scope)";
        cmd.Parameters.AddWithValue("$id", RunId); cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd")); cmd.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$scope", scope); cmd.ExecuteNonQuery();
    }
    public void BeginSource(string machine, string pc)
    {
        Operation = "Log";
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO SourceCollectionRuns(RunId,MachineId,PcId,StartedUtc,Outcome) VALUES($id,$m,$p,$time,'Running')";
        cmd.Parameters.AddWithValue("$id", RunId!); cmd.Parameters.AddWithValue("$m", machine); cmd.Parameters.AddWithValue("$p", pc);
        cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O")); cmd.ExecuteNonQuery();
    }
    public void EndSource(SourceScanStatus source)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE SourceCollectionRuns SET CompletedUtc=$time,Outcome=$outcome,StatsJson=$stats WHERE RunId=$id AND MachineId=$m AND PcId=$p";
        cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$outcome", source.Failed > 0 ? "Failed" : "Success");
        cmd.Parameters.AddWithValue("$stats", JsonSerializer.Serialize(source)); cmd.Parameters.AddWithValue("$id", RunId!);
        cmd.Parameters.AddWithValue("$m", source.MachineId); cmd.Parameters.AddWithValue("$p", source.PcId); cmd.ExecuteNonQuery();
    }
    public void EndRun(string outcome, string? error = null)
    {
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "UPDATE CollectionRuns SET CompletedUtc=$time,Outcome=$outcome,Error=$error WHERE Id=$id; UPDATE SourceCollectionRuns SET CompletedUtc=$time,Outcome=$outcome WHERE RunId=$id AND Outcome='Running';";
        cmd.Parameters.AddWithValue("$time", DateTime.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$outcome", outcome);
        cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value); cmd.Parameters.AddWithValue("$id", RunId!);
        cmd.ExecuteNonQuery(); tx.Commit();
    }
}

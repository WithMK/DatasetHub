using System.Globalization;
using System.IO;
using System.Text.Json;
using DatasetHub.Core;
using Microsoft.Data.Sqlite;
namespace DatasetHub.Infrastructure;
public sealed class CollectionMonitoringService(IConfigurationService configurations, ICollectionScheduleState schedules) : ICollectionMonitoringService
{
    private CollectorOptions Options(string path)
    {
        var options = configurations.Read<CollectorOptions>(path);
        options.Validate(Path.GetDirectoryName(Path.GetFullPath(path))!); return options;
    }
    private static SqliteConnection? Open(string path)
    {
        if (!File.Exists(path)) return null;
        var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 3 }.ToString());
        db.Open(); return db;
    }
    private static bool HasTable(SqliteConnection db, string name)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name"; cmd.Parameters.AddWithValue("$name", name);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
    private static DateTime Date(string text) => DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    private static bool WriterActive(string databasePath)
    {
        var path = databasePath + ".collector.lock";
        if (!File.Exists(path)) return false;
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }
    private static (CollectionMetrics Log, CollectionMetrics Image) Metrics(string? json)
    {
        var s = json == null ? null : JsonSerializer.Deserialize<SourceScanStatus>(json);
        return s == null ? (new(), new()) :
            (new(s.Files, s.Updated, s.Unchanged, s.Failed - s.ImageFailed, s.LogDeferred, s.LogBytes),
             new(s.ImageFiles, s.ImagesCopied, s.ImagesSkipped, s.ImageFailed, s.ImagesDeferred, s.ImageBytes));
    }
    public Task<MonitoringSnapshot> ReadAsync(string configurationPath, TimeSpan delayGrace) => Task.Run(() =>
    {
        if (delayGrace < TimeSpan.Zero) throw new ArgumentException("지연 유예 시간은 0 이상이어야 합니다.");
        var options = Options(configurationPath);
        using var db = Open(options.DatabasePath);
        var sources = new List<SourceCollectionState>(); DateTime? lastFull = null;
        var hasRuns = db != null && HasTable(db, "SourceCollectionRuns");
        var active = hasRuns && WriterActive(options.DatabasePath);
        if (hasRuns)
        {
            using var cmd = db!.CreateCommand(); cmd.CommandText = "SELECT MAX(StartedUtc) FROM CollectionRuns WHERE Scope='All'";
            if (cmd.ExecuteScalar() is string text) lastFull = Date(text);
        }
        foreach (var machine in options.Equipments)
        foreach (var pc in machine.Pcs)
        {
            DateTime? attempt = null, success = null; var outcome = ""; var metrics = (Log: new CollectionMetrics(), Image: new CollectionMetrics());
            var error = "";
            if (hasRuns)
            {
                using var cmd = db!.CreateCommand();
                cmd.CommandText = "SELECT s.StartedUtc,s.Outcome,s.StatsJson,CASE WHEN s.Outcome IN ('Failed','Cancelled','Interrupted') THEN r.Error ELSE NULL END FROM SourceCollectionRuns s JOIN CollectionRuns r ON r.Id=s.RunId WHERE s.MachineId=$m COLLATE NOCASE AND s.PcId=$p COLLATE NOCASE ORDER BY s.StartedUtc DESC LIMIT 1";
                Key(cmd, machine.MachineId, pc.PcId);
                using (var reader = cmd.ExecuteReader()) if (reader.Read()) { attempt = Date(reader.GetString(0)); outcome = reader.GetString(1); metrics = Metrics(reader.IsDBNull(2) ? null : reader.GetString(2)); error = reader.IsDBNull(3) ? "" : reader.GetString(3); }
                cmd.CommandText = "SELECT MAX(CompletedUtc) FROM SourceCollectionRuns WHERE MachineId=$m COLLATE NOCASE AND PcId=$p COLLATE NOCASE AND Outcome='Success'";
                if (cmd.ExecuteScalar() is string text) success = Date(text);
            }
            if (db != null && HasTable(db, "ScanErrors"))
            {
                using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT Error FROM ScanErrors WHERE MachineId=$m COLLATE NOCASE AND PcId=$p COLLATE NOCASE ORDER BY UpdatedUtc DESC,Id DESC LIMIT 1";
                Key(cmd, machine.MachineId, pc.PcId); var scanError = cmd.ExecuteScalar() as string; if (error.Length == 0) error = scanError ?? "";
            }
            var state = !machine.Enabled ? "비활성" : attempt == null ? "이력 없음" : outcome switch
            { "Running" => active ? "수집 중" : "중단", "Success" => "정상", "Failed" => "실패", _ => "중단" };
            var schedule = schedules.Get(options.DatabasePath);
            if (machine.Enabled && attempt != null && state == "정상" && schedule != null && DateTime.UtcNow > schedule.DueUtc + delayGrace &&
                (success == null || success < schedule.DueUtc)) state = "지연";
            var target = string.IsNullOrWhiteSpace(pc.SourceRoot) ? "Image" : string.IsNullOrWhiteSpace(pc.ImageSourceRoot) ? "Log" : "Log+Image";
            sources.Add(new(machine.MachineId, pc.PcId, target, state, attempt, success, metrics.Log, metrics.Image, error));
        }
        return new MonitoringSnapshot(options.Equipments.Count, sources.Count, lastFull, sources);
    });
    private static void Key(SqliteCommand cmd, string machine, string pc) { cmd.Parameters.AddWithValue("$m", machine); cmd.Parameters.AddWithValue("$p", pc); }
    public Task<IReadOnlyList<CollectionRunEntry>> RunsAsync(string configurationPath, string machine, string pc) => Task.Run<IReadOnlyList<CollectionRunEntry>>(() =>
    {
        var databasePath = Options(configurationPath).DatabasePath;
        using var db = Open(databasePath); var rows = new List<CollectionRunEntry>();
        var active = WriterActive(databasePath);
        if (db == null || !HasTable(db, "SourceCollectionRuns")) return rows;
        using var cmd = db.CreateCommand(); Key(cmd, machine, pc);
        cmd.CommandText = "SELECT s.RunId,s.StartedUtc,s.CompletedUtc,s.Outcome,s.StatsJson,CASE WHEN s.Outcome IN ('Failed','Cancelled','Interrupted') THEN r.Error ELSE NULL END FROM SourceCollectionRuns s JOIN CollectionRuns r ON r.Id=s.RunId WHERE s.MachineId=$m COLLATE NOCASE AND s.PcId=$p COLLATE NOCASE ORDER BY s.StartedUtc DESC LIMIT 200";
        using var reader = cmd.ExecuteReader(); while (reader.Read())
        {
            var metrics = Metrics(reader.IsDBNull(4) ? null : reader.GetString(4));
            rows.Add(new(reader.GetString(0), machine, pc, Date(reader.GetString(1)), reader.IsDBNull(2) ? null : Date(reader.GetString(2)), reader.GetString(3) == "Running" && !active ? "Interrupted" : reader.GetString(3), metrics.Log, metrics.Image, reader.IsDBNull(5) ? "" : reader.GetString(5)));
        }
        return rows;
    });
    public Task<IReadOnlyList<CollectionErrorEntry>> ErrorsAsync(string configurationPath, string machine, string pc) => Task.Run<IReadOnlyList<CollectionErrorEntry>>(() =>
    {
        using var db = Open(Options(configurationPath).DatabasePath); var rows = new List<CollectionErrorEntry>();
        if (db == null || !HasTable(db, "ScanErrors")) return rows;
        using var cmd = db.CreateCommand(); Key(cmd, machine, pc);
        var modern = HasTable(db, "CollectionRuns");
        cmd.CommandText = "SELECT UpdatedUtc,Path,Error," + (modern ? "Operation,RunId" : "NULL,NULL") + " FROM ScanErrors WHERE MachineId=$m COLLATE NOCASE AND PcId=$p COLLATE NOCASE ORDER BY UpdatedUtc DESC,Id DESC LIMIT 200";
        using var reader = cmd.ExecuteReader(); while (reader.Read()) rows.Add(new(Date(reader.GetString(0)), machine, pc, reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? "기존 이력" : reader.GetString(3), reader.IsDBNull(4) ? "" : reader.GetString(4)));
        return rows;
    });
    public Task RetryAsync(string configurationPath, string machine, string? pc, DateOnly from, DateOnly to, CancellationToken token) => Task.Run(async () =>
    {
        var options = Options(configurationPath);
        options.Equipments = options.Equipments.Where(x => x.Enabled && x.MachineId.Equals(machine, StringComparison.OrdinalIgnoreCase)).ToList();
        if (options.Equipments.Count != 1) throw new ArgumentException("활성 설비를 선택하세요.");
        if (pc != null) options.Equipments[0].Pcs = options.Equipments[0].Pcs.Where(x => x.PcId.Equals(pc, StringComparison.OrdinalIgnoreCase)).ToList();
        if (options.Equipments[0].Pcs.Count == 0) throw new ArgumentException("수집할 PC가 없습니다.");
        await new Collector(options, scope: pc == null ? "Machine" : "PC").RunAsync(from, to, token);
    }, token);
}

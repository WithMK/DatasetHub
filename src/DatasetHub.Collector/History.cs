using Microsoft.Data.Sqlite;
namespace DatasetHub;

internal sealed class History : IDisposable
{
    private readonly SqliteConnection connection;
    public History(string path)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            CREATE TABLE IF NOT EXISTS Files (
              MachineId TEXT NOT NULL, PcId TEXT NOT NULL, RelativePath TEXT NOT NULL,
              Status TEXT NOT NULL, Size INTEGER, Sha256 TEXT, Error TEXT,
              Attempts INTEGER NOT NULL DEFAULT 1, UpdatedUtc TEXT NOT NULL,
              PRIMARY KEY (MachineId, PcId, RelativePath));
            CREATE TABLE IF NOT EXISTS LogRows (
              MachineId TEXT NOT NULL, PcId TEXT NOT NULL, RelativePath TEXT NOT NULL,
              LineNumber INTEGER NOT NULL, EventTime TEXT, LogMachineId TEXT, LogType TEXT,
              LotId TEXT, Recipe TEXT, ProductId TEXT, Status TEXT, Scope TEXT,
              OperationName TEXT, OperationStatus TEXT, Data TEXT, ParseError TEXT,
              PRIMARY KEY (MachineId,PcId,RelativePath,LineNumber));
            CREATE TABLE IF NOT EXISTS ParseState (
              MachineId TEXT NOT NULL, PcId TEXT NOT NULL, RelativePath TEXT NOT NULL,
              Sha256 TEXT NOT NULL, Status TEXT NOT NULL, LastLine INTEGER NOT NULL,
              Error TEXT, UpdatedUtc TEXT NOT NULL,
              PRIMARY KEY (MachineId,PcId,RelativePath));
            CREATE TABLE IF NOT EXISTS ScanErrors (
              Id INTEGER PRIMARY KEY, MachineId TEXT NOT NULL, PcId TEXT NOT NULL,
              Path TEXT NOT NULL, Error TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(Files)";
        using (var reader = columns.ExecuteReader())
        {
            var found = false;
            while (reader.Read()) if (reader.GetString(1) == "SourceModifiedUtc") found = true;
            if (!found)
            {
                reader.Close();
                using var migration = connection.CreateCommand();
                migration.CommandText = "ALTER TABLE Files ADD COLUMN SourceModifiedUtc TEXT";
                migration.ExecuteNonQuery();
            }
        }
    }
    public (long size, DateTime modified)? LastCopy(string machine, string pc, string relative)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Size, SourceModifiedUtc FROM Files WHERE MachineId=$m AND PcId=$p AND RelativePath=$r AND Status='Collected'";
        AddKey(command, machine, pc, relative);
        using var reader = command.ExecuteReader();
        return reader.Read() && !reader.IsDBNull(0) && !reader.IsDBNull(1)
            ? (reader.GetInt64(0), DateTime.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind))
            : null;
    }
    public void Record(string machine, string pc, string relative, string status, long? size = null, string? hash = null, string? error = null, DateTime? sourceModifiedUtc = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Files (MachineId,PcId,RelativePath,Status,Size,Sha256,Error,Attempts,UpdatedUtc,SourceModifiedUtc)
            VALUES ($m,$p,$r,$s,$z,$h,$e,1,$t,$modified)
            ON CONFLICT(MachineId,PcId,RelativePath) DO UPDATE SET
              Status=excluded.Status, Size=COALESCE(excluded.Size,Files.Size),
              Sha256=excluded.Sha256, Error=excluded.Error,
              SourceModifiedUtc=COALESCE(excluded.SourceModifiedUtc,Files.SourceModifiedUtc),
              Attempts=Files.Attempts+1, UpdatedUtc=excluded.UpdatedUtc;
            """;
        command.Parameters.AddWithValue("$m", machine);
        command.Parameters.AddWithValue("$p", pc);
        command.Parameters.AddWithValue("$r", relative);
        command.Parameters.AddWithValue("$s", status);
        command.Parameters.AddWithValue("$z", (object?)size ?? DBNull.Value);
        command.Parameters.AddWithValue("$h", (object?)hash ?? DBNull.Value);
        command.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value);
        command.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$modified", sourceModifiedUtc?.ToString("O") is { } value ? (object)value : DBNull.Value);
        command.ExecuteNonQuery();
    }
    public void ScanError(string machine, string pc, string path, string error)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ScanErrors (MachineId,PcId,Path,Error,UpdatedUtc) VALUES ($m,$p,$r,$e,$t)";
        command.Parameters.AddWithValue("$m", machine);
        command.Parameters.AddWithValue("$p", pc);
        command.Parameters.AddWithValue("$r", path);
        command.Parameters.AddWithValue("$e", error);
        command.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }
    public (bool complete, int invalid) ParsedStatus(string machine, string pc, string relative, string hash)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT (SELECT COUNT(*) FROM ParseState WHERE MachineId=$m AND PcId=$p AND RelativePath=$r AND Sha256=$h AND Status='Complete'),
                   (SELECT COUNT(*) FROM LogRows WHERE MachineId=$m AND PcId=$p AND RelativePath=$r AND ParseError IS NOT NULL);
            """;
        AddKey(cmd, machine, pc, relative);
        cmd.Parameters.AddWithValue("$h", hash);
        using var reader = cmd.ExecuteReader(); reader.Read(); return (reader.GetInt32(0) > 0, reader.GetInt32(1));
    }
    public void CommitRows(string machine, string pc, string relative, string hash, IReadOnlyList<ParsedRow> rows, long lastLine, bool complete)
    {
        using var tx = connection.BeginTransaction();
        foreach (var row in rows)
        {
            using var cmd = connection.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO LogRows VALUES ($m,$p,$r,$line,$time,$machine,$type,$lot,$recipe,$product,$status,$scope,$operation,$opstatus,$data,$error)
                ON CONFLICT(MachineId,PcId,RelativePath,LineNumber) DO UPDATE SET
                  EventTime=excluded.EventTime, LogMachineId=excluded.LogMachineId, LogType=excluded.LogType,
                  LotId=excluded.LotId, Recipe=excluded.Recipe, ProductId=excluded.ProductId, Status=excluded.Status,
                  Scope=excluded.Scope, OperationName=excluded.OperationName, OperationStatus=excluded.OperationStatus,
                  Data=excluded.Data, ParseError=excluded.ParseError;
                """;
            AddKey(cmd, machine, pc, relative);
            cmd.Parameters.AddWithValue("$line", row.Line);
            cmd.Parameters.AddWithValue("$time", (object?)row.EventTime ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$machine", (object?)row.Fields?[1] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$type", (object?)row.Fields?[2] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lot", (object?)row.Fields?[3] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$recipe", (object?)row.Fields?[4] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$product", (object?)row.Fields?[5] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$status", (object?)row.Fields?[6] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$scope", (object?)row.Fields?[7] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$operation", (object?)row.Fields?[8] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$opstatus", (object?)row.Fields?[9] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$data", (object?)row.Fields?[10] ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$error", (object?)row.Error ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ParseState VALUES ($m,$p,$r,$h,$s,$line,NULL,$t)
                ON CONFLICT(MachineId,PcId,RelativePath) DO UPDATE SET
                  Sha256=excluded.Sha256,Status=excluded.Status,LastLine=excluded.LastLine,
                  Error=NULL,UpdatedUtc=excluded.UpdatedUtc;
                """;
            AddKey(cmd, machine, pc, relative);
            cmd.Parameters.AddWithValue("$h", hash);
            cmd.Parameters.AddWithValue("$s", complete ? "Complete" : "Processing");
            cmd.Parameters.AddWithValue("$line", lastLine);
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
    public void ParseFailure(string machine, string pc, string relative, string hash, string error)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ParseState VALUES ($m,$p,$r,$h,'Failed',0,$e,$t)
            ON CONFLICT(MachineId,PcId,RelativePath) DO UPDATE SET
              Status='Failed',Error=excluded.Error,UpdatedUtc=excluded.UpdatedUtc;
            """;
        AddKey(cmd, machine, pc, relative);
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.Parameters.AddWithValue("$e", error);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
    private static void AddKey(SqliteCommand cmd, string machine, string pc, string relative)
    {
        cmd.Parameters.AddWithValue("$m", machine); cmd.Parameters.AddWithValue("$p", pc); cmd.Parameters.AddWithValue("$r", relative);
    }
    public void Dispose() => connection.Dispose();
}

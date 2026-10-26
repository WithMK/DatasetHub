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
            CREATE TABLE IF NOT EXISTS ScanErrors (
              Id INTEGER PRIMARY KEY, MachineId TEXT NOT NULL, PcId TEXT NOT NULL,
              Path TEXT NOT NULL, Error TEXT NOT NULL, UpdatedUtc TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
    }
    public void Record(string machine, string pc, string relative, string status, long? size = null, string? hash = null, string? error = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Files VALUES ($m,$p,$r,$s,$z,$h,$e,1,$t)
            ON CONFLICT(MachineId,PcId,RelativePath) DO UPDATE SET
              Status=excluded.Status, Size=COALESCE(excluded.Size,Files.Size),
              Sha256=COALESCE(excluded.Sha256,Files.Sha256), Error=excluded.Error,
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
    public void Dispose() => connection.Dispose();
}

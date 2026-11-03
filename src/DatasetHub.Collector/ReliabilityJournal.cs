using Microsoft.Data.Sqlite;
namespace DatasetHub;
public sealed class ReliabilityJournal
{
    private readonly string path;
    public ReliabilityJournal(string path) { this.path = path; }
    private T With<T>(Func<SqliteConnection,T> action, T fallback)
    {
        try
        {
            using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Pooling=false, DefaultTimeout=1 }.ToString());
            db.Open(); return action(db);
        }
        catch (Exception error) { System.Diagnostics.Trace.WriteLine("Reliability diagnostic failure: " + error); Console.Error.WriteLine("Reliability diagnostic failure: " + error.Message); return fallback; }
    }
    public void Initialize() => With(db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = """
        CREATE TABLE IF NOT EXISTS ReliabilityIncidents (
          Id INTEGER PRIMARY KEY, MachineId TEXT NOT NULL, PcId TEXT NOT NULL, Kind TEXT NOT NULL,
          FirstUtc TEXT NOT NULL, LastUtc TEXT NOT NULL, Message TEXT NOT NULL, Attempts INTEGER NOT NULL DEFAULT 1,
          RecoveredUtc TEXT);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_Incident_Open ON ReliabilityIncidents(MachineId,PcId,Kind) WHERE RecoveredUtc IS NULL;
        CREATE TABLE IF NOT EXISTS ReliabilityState(Key TEXT PRIMARY KEY,Value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS SourceHealth(MachineId TEXT NOT NULL,PcId TEXT NOT NULL,Connection TEXT NOT NULL,
          LastConnectionSuccessUtc TEXT, LastConnectionFailureUtc TEXT, Error TEXT, PRIMARY KEY(MachineId,PcId));
        """; cmd.ExecuteNonQuery(); return true;
    }, false);
    public string? Get(string key) => With<string?>(db =>
    { using var c=db.CreateCommand(); c.CommandText="SELECT Value FROM ReliabilityState WHERE Key=$k"; c.Parameters.AddWithValue("$k",key); return c.ExecuteScalar() as string; }, null);
    public void Set(string key,string value) => With(db =>
    { using var c=db.CreateCommand(); c.CommandText="INSERT INTO ReliabilityState VALUES($k,$v) ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value"; c.Parameters.AddWithValue("$k",key); c.Parameters.AddWithValue("$v",value); c.ExecuteNonQuery(); return true; },false);
    public void Fault(string machine,string pc,string kind,string message) => With(db =>
    {
        using var c=db.CreateCommand(); c.CommandText="""
        INSERT INTO ReliabilityIncidents(MachineId,PcId,Kind,FirstUtc,LastUtc,Message) VALUES($m,$p,$k,$t,$t,$e)
        ON CONFLICT(MachineId,PcId,Kind) WHERE RecoveredUtc IS NULL DO UPDATE SET LastUtc=excluded.LastUtc,Message=excluded.Message,Attempts=ReliabilityIncidents.Attempts+1;
        """;
        c.Parameters.AddWithValue("$m",machine); c.Parameters.AddWithValue("$p",pc); c.Parameters.AddWithValue("$k",kind); c.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O")); c.Parameters.AddWithValue("$e",message); c.ExecuteNonQuery(); return true;
    }, false);
    public void Resolve(string machine,string pc,string kind) => With(db =>
    { using var c=db.CreateCommand(); c.CommandText="UPDATE ReliabilityIncidents SET RecoveredUtc=$t WHERE MachineId=$m AND PcId=$p AND Kind=$k AND RecoveredUtc IS NULL";
      c.Parameters.AddWithValue("$t",DateTime.UtcNow.ToString("O")); c.Parameters.AddWithValue("$m",machine); c.Parameters.AddWithValue("$p",pc); c.Parameters.AddWithValue("$k",kind); c.ExecuteNonQuery(); return true; }, false);
    public void Connection(string machine,string pc,bool success,string message) => With(db =>
    {
        using var c=db.CreateCommand(); c.CommandText="""
        INSERT INTO SourceHealth(MachineId,PcId,Connection,LastConnectionSuccessUtc,LastConnectionFailureUtc,Error) VALUES($m,$p,$s,$ok,$bad,$e)
        ON CONFLICT(MachineId,PcId) DO UPDATE SET Connection=excluded.Connection,
        LastConnectionSuccessUtc=COALESCE(excluded.LastConnectionSuccessUtc,SourceHealth.LastConnectionSuccessUtc),
        LastConnectionFailureUtc=COALESCE(excluded.LastConnectionFailureUtc,SourceHealth.LastConnectionFailureUtc),Error=excluded.Error;
        """;
        c.Parameters.AddWithValue("$m",machine);c.Parameters.AddWithValue("$p",pc);c.Parameters.AddWithValue("$s",success?"접근 가능":"접근 실패");
        c.Parameters.AddWithValue("$ok",success?(object)DateTime.UtcNow.ToString("O"):DBNull.Value);c.Parameters.AddWithValue("$bad",success?DBNull.Value:(object)DateTime.UtcNow.ToString("O"));c.Parameters.AddWithValue("$e",message);c.ExecuteNonQuery();return true;
    },false);
    public bool Collected(string machine,string pc,string relative) => With(db =>
    { using var c=db.CreateCommand();c.CommandText="SELECT COUNT(*) FROM Files WHERE MachineId=$m AND PcId=$p AND (RelativePath=$r OR RelativePath=$native) AND Status='Collected'";
      c.Parameters.AddWithValue("$m",machine);c.Parameters.AddWithValue("$p",pc);c.Parameters.AddWithValue("$r",relative);c.Parameters.AddWithValue("$native",relative.Replace('/',Path.DirectorySeparatorChar));return Convert.ToInt64(c.ExecuteScalar())>0; },false);
    public IReadOnlyList<(string Machine,string Pc,string Relative)> MissingFiles() => With<IReadOnlyList<(string,string,string)>>(db =>
    { using var c=db.CreateCommand();c.CommandText="SELECT MachineId,PcId,substr(Kind,9) FROM ReliabilityIncidents WHERE Kind LIKE 'Missing:%' AND RecoveredUtc IS NULL";
      var rows=new List<(string,string,string)>();using var reader=c.ExecuteReader();while(reader.Read())rows.Add((reader.GetString(0),reader.GetString(1),reader.GetString(2)));return rows; }, []);
    public IReadOnlyList<(string Machine,string Pc,string Relative)> Unfinished() => With<IReadOnlyList<(string,string,string)>>(db =>
    {
        using var c=db.CreateCommand();c.CommandText="SELECT MachineId,PcId,RelativePath FROM Files WHERE Status IN ('Failed','Collecting','Pending','Deferred')";
        var rows=new List<(string,string,string)>();using var reader=c.ExecuteReader();while(reader.Read())rows.Add((reader.GetString(0),reader.GetString(1),reader.GetString(2)));return rows;
    }, []);
}

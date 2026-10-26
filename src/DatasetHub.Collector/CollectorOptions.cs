namespace DatasetHub;

public sealed class CollectorOptions
{
    public string ArchiveRoot { get; set; } = "";
    public string DatabasePath { get; set; } = "";
    public string CompletionSuffix { get; set; } = ".done";
    public List<Equipment> Equipments { get; set; } = [];

    public void Validate(string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(ArchiveRoot) || string.IsNullOrWhiteSpace(DatabasePath))
            throw new ArgumentException("ArchiveRoot and DatabasePath are required.");
        if (CompletionSuffix != ".done") throw new ArgumentException("This prototype supports only the .done mock completion contract.");
        ArchiveRoot = Path.GetFullPath(ArchiveRoot, baseDirectory);
        DatabasePath = Path.GetFullPath(DatabasePath, baseDirectory);
        var machines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var machine in Equipments)
        {
            CheckId(machine.MachineId);
            if (!machines.Add(machine.MachineId)) throw new ArgumentException("Duplicate MachineId.");
            var pcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pc in machine.Pcs)
            {
                CheckId(pc.PcId);
                if (!pcs.Add(pc.PcId)) throw new ArgumentException("Duplicate PcId.");
                if (string.IsNullOrWhiteSpace(pc.SourceRoot)) throw new ArgumentException("SourceRoot is required.");
                pc.SourceRoot = Path.GetFullPath(pc.SourceRoot, baseDirectory);
                if (Contains(pc.SourceRoot, ArchiveRoot) || Contains(ArchiveRoot, pc.SourceRoot)
                    || Contains(pc.SourceRoot, DatabasePath)) throw new ArgumentException("Source and output paths must not overlap.");
            }
        }
    }
    private static bool Contains(string root, string path) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void CheckId(string value)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9_-]{1,64}$"))
            throw new ArgumentException("IDs must contain 1–64 ASCII letters, digits, underscores or hyphens.");
    }
}
public sealed class Equipment
{
    public string MachineId { get; set; } = "";
    public bool Enabled { get; set; }
    public List<SourcePc> Pcs { get; set; } = [];
}
public sealed class SourcePc
{
    public string PcId { get; set; } = "";
    public string SourceRoot { get; set; } = "";
}
public sealed class CollectionResult
{
    public int Collected { get; set; }
    public int Skipped { get; set; }
    public int Deferred { get; set; }
    public int Failed { get; set; }
}

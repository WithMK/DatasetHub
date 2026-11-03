using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DatasetHub.Core;
namespace DatasetHub.Infrastructure;

public sealed class EquipmentConfigurationService : IEquipmentConfigurationService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    public Task<EquipmentDocument> LoadAsync(string path) => Task.Run(() =>
    {
        var full = Path.GetFullPath(path);
        var bytes = File.ReadAllBytes(full);
        // ReadAllText semantics: support an existing UTF-8 BOM without rewriting path values.
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        var json = JsonNode.Parse(reader.ReadToEnd(), new JsonNodeOptions { PropertyNameCaseInsensitive = true }) as JsonObject
            ?? throw new InvalidDataException("설정은 JSON 객체여야 합니다.");
        var document = new EquipmentDocument(full, Hash(bytes), json);
        Validate(document);
        return document;
    });
    public void Validate(EquipmentDocument document)
    {
        var options = document.Json.Deserialize<CollectorOptions>(Options) ?? throw new InvalidDataException("빈 설정입니다.");
        if (options.Equipments == null || options.Equipments.Any(x => x == null || x.Pcs == null || x.Pcs.Any(pc => pc == null)))
            throw new ArgumentException("설비 및 PC 목록은 null일 수 없습니다.");
        if (options.Equipments.Any(x => x.MachineId == null || x.Pcs.Any(pc => pc.PcId == null)))
            throw new ArgumentException("MachineId 및 PcId는 필수입니다.");
        // Validate a deserialized copy: Collector normalizes paths in place.
        options.Validate(Path.GetDirectoryName(Path.GetFullPath(document.Path))!);
        foreach (var pc in options.Equipments.SelectMany(x => x.Pcs))
            foreach (var path in new[] { pc.SourceRoot, pc.ImageSourceRoot }.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var remainder = path[Path.GetPathRoot(path)!.Length..];
                if (remainder.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(part => part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw new ArgumentException("원본 경로에 사용할 수 없는 문자가 있습니다: " + path);
            }
    }
    public Task<EquipmentDocument> SaveAsync(EquipmentDocument document)
    {
        // Detach immediately, before the async operation starts.
        var snapshot = document with { Json = (JsonObject)document.Json.DeepClone() };
        return Task.Run(() => Save(snapshot));
    }
    private EquipmentDocument Save(EquipmentDocument document)
    {
        Validate(document);
        var path = Path.GetFullPath(document.Path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        // Serialize cooperating app instances. Never truncate the live configuration.
        using var lease = new FileStream(path + ".edit.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Hash(File.ReadAllBytes(path)) != document.Revision)
            throw new IOException("다른 곳에서 설정 파일이 변경되었습니다. 다시 불러온 뒤 수정하세요.");
        var bytes = Encoding.UTF8.GetBytes(document.Json.ToJsonString(Options));
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            // Recheck after writing the temporary file to catch external edits during serialization.
            if (Hash(File.ReadAllBytes(path)) != document.Revision)
                throw new IOException("저장 중 원본 설정이 변경되었습니다. 다시 불러오세요.");
            File.Replace(temp, path, null); // same directory; unsupported shares fail without deleting the original
            return document with { Path = path, Revision = Hash(bytes) };
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

using System.Globalization;
using System.Text.Json;
using DatasetHub;

if (args.Length != 3 || !DateOnly.TryParseExact(args[1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
    || !DateOnly.TryParseExact(args[2], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) || from > to)
{
    Console.Error.WriteLine("Usage: DatasetHub.Collector <config.json> <from:yyyyMMdd> <to:yyyyMMdd>");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    var configPath = Path.GetFullPath(args[0]);
    var options = JsonSerializer.Deserialize<CollectorOptions>(await File.ReadAllTextAsync(configPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
        ?? throw new InvalidDataException("Empty configuration.");
    options.Validate(Path.GetDirectoryName(configPath)!);
    var result = await new Collector(options).RunAsync(from, to, cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result));
    return result.Failed + result.Deferred == 0 ? 0 : 1;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled; rerun the same date range to recover."); return 130; }
catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }

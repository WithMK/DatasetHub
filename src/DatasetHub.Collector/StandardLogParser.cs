using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DatasetHub;

public sealed record ParsedRow(long Line, string? EventTime, string[]? Fields, string? Error);

internal sealed class StandardLogParser(History history)
{
    private static readonly string Header = "Datetime\tMachineID\tLogType\tLotID\tRecipe\tProductID\tStatus\tScope\tOperationName\tOperationStatus\tData";
    public async Task<(int valid, int invalid)> ParseAsync(string machine, string pc, string relative, string archive, string hash, CancellationToken token)
    {
        var prior = history.ParsedStatus(machine, pc, relative, hash);
        if (prior.complete) return (0, prior.invalid);
        var before = new FileInfo(archive);
        if (!before.Exists) throw new IOException("Archive missing.");
        // Rehash before parsing: the file history alone is insufficient to trust the archive.
        await using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (Convert.ToHexString(await SHA256.HashDataAsync(stream, token)) != hash) throw new IOException("Archive checksum changed.");
        stream.Position = 0;
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var batch = new List<ParsedRow>(500);
        int valid = 0, invalid = 0;
        long number = 0;
        try
        {
            while (await reader.ReadLineAsync(token) is { } line)
            {
                number++;
                if (number == 1 && (line == "SEM_LOG_Version=2.0" || line == Header)) continue;
                if (number == 2 && line == Header) continue;
                var fields = line.Split('\t');
                string? error = null;
                string? time = null;
                if (fields.Length != 11) error = $"Expected 11 fields; got {fields.Length}.";
                else if (!DateTime.TryParseExact(fields[0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    error = "Invalid Datetime.";
                else if (fields[9] is not ("Start" or "End" or "Event")) error = "Invalid OperationStatus.";
                else time = parsed.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
                if (error == null) valid++; else invalid++;
                batch.Add(new ParsedRow(number, time, error == null ? fields : null, error));
                if (batch.Count < 500) continue;
                history.CommitRows(machine, pc, relative, hash, batch, number, false);
                batch.Clear();
            }
            history.CommitRows(machine, pc, relative, hash, batch, number, true);
            return (valid, invalid);
        }
        catch (DecoderFallbackException e)
        {
            history.ParseFailure(machine, pc, relative, hash, $"Invalid UTF-8 near line {number}: {e.Message}");
            throw new InvalidDataException("Invalid UTF-8. Archive preserved; confirm actual encoding.", e);
        }
    }
}

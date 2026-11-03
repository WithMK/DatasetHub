using System.Diagnostics;
using System.Security.Cryptography;
namespace DatasetHub;
internal static class TransferIO
{
    public static async Task CopyAsync(Stream input,Stream output,CancellationToken token,Action heartbeat)
    {
        var buffer=new byte[131072];var last=Stopwatch.GetTimestamp();int count;
        while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0)
        {
            await output.WriteAsync(buffer.AsMemory(0,count),token).ConfigureAwait(false);
            if(Stopwatch.GetElapsedTime(last).TotalSeconds>=1){heartbeat();last=Stopwatch.GetTimestamp();}
        }
    }
    public static async Task<string> HashAsync(Stream input,CancellationToken token,Action heartbeat)
    {
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=new byte[131072];var last=Stopwatch.GetTimestamp();int count;
        while((count=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0)
        {
            hash.AppendData(buffer,0,count);
            if(Stopwatch.GetElapsedTime(last).TotalSeconds>=1){heartbeat();last=Stopwatch.GetTimestamp();}
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

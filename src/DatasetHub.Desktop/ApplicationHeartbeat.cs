using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
namespace DatasetHub.Desktop;
internal sealed class ApplicationHeartbeat : IDisposable
{
    private readonly Timer timer;
    private readonly DispatcherTimer uiTimer;
    private long uiTicks=DateTime.UtcNow.Ticks;
    private readonly object gate=new();
    private bool stopped;
    private readonly string path;
    private readonly DateTime started=Process.GetCurrentProcess().StartTime.ToUniversalTime();
    public ApplicationHeartbeat()
    {
        var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DatasetHub","health");
        Directory.CreateDirectory(folder);
        path=Path.Combine(folder,Environment.ProcessId+".json");
        uiTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) };
        uiTimer.Tick+=(_,_)=>Interlocked.Exchange(ref uiTicks,DateTime.UtcNow.Ticks);uiTimer.Start();
        timer=new Timer(_=>Write("Running"),null,TimeSpan.Zero,TimeSpan.FromSeconds(5));
    }
    private void Write(string state)
    {
        lock(gate)
        try
        {
            if(stopped && state=="Running")return;
            var temp=path+".tmp";
            File.WriteAllText(temp,JsonSerializer.Serialize(new { ProcessId=Environment.ProcessId,StartedUtc=started,HeartbeatUtc=DateTime.UtcNow,UiHeartbeatUtc=new DateTime(Interlocked.Read(ref uiTicks),DateTimeKind.Utc),State=state }));
            File.Move(temp,path,true);
        }
        catch(Exception error){Trace.WriteLine(error);}
    }
    public void Dispose(){uiTimer.Stop();timer.Dispose();lock(gate){stopped=true;Write("Stopped");}}
}

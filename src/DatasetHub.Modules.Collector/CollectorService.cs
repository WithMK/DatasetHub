using DatasetHub;
namespace DatasetHub.Modules.Collector;
// No WPF dependency in the service contract: reusable by a future worker host.
public interface ICollectorService
{
    Task<CollectionResult> RunAsync(CollectorOptions options, DateOnly from, DateOnly to, Action<CollectionProgress> progress, CancellationToken token);
}
public sealed class CollectorService : ICollectorService
{
    public Task<CollectionResult> RunAsync(CollectorOptions options, DateOnly from, DateOnly to, Action<CollectionProgress> progress, CancellationToken token)
        => Task.Run(() => new DatasetHub.Collector(options, progress).RunAsync(from, to, token), token);
}

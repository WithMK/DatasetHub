using DatasetHub;
namespace DatasetHub.Modules.Collector;
// No WPF dependency in the service contract: reusable by a future worker host.
public interface ICollectorService
{
    Task<CollectionResult> RunAsync(CollectorOptions options, DateOnly from, DateOnly to, Action<CollectionProgress> progress, CancellationToken token, bool reconcile = true);
}
public sealed class CollectorService : ICollectorService
{
    public Task<CollectionResult> RunAsync(CollectorOptions options, DateOnly from, DateOnly to, Action<CollectionProgress> progress, CancellationToken token, bool reconcile = true)
        => Task.Run(() => new DatasetHub.ReliableCollector(options, progress, reconcile ? "All" : "Manual").RunAsync(from, to, token), token);
}

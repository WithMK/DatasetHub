using System.Collections.Concurrent;
using DatasetHub.Core;
namespace DatasetHub.Infrastructure;
public sealed class CollectionScheduleState : ICollectionScheduleState
{
    private readonly ConcurrentDictionary<string, ScheduleState> states = new(StringComparer.OrdinalIgnoreCase);
    public void Start(string databasePath) => states[databasePath] = new(DateTime.UtcNow, DateTime.UtcNow);
    public void Waiting(string databasePath, DateTime dueUtc) { if (states.TryGetValue(databasePath, out var state)) states[databasePath] = state with { DueUtc = dueUtc }; }
    public void Stop(string databasePath) => states.TryRemove(databasePath, out _);
    public ScheduleState? Get(string databasePath) => states.GetValueOrDefault(databasePath);
}

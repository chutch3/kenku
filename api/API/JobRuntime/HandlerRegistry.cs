using API.JobRuntime.Interfaces;
namespace API.JobRuntime;

/// <summary>
/// Maps a job type to its handler. This is the trust boundary for triggers (AF6b): only registered types
/// can be enqueued or run — a user-supplied type is never executed unless it resolves to a known handler.
/// </summary>
public class HandlerRegistry
{
    private readonly IReadOnlyDictionary<string, IJobHandler> _handlers;

    public HandlerRegistry(IEnumerable<IJobHandler> handlers) =>
        _handlers = handlers.ToDictionary(h => h.JobType);

    public bool IsRegistered(string jobType) => _handlers.ContainsKey(jobType);

    public IJobHandler? Resolve(string jobType) => _handlers.GetValueOrDefault(jobType);

    public IReadOnlyCollection<string> JobTypes => _handlers.Keys.ToArray();
}

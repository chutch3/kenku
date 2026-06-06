using API.JobRuntime;

namespace API.Tests.JobRuntime;

/// <summary>A controllable <see cref="IClock"/> for dispatcher tests — time only moves when advanced.</summary>
public sealed class FakeClock(DateTime? start = null) : IClock
{
    public DateTime UtcNow { get; private set; } = start ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Advance(TimeSpan by) => UtcNow += by;
}

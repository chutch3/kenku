using API.JobRuntime.Interfaces;

namespace API.JobRuntime;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

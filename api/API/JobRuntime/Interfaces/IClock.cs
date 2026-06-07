namespace API.JobRuntime.Interfaces;

/// <summary>Abstracts "now" so the dispatcher's scheduling, backoff, and lease expiry are testable
/// under a fake clock (see DF1–DF7).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

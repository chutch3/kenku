namespace API.JobRuntime;

/// <summary>Exponential backoff between retries: <c>Base * 2^(attempts-1)</c>, capped at <see cref="Max"/>.</summary>
public sealed record BackoffPolicy(TimeSpan Base, TimeSpan Max)
{
    public static readonly BackoffPolicy Default = new(TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));

    public TimeSpan Delay(int attempts)
    {
        double seconds = Base.TotalSeconds * Math.Pow(2, Math.Max(0, attempts - 1));
        return seconds >= Max.TotalSeconds ? Max : TimeSpan.FromSeconds(seconds);
    }
}

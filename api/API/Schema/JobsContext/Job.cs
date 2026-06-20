using System.ComponentModel.DataAnnotations;

namespace API.Schema.JobsContext;

public enum JobStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    NeedsAttention,
    Cancelled
}

/// <summary>
/// Why a parked job is parked, so the UI can offer the right affordance. <see cref="None"/> is an
/// ordinary failure (offer Retry); <see cref="NeedsChoice"/> means the job can't proceed until the user
/// picks between genuine options (e.g. a multi-download post) — distinct from a transient failure that
/// merely happens to land in the same <see cref="JobStatus.NeedsAttention"/> state.
/// </summary>
public enum JobFailureKind
{
    None,
    NeedsChoice
}

/// <summary>
/// A first-class unit of work for the job runtime: a declared input (<see cref="Type"/> + <see cref="Payload"/>),
/// a recorded outcome (<see cref="Status"/>/<see cref="Error"/>), bounded retries (<see cref="Attempts"/>),
/// and the scheduling metadata the dispatcher needs (<see cref="ScheduledFor"/>, <see cref="ResourceKey"/>
/// for fairness/rate-limit, <see cref="DedupKey"/> for coalescing, <see cref="LeasedUntil"/> for crash
/// recovery).
/// </summary>
public class Job : Identifiable
{
    [StringLength(128)] public string Type { get; init; }
    public string Payload { get; init; }
    [StringLength(256)] public string? ResourceKey { get; init; }
    [StringLength(256)] public string? DedupKey { get; init; }
    public JobStatus Status { get; set; }
    public int Priority { get; init; }
    public int Attempts { get; set; }
    public int MaxAttempts { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ScheduledFor { get; set; }
    public DateTime? LeasedUntil { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    [StringLength(2048)] public string? Error { get; set; }
    [StringLength(512)] public string? Progress { get; set; }
    public JobFailureKind FailureKind { get; set; }

    public Job(string type, string payload, DateTime createdAt, string? resourceKey = null,
        string? dedupKey = null, int priority = 0, int maxAttempts = 5)
    {
        Type = type;
        Payload = payload;
        ResourceKey = resourceKey;
        DedupKey = dedupKey;
        Priority = priority;
        MaxAttempts = maxAttempts;
        CreatedAt = createdAt;
        ScheduledFor = createdAt;
        Status = JobStatus.Queued;
    }
}

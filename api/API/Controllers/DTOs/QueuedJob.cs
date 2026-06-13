using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using API.Schema.JobsContext;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Controllers.DTOs;

/// <summary>A <see cref="Job"/> in the runtime queue.</summary>
public record QueuedJob(string Key, string Type, JobStatus Status, int Attempts, int MaxAttempts,
    string? ResourceKey, string? Error, DateTime CreatedAt, DateTime ScheduledFor, DateTime? StartedAt,
    DateTime? FinishedAt, string? Progress, string Payload)
    : Identifiable(Key)
{
    [Required] [Description("The job's payload (JSON, type-specific) — lets the UI link a job to its subject.")]
    public string Payload { get; init; } = Payload;

    [Required] [Description("Registered job type.")] public string Type { get; init; } = Type;
    [Required] [Description("Lifecycle state.")] public JobStatus Status { get; init; } = Status;
    [Required] public int Attempts { get; init; } = Attempts;
    [Required] public int MaxAttempts { get; init; } = MaxAttempts;
    [Description("Fairness/rate-limit key.")] public string? ResourceKey { get; init; } = ResourceKey;
    [Description("Last failure message.")] public string? Error { get; init; } = Error;
    [Required] public DateTime CreatedAt { get; init; } = CreatedAt;
    [Required] public DateTime ScheduledFor { get; init; } = ScheduledFor;
    [Description("When the dispatcher began executing the job; null until it starts.")]
    public DateTime? StartedAt { get; init; } = StartedAt;
    public DateTime? FinishedAt { get; init; } = FinishedAt;
    [Description("Latest progress message from a running job.")] public string? Progress { get; init; } = Progress;

    public static QueuedJob From(JobEntity job) => new(job.Key, job.Type, job.Status, job.Attempts, job.MaxAttempts,
        job.ResourceKey, job.Error, job.CreatedAt, job.ScheduledFor, job.StartedAt, job.FinishedAt, job.Progress, job.Payload);
}

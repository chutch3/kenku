using System.ComponentModel.DataAnnotations;
using API.Workers;

namespace API.Schema.JobsContext;

/// <summary>
/// A persisted record of one worker execution: its type, label, lifecycle state, timing, and error.
/// Written by the worker queue so job status survives restarts and repeated runs stay observable — e.g. a
/// download that keeps "completing" while nothing is actually downloaded (the #31 loop), which the
/// in-memory worker list could never show.
/// </summary>
public class JobRecord : Identifiable
{
    [StringLength(128)] public string Type { get; init; }
    [StringLength(512)] public string Name { get; set; }
    public WorkerExecutionState State { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    [StringLength(2048)] public string? Error { get; set; }

    public JobRecord(string key, string type, string name, DateTime createdAt) : base(key)
    {
        Type = type;
        Name = name;
        CreatedAt = createdAt;
        State = WorkerExecutionState.Running;
    }
}

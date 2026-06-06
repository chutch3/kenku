using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using API.Workers;

namespace API.Controllers.DTOs;

/// <summary>
/// <see cref="API.Schema.JobsContext.JobRecord"/> DTO — a persisted worker execution.
/// </summary>
public record Job(string Key, string Type, string Name, WorkerExecutionState State, DateTime CreatedAt,
    DateTime? StartedAt, DateTime? FinishedAt, string? Error) : Identifiable(Key)
{
    /// <summary>Worker type that ran.</summary>
    [Required] [Description("Worker type that ran.")]
    public string Type { get; init; } = Type;

    /// <summary>Human-readable label for the execution.</summary>
    [Required] [Description("Human-readable label for the execution.")]
    public string Name { get; init; } = Name;

    /// <summary>Execution state.</summary>
    [Required] [Description("Execution state.")]
    public WorkerExecutionState State { get; init; } = State;

    /// <summary>UTC time the record was created.</summary>
    [Required] [Description("UTC time the record was created.")]
    public DateTime CreatedAt { get; init; } = CreatedAt;

    /// <summary>UTC time the execution started, if it has.</summary>
    [Description("UTC time the execution started, if it has.")]
    public DateTime? StartedAt { get; init; } = StartedAt;

    /// <summary>UTC time the execution finished, if it has.</summary>
    [Description("UTC time the execution finished, if it has.")]
    public DateTime? FinishedAt { get; init; } = FinishedAt;

    /// <summary>Failure message, if the execution failed.</summary>
    [Description("Failure message, if the execution failed.")]
    public string? Error { get; init; } = Error;
}

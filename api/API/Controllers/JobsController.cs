using API.Controllers.DTOs;
using API.Schema.JobsContext;
using API.Workers;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static Microsoft.AspNetCore.Http.StatusCodes;
// ReSharper disable InconsistentNaming

namespace API.Controllers;

/// <summary>
/// Persisted job status: the recorded outcome of every worker execution, surviving restarts. Unlike the
/// in-memory <see cref="WorkerController"/>, this exposes history — so a job that keeps "completing"
/// while nothing progresses (the #31 loop) is visible.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{version:apiVersion}/[controller]")]
public class JobsController(JobsContext jobs) : ControllerBase
{
    private const int MaxJobs = 500;

    /// <summary>Most recent persisted job executions.</summary>
    /// <response code="200"><see cref="Job"/></response>
    [HttpGet]
    [ProducesResponseType<List<Job>>(Status200OK, "application/json")]
    public async Task<Ok<List<Job>>> GetJobs()
    {
        List<Job> result = await jobs.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(MaxJobs)
            .Select(j => new Job(j.Key, j.Type, j.Name, j.State, j.CreatedAt, j.StartedAt, j.FinishedAt, j.Error))
            .ToListAsync();
        return TypedResults.Ok(result);
    }

    /// <summary>Counts of persisted jobs grouped by execution state.</summary>
    /// <response code="200">Map of <see cref="WorkerExecutionState"/> to count.</response>
    [HttpGet("Summary")]
    [ProducesResponseType<Dictionary<WorkerExecutionState, int>>(Status200OK, "application/json")]
    public async Task<Ok<Dictionary<WorkerExecutionState, int>>> GetSummary()
    {
        Dictionary<WorkerExecutionState, int> result = await jobs.Jobs
            .GroupBy(j => j.State)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
        return TypedResults.Ok(result);
    }
}

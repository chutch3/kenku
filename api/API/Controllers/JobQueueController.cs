using API.Controllers.DTOs;
using API.Controllers.Requests;
using API.JobRuntime;
using API.Schema.JobsContext;
using Asp.Versioning;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.StatusCodes;
using JobEntity = API.Schema.JobsContext.Job;
// ReSharper disable InconsistentNaming

namespace API.Controllers;

/// <summary>
/// The runtime job queue: enqueue (registry-validated), observe, retry, and cancel first-class jobs.
/// </summary>
[ApiVersion(2)]
[ApiController]
[Route("v{version:apiVersion}/[controller]")]
public class JobQueueController(IJobStore store, HandlerRegistry registry, IClock clock, RunningJobRegistry running)
    : ControllerBase
{
    /// <summary>Enqueues a job. The type must be in the handler registry (trust boundary).</summary>
    /// <response code="201">The enqueued <see cref="QueuedJob"/>.</response>
    /// <response code="400">Unknown/unregistered job type.</response>
    [HttpPost]
    [ProducesResponseType<QueuedJob>(Status201Created, "application/json")]
    [ProducesResponseType<string>(Status400BadRequest, "text/plain")]
    public async Task<Results<Created<QueuedJob>, BadRequest<string>>> Enqueue([FromBody] EnqueueJobRequest request)
    {
        if (!registry.IsRegistered(request.Type))
            return TypedResults.BadRequest($"Unknown job type '{request.Type}'.");

        JobEntity job = await store.EnqueueAsync(new JobEntity(request.Type, request.Payload ?? "{}", clock.UtcNow,
            request.ResourceKey, request.DedupKey, request.Priority));
        return TypedResults.Created($"/v2/JobQueue/{job.Key}", QueuedJob.From(job));
    }

    /// <summary>All runtime jobs.</summary>
    [HttpGet]
    [ProducesResponseType<List<QueuedJob>>(Status200OK, "application/json")]
    public async Task<Ok<List<QueuedJob>>> GetAll() =>
        TypedResults.Ok((await store.GetAllAsync()).Select(QueuedJob.From).ToList());

    /// <summary>Rollup of runtime jobs by status (AF6a) — drives the queue badge/overview.</summary>
    [HttpGet("Summary")]
    [ProducesResponseType<Dictionary<JobStatus, int>>(Status200OK, "application/json")]
    public async Task<Ok<Dictionary<JobStatus, int>>> Summary()
    {
        Dictionary<JobStatus, int> counts = (await store.GetAllAsync())
            .GroupBy(j => j.Status)
            .ToDictionary(g => g.Key, g => g.Count());
        return TypedResults.Ok(counts);
    }

    /// <summary>A single runtime job by id.</summary>
    [HttpGet("{JobId}")]
    [ProducesResponseType<QueuedJob>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    public async Task<Results<Ok<QueuedJob>, NotFound<string>>> Get(string JobId) =>
        await store.GetAsync(JobId) is { } job
            ? TypedResults.Ok(QueuedJob.From(job))
            : TypedResults.NotFound(nameof(JobId));

    /// <summary>Re-queues a <see cref="JobStatus.NeedsAttention"/> job, resetting its attempts.</summary>
    /// <response code="412">The job is not in NeedsAttention.</response>
    [HttpPost("{JobId}/Retry")]
    [ProducesResponseType<QueuedJob>(Status200OK, "application/json")]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status412PreconditionFailed)]
    public async Task<Results<Ok<QueuedJob>, NotFound<string>, StatusCodeHttpResult>> Retry(string JobId)
    {
        if (await store.GetAsync(JobId) is not { } job)
            return TypedResults.NotFound(nameof(JobId));
        if (job.Status != JobStatus.NeedsAttention)
            return TypedResults.StatusCode(Status412PreconditionFailed);

        job.Status = JobStatus.Queued;
        job.Attempts = 0;
        job.ScheduledFor = clock.UtcNow;
        job.Error = null;
        await store.UpdateAsync(job);
        return TypedResults.Ok(QueuedJob.From(job));
    }

    /// <summary>Cancels a job. A Queued job is cancelled immediately; a Running job is signalled to stop
    /// cooperatively — the dispatcher records it Cancelled once the handler honours the token.</summary>
    /// <response code="202">A running job was signalled to cancel.</response>
    [HttpPost("{JobId}/Cancel")]
    [ProducesResponseType<QueuedJob>(Status200OK, "application/json")]
    [ProducesResponseType(Status202Accepted)]
    [ProducesResponseType<string>(Status404NotFound, "text/plain")]
    [ProducesResponseType(Status412PreconditionFailed)]
    public async Task<Results<Ok<QueuedJob>, Accepted, NotFound<string>, StatusCodeHttpResult>> Cancel(string JobId)
    {
        if (await store.GetAsync(JobId) is not { } job)
            return TypedResults.NotFound(nameof(JobId));

        if (job.Status == JobStatus.Queued)
        {
            job.Status = JobStatus.Cancelled;
            job.FinishedAt = clock.UtcNow;
            await store.UpdateAsync(job);
            return TypedResults.Ok(QueuedJob.From(job));
        }

        if (job.Status == JobStatus.Running)
        {
            running.Cancel(job.Key);
            return TypedResults.Accepted((string?)null);
        }

        return TypedResults.StatusCode(Status412PreconditionFailed);
    }
}

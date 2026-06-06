using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace API.Controllers.Requests;

/// <summary>Trigger payload for enqueueing a runtime job. Only a registered <see cref="Type"/> is accepted.</summary>
public record EnqueueJobRequest
{
    [Required] [Description("Registered job type to run.")] public required string Type { get; init; }

    [Description("Typed JSON payload for the handler.")] public string? Payload { get; init; }

    [Description("Fairness/rate-limit key (host or series).")] public string? ResourceKey { get; init; }

    [Description("Coalescing key — a second enqueue with the same key while one is active is a no-op.")]
    public string? DedupKey { get; init; }

    [Description("Higher runs first.")] public int Priority { get; init; }
}

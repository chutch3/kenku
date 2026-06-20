namespace API.JobRuntime;

/// <summary>
/// Thrown by a handler when a job cannot proceed without a user choice (e.g. a multi-download post).
/// The dispatcher parks it as <see cref="Schema.JobsContext.JobStatus.NeedsAttention"/> with
/// <see cref="Schema.JobsContext.JobFailureKind.NeedsChoice"/> and does NOT retry — retrying only
/// re-derives the same choice.
/// </summary>
public sealed class JobChoiceRequiredException(string message) : Exception(message);

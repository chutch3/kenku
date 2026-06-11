
namespace API.Controllers.Responses;

/// <summary>The running build's identity, stamped at compile time by GitInfo + BuildInformation.</summary>
public record VersionResponse(string Version, string Commit, DateTime BuiltAt)
{
    public static VersionResponse Current => new(
        ThisAssembly.Git.Tag is { Length: > 0 } tag ? tag : $"{ThisAssembly.Git.Branch}@{ThisAssembly.Git.Commit}",
        ThisAssembly.Git.Commit,
        BuildInformation.BuildAt);
}

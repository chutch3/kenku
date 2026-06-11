using System.Reflection;

namespace API.Controllers.Responses;

/// <summary>The running build's identity. Release builds carry the semantic version (and "+commit")
/// the pipeline stamps into the assembly; dev builds fall back to local git info. Docker builds have
/// no .git in the build context, so the git fields can be empty there.</summary>
public record VersionResponse(string Version, string Commit, DateTime BuiltAt)
{
    public static VersionResponse Current => From(
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
        ThisAssembly.Git.Tag, ThisAssembly.Git.Branch, ThisAssembly.Git.Commit);

    public static VersionResponse From(string? informationalVersion, string gitTag, string gitBranch, string gitCommit)
    {
        string[] parts = (informationalVersion ?? "").Split('+', 2);
        string version = parts[0];
        string commit = parts.Length > 1 ? parts[1] : gitCommit;

        // "0.0.0"/"1.0.0" are the build-arg and SDK defaults — nothing was stamped.
        if (version is "" or "0.0.0" or "1.0.0")
            version = gitTag is { Length: > 0 } ? gitTag
                : gitCommit is { Length: > 0 } ? $"{gitBranch}@{gitCommit}"
                : "dev";

        return new(version, commit, BuildInformation.BuildAt);
    }
}

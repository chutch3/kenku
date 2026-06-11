using API.Controllers.Responses;
using Xunit;

namespace API.Tests.Unit.Controllers;

/// <summary>
/// Docker builds have no .git, so git-derived fields are empty there; the version must come from the
/// assembly version the pipeline stamps. The "main@" regression came from trusting git info alone.
/// </summary>
public class VersionResponseTests
{
    [Fact]
    public void StampedReleaseBuild_UsesTheAssemblyVersionAndEmbeddedCommit()
    {
        var v = VersionResponse.From("0.17.0+abc1234", gitTag: "", gitBranch: "", gitCommit: "");

        Assert.Equal("0.17.0", v.Version);
        Assert.Equal("abc1234", v.Commit);
    }

    [Theory]
    [InlineData("0.0.0")]
    [InlineData("1.0.0")]
    public void UnstampedBuildWithLocalGit_FallsBackToTagThenBranchAtCommit(string sdkDefault)
    {
        Assert.Equal("v0.16.0", VersionResponse.From(sdkDefault, "v0.16.0", "main", "53a08c9").Version);
        Assert.Equal("main@53a08c9", VersionResponse.From(sdkDefault, "", "main", "53a08c9").Version);
    }

    [Fact]
    public void UnstampedBuildWithoutGit_SaysDevInsteadOfADanglingBranchAt()
    {
        var v = VersionResponse.From("0.0.0", gitTag: "", gitBranch: "main", gitCommit: "");

        Assert.Equal("dev", v.Version);
    }
}

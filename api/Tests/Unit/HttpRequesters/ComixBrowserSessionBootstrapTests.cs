using API.HttpRequesters;

namespace API.Tests.Unit.HttpRequesters;

public class ComixBrowserSessionBootstrapTests
{
    [Fact]
    public void Parse_PreservesTheScopedSolverSession()
    {
        var (userAgent, cookies) = ComixBrowserSessionBootstrap.Parse("""
            {"userAgent":"Solver/1.0","cookies":[{"name":"cf_clearance","value":"test",
              "domain":".comix.to","path":"/","httpOnly":true,"secure":true,"sameSite":"None"}]}
            """);
        Assert.Equal("Solver/1.0", userAgent);
        Assert.Equal("test", Assert.Single(cookies).Value);
        Assert.Equal(".comix.to", cookies[0].Domain);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"userAgent\":\"Solver/1.0\",\"cookies\":[]}")]
    [InlineData("{\"userAgent\":\"\",\"cookies\":[{\"domain\":\"comix.to\"}]}")]
    [InlineData("{\"userAgent\":\"Solver/1.0\",\"cookies\":[{\"domain\":\"evilcomix.to\"}]}")]
    [InlineData("{\"userAgent\":\"Solver/1.0\",\"cookies\":[{\"domain\":\"comix.to\",\"url\":\"https://elsewhere.test\"}]}")]
    public void Parse_RejectsMissingOrOutOfScopeSessions(string json)
    {
        Assert.Throws<InvalidOperationException>(() => ComixBrowserSessionBootstrap.Parse(json));
    }
}

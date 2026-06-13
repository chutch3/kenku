using API.Discovery;
using API.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Unit.Extensions;

/// <summary>
/// The discovery HTTP clients must construct through the real DI graph. The reddit client's
/// User-Agent was once set with the strict typed parser, which threw at construction — invisible to
/// tests that stub <see cref="IRedditFeedClient"/> (the feed end-to-end test does), so it only
/// surfaced when the live job resolved the client. This resolves the real registration instead.
/// </summary>
public class DiscoveryRegistrationTests
{
    [Fact]
    public void RedditFeedClient_ResolvesFromTheRealContainer_WithoutThrowing()
    {
        using ServiceProvider provider = new ServiceCollection().AddKenkuServices().BuildServiceProvider();

        // Resolution builds the typed HttpClient and runs the registration's configure callback —
        // the exact step that threw on the bad User-Agent.
        var client = provider.GetRequiredService<IRedditFeedClient>();

        Assert.NotNull(client);
    }
}

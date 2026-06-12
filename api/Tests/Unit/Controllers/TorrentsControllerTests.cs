using API.Controllers;
using API.Controllers.Responses;
using API.DownloadClients;
using API.DownloadClients.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace API.Tests.Unit.Controllers;

/// <summary>In-flight torrent visibility: what the download client holds, with live progress.</summary>
public class TorrentsControllerTests
{
    private static TorrentsController CreateController(IDownloadClient? client)
    {
        var services = new ServiceCollection();
        if (client is not null)
            services.AddSingleton(client);
        return new TorrentsController
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() },
            },
        };
    }

    [Fact]
    public async Task GetAll_MapsTheClientsEntries()
    {
        var client = new Mock<IDownloadClient>();
        client.Setup(c => c.List(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new DownloadEntry("chap-1", "Saga 060", new DownloadStatus.Downloading(0.42), 0.42, 12),
            new DownloadEntry("pack:s1:abcd1234", "Invincible 001-144", new DownloadStatus.Completed("/d"), 1.0, 3),
            new DownloadEntry("chap-2", "Saga 061", new DownloadStatus.Errored("missingFiles"), 0.1, 0),
        ]);

        var ok = await CreateController(client.Object).GetAll();

        Assert.Equal(3, ok.Value!.Count);
        Assert.Equal(new TorrentResponse("Saga 060", "chap-1", "downloading", 0.42, 12, null), ok.Value[0]);
        Assert.Equal("completed", ok.Value[1].State);
        Assert.Equal("errored", ok.Value[2].State);
        Assert.Equal("missingFiles", ok.Value[2].Error);
    }

    [Fact]
    public async Task GetAll_IsEmpty_WhenNoDownloadClientIsConfigured()
    {
        var ok = await CreateController(null).GetAll();

        Assert.Empty(ok.Value!);
    }
}

using API;
using API.Controllers;
using API.Prowlarr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace API.Tests.Unit.Controllers;

public class MylarApiControllerTests
{
    [Fact]
    public void GetVersion_ReturnsSuccess()
    {
        var (controller, _) = NewController();

        IActionResult result = controller.Dispatch("getVersion");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<MylarStatusResponse>(ok.Value);
        Assert.True(resp.Success);
        Assert.NotNull(resp.Data);
    }

    [Fact]
    public void ListProviders_PartitionsByProtocol()
    {
        var (controller, settings) = NewController();
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/1/api", "k", [7030], "torrent", true));
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "UseMe", "http://p/2/api", "k", [7030], "usenet", true));

        IActionResult result = controller.Dispatch("listProviders");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<MylarListResponse>(ok.Value);
        Assert.True(resp.Success);
        Assert.Single(resp.Data.Torznabs);
        Assert.Equal("Nyaa", resp.Data.Torznabs[0].Name);
        Assert.Single(resp.Data.Newznabs);
        Assert.Equal("UseMe", resp.Data.Newznabs[0].Name);
    }

    [Fact]
    public void AddProvider_UpsertsSyncedIndexer()
    {
        var (controller, settings) = NewController(new()
        {
            ["name"] = "Nyaa",
            ["providertype"] = "Torznab",
            ["host"] = "http://p/1/api",
            ["prov_apikey"] = "key",
            ["enabled"] = "true",
            ["categories"] = "7030,7000",
        });

        IActionResult result = controller.Dispatch("addProvider");

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.True(Assert.IsType<MylarStatusResponse>(ok.Value).Success);

        Assert.Single(settings.SyncedIndexers);
        SyncedIndexerConfig idx = settings.SyncedIndexers[0];
        Assert.Equal("Nyaa", idx.Name);
        Assert.Equal("http://p/1/api", idx.Url);
        Assert.Equal("key", idx.ApiKey);
        Assert.Equal([7030, 7000], idx.Categories);
        Assert.Equal("torrent", idx.Protocol);
        Assert.True(idx.Enabled);
    }

    [Fact]
    public void ChangeProvider_UpdatesExistingByNameAndType()
    {
        var (controller, settings) = NewController(new()
        {
            ["name"] = "Nyaa",
            ["providertype"] = "Torznab",
            ["host"] = "http://p/9/api",
            ["prov_apikey"] = "key2",
            ["enabled"] = "false",
            ["categories"] = "7030",
        });
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/1/api", "key", [7030], "torrent", true));

        IActionResult result = controller.Dispatch("changeProvider");

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(settings.SyncedIndexers);
        Assert.Equal("http://p/9/api", settings.SyncedIndexers[0].Url);
        Assert.False(settings.SyncedIndexers[0].Enabled);
    }

    [Fact]
    public void DelProvider_RemovesByNameAndType()
    {
        var (controller, settings) = NewController(new()
        {
            ["name"] = "Nyaa",
            ["providertype"] = "Torznab",
        });
        settings.AddOrUpdateSyncedIndexer(new SyncedIndexerConfig(0, "Nyaa", "http://p/1/api", "key", [7030], "torrent", true));

        IActionResult result = controller.Dispatch("delProvider");

        Assert.IsType<OkObjectResult>(result);
        Assert.Empty(settings.SyncedIndexers);
    }

    [Fact]
    public void UnknownCommand_ReturnsFailure()
    {
        var (controller, _) = NewController();

        IActionResult result = controller.Dispatch("bogus");

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<MylarStatusResponse>(bad.Value);
        Assert.False(resp.Success);
        Assert.NotNull(resp.Error);
    }

    private static (MylarApiController, KenkuSettings) NewController(Dictionary<string, string>? query = null)
    {
        var settings = new KenkuSettings { AppData = Path.Combine(Path.GetTempPath(), $"kenku-test-{Guid.NewGuid():N}") };
        Directory.CreateDirectory(settings.WorkingDirectory);
        var controller = new MylarApiController(settings);
        var httpContext = new DefaultHttpContext();
        if (query != null)
        {
            QueryString qs = QueryString.Empty;
            foreach (KeyValuePair<string, string> kv in query)
                qs = qs.Add(kv.Key, kv.Value);
            httpContext.Request.QueryString = qs;
        }
        controller.ControllerContext.HttpContext = httpContext;
        return (controller, settings);
    }
}

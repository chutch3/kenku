using System.Text.Json;
using API;
using API.Prowlarr;
using Newtonsoft.Json;
using Xunit;

namespace API.Tests.Prowlarr;

public class MylarIndexerMappingTests
{
    [Fact]
    public void Categories_RoundTripCsvToIntArray()
    {
        var ints = MylarIndexerMapping.ParseCategories("7030,7000, 5070");
        Assert.Equal(new[] { 7030, 7000, 5070 }, ints);

        var csv = MylarIndexerMapping.FormatCategories(new[] { 7030, 7000 });
        Assert.Equal("7030,7000", csv);
    }

    [Fact]
    public void ParseCategories_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(MylarIndexerMapping.ParseCategories(null));
        Assert.Empty(MylarIndexerMapping.ParseCategories(""));
        Assert.Empty(MylarIndexerMapping.ParseCategories("  "));
    }

    [Theory]
    [InlineData("Torznab", "torrent")]
    [InlineData("torznab", "torrent")]
    [InlineData("Newznab", "usenet")]
    [InlineData("newznab", "usenet")]
    public void ProviderType_MapsToProtocol(string providerType, string expected)
    {
        Assert.Equal(expected, MylarIndexerMapping.ProviderTypeToProtocol(providerType));
    }

    [Theory]
    [InlineData("torrent", "Torznab")]
    [InlineData("usenet", "Newznab")]
    public void Protocol_MapsToProviderType(string protocol, string expected)
    {
        Assert.Equal(expected, MylarIndexerMapping.ProtocolToProviderType(protocol));
    }

    [Fact]
    public void ToMylarIndexer_MapsConfigFields()
    {
        var config = new SyncedIndexerConfig(1, "Nyaa", "http://p/1/api", "key", new[] { 7030, 7000 }, "torrent", true);

        var mylar = MylarIndexerMapping.ToMylarIndexer(config);

        Assert.Equal("Nyaa", mylar.Name);
        Assert.Equal("http://p/1/api", mylar.Host);
        Assert.Equal("key", mylar.Apikey);
        Assert.Equal("7030,7000", mylar.Categories);
        Assert.True(mylar.Enabled);
    }

    // Prowlarr inspects lowercase `success`/`data`/`torznabs`/`name`/`host` on the wire. The MVC
    // pipeline serializes controller results with Newtonsoft (AddNewtonsoftJson in Program.cs), so
    // these tests assert the wire shape through the same serializer the request path uses.
    [Fact]
    public void MylarIndexer_SerializesCamelCaseOnTheWire()
    {
        var indexer = new MylarIndexer("Nyaa", "http://p/1/api", "key", "7030,7000", true, "alt");

        var json = JsonConvert.SerializeObject(indexer);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Nyaa", root.GetProperty("name").GetString());
        Assert.Equal("http://p/1/api", root.GetProperty("host").GetString());
        Assert.Equal("key", root.GetProperty("apikey").GetString());
        Assert.Equal("7030,7000", root.GetProperty("categories").GetString());
        Assert.True(root.GetProperty("enabled").GetBoolean());
        Assert.Equal("alt", root.GetProperty("altername").GetString());
    }

    [Fact]
    public void MylarListResponse_SerializesCamelCaseOnTheWire()
    {
        var data = new MylarIndexerData(
            new List<MylarIndexer> { new("Nyaa", "http://p/1/api", "k", "7030", true, "") },
            new List<MylarIndexer>());
        var response = new MylarListResponse(true, data, null);

        var json = JsonConvert.SerializeObject(response);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        var torznabs = root.GetProperty("data").GetProperty("torznabs");
        Assert.Equal(1, torznabs.GetArrayLength());
        Assert.Equal("Nyaa", torznabs[0].GetProperty("name").GetString());
        Assert.Equal(0, root.GetProperty("data").GetProperty("newznabs").GetArrayLength());
    }

    [Fact]
    public void MylarStatusResponse_SerializesCamelCaseOnTheWire()
    {
        var json = JsonConvert.SerializeObject(new MylarStatusResponse(true, "kenku", null));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("kenku", root.GetProperty("data").GetString());
        Assert.True(root.TryGetProperty("error", out _));
    }
}

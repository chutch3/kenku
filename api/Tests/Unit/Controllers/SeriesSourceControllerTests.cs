using API;
using API.Controllers;
using API.Controllers.DTOs;
using ConnectorDto = API.Controllers.DTOs.SeriesSource;
using API.Connectors;
using API.Schema.SeriesContext;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using SeriesSourceImpl = API.Connectors.SeriesSource;

namespace API.Tests.Unit.Controllers;

public class SeriesSourceControllerTests
{
    private readonly KenkuSettings _settings = new() { AppData = Path.GetTempPath() };

    private SeriesContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SeriesContext(options);
    }

    private Mock<SeriesSourceImpl> MakeConnector(string name, bool enabled = true, string[] languages = null!)
    {
        var mock = new Mock<SeriesSourceImpl>(name, languages ?? ["en"], new[] { "example.com" }, "icon.png", _settings);
        mock.Object.Enabled = enabled;
        return mock;
    }

    private SeriesSourceController CreateController(SeriesContext ctx, IEnumerable<SeriesSourceImpl> connectors, KenkuSettings settings)
    {
        var controller = new SeriesSourceController(ctx, connectors, settings);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public void GetConnectors_ReturnsAllConnectors()
    {
        using var ctx = CreateContext();
        var c1 = MakeConnector("MangaDex");
        var c2 = MakeConnector("Mangaworld", languages: ["it"]);

        var result = CreateController(ctx, [c1.Object, c2.Object], _settings).GetConnectors();

        var ok = Assert.IsType<Ok<List<ConnectorDto>>>(result);
        Assert.Equal(2, ok.Value!.Count);
        Assert.Contains(ok.Value, c => c.Name == "MangaDex");
        Assert.Contains(ok.Value, c => c.Name == "Mangaworld");
    }

    [Fact]
    public void GetConnectors_ExposesTheAcquisitionKind_SoTheUiCanTellComicsFromManga()
    {
        using var ctx = CreateContext();
        var manga = MakeConnector("MangaDex");
        var comics = MakeConnector("Indexers");
        comics.Setup(c => c.Kind).Returns(API.Acquirers.AcquisitionKind.Torrent);

        var result = CreateController(ctx, [manga.Object, comics.Object], _settings).GetConnectors();

        var ok = Assert.IsType<Ok<List<ConnectorDto>>>(result);
        Assert.Equal(API.Acquirers.AcquisitionKind.ImageList, ok.Value!.Single(c => c.Name == "MangaDex").Kind);
        Assert.Equal(API.Acquirers.AcquisitionKind.Torrent, ok.Value!.Single(c => c.Name == "Indexers").Kind);
    }

    [Fact]
    public void GetConnectors_WhenEmpty_ReturnsEmptyList()
    {
        using var ctx = CreateContext();

        var result = CreateController(ctx, [], _settings).GetConnectors();

        var ok = Assert.IsType<Ok<List<ConnectorDto>>>(result);
        Assert.Empty(ok.Value!);
    }

    [Fact]
    public void GetConnector_KnownName_ReturnsConnector()
    {
        using var ctx = CreateContext();
        var connector = MakeConnector("MangaDex");

        var result = CreateController(ctx, [connector.Object], _settings).GetConnector("MangaDex");

        var ok = Assert.IsType<Ok<ConnectorDto>>(result.Result);
        Assert.Equal("MangaDex", ok.Value!.Name);
    }

    [Fact]
    public void GetConnector_CaseInsensitiveLookup_ReturnsConnector()
    {
        using var ctx = CreateContext();
        var connector = MakeConnector("MangaDex");

        var result = CreateController(ctx, [connector.Object], _settings).GetConnector("mangadex");

        Assert.IsType<Ok<ConnectorDto>>(result.Result);
    }

    [Fact]
    public void GetConnector_UnknownName_ReturnsNotFound()
    {
        using var ctx = CreateContext();

        var result = CreateController(ctx, [], _settings).GetConnector("UnknownSite");

        Assert.IsType<NotFound<string>>(result.Result);
    }

    [Fact]
    public void GetEnabledConnectors_ReturnsOnlyEnabledConnectors()
    {
        using var ctx = CreateContext();
        var enabled = MakeConnector("MangaDex", enabled: true);
        var disabled = MakeConnector("Mangaworld", enabled: false);

        var result = CreateController(ctx, [enabled.Object, disabled.Object], _settings).GetEnabledConnectors(true);

        var ok = Assert.IsType<Ok<List<ConnectorDto>>>(result);
        Assert.Single(ok.Value!);
        Assert.Equal("MangaDex", ok.Value![0].Name);
    }

    [Fact]
    public void GetEnabledConnectors_ReturnsOnlyDisabledConnectors()
    {
        using var ctx = CreateContext();
        var enabled = MakeConnector("MangaDex", enabled: true);
        var disabled = MakeConnector("Mangaworld", enabled: false);

        var result = CreateController(ctx, [enabled.Object, disabled.Object], _settings).GetEnabledConnectors(false);

        var ok = Assert.IsType<Ok<List<ConnectorDto>>>(result);
        Assert.Single(ok.Value!);
        Assert.Equal("Mangaworld", ok.Value![0].Name);
    }

    [Fact]
    public void GetConnectors_ReturnsSupportedLanguages()
    {
        using var ctx = CreateContext();
        var connector = MakeConnector("Mangaworld", languages: ["it", "en"]);

        var result = CreateController(ctx, [connector.Object], _settings).GetConnectors();

        var ok = Assert.IsType<Ok<List<ConnectorDto>>>(result);
        Assert.Contains("it", ok.Value![0].SupportedLanguages);
        Assert.Contains("en", ok.Value![0].SupportedLanguages);
    }

    // --- Persistence: the settings record only explicit on/off choices; a source without one uses its default ---

    [Fact]
    public void SetEnabled_RecordsTheChoice()
    {
        using var ctx = CreateContext();
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        var controller = CreateController(ctx, [MakeConnector("MangaDex").Object, MakeConnector("Comix", enabled: false).Object], settings);

        controller.SetEnabled("MangaDex", false);
        controller.SetEnabled("Comix", true);

        Assert.Equal(new Dictionary<string, bool> { ["MangaDex"] = false, ["Comix"] = true }, settings.ConnectorEnabled);
    }

    [Fact]
    public void SetEnabled_UnknownConnector_ReturnsNotFound()
    {
        using var ctx = CreateContext();
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };

        var result = CreateController(ctx, [], settings).SetEnabled("Unknown", false);

        Assert.IsType<NotFound<string>>(result.Result);
        Assert.Empty(settings.ConnectorEnabled);
    }

    [Fact]
    public void ApplyConnectorChoices_UsesTheRecordedChoice()
    {
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        settings.ConnectorEnabled["MangaDex"] = false;
        settings.ConnectorEnabled["Comix"] = true;
        var mangaDex = MakeConnector("MangaDex", enabled: true);
        var optIn = MakeConnector("Comix", enabled: false);
        optIn.Setup(c => c.EnabledByDefault).Returns(false);

        settings.ApplyConnectorChoices([mangaDex.Object, optIn.Object]);

        Assert.False(mangaDex.Object.Enabled);
        Assert.True(optIn.Object.Enabled); // a recorded "on" beats the source's off-by-default
    }

    [Fact]
    public void ApplyConnectorChoices_UsesTheSourcesDefault_WhenNoChoiceIsRecorded()
    {
        var settings = new KenkuSettings { AppData = Path.GetTempPath() };
        var onByDefault = new FakeSeriesSource("MangaDex", settings) { Enabled = false }; // real base default
        var offByDefault = MakeConnector("Comix", enabled: true);
        offByDefault.Setup(c => c.EnabledByDefault).Returns(false);

        settings.ApplyConnectorChoices([onByDefault, offByDefault.Object]);

        Assert.True(onByDefault.Enabled);
        Assert.False(offByDefault.Object.Enabled);
    }
}

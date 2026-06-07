using API;
using API.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace API.Tests.Unit.Controllers;

public class SettingsControllerTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly KenkuSettings _settings;

    public SettingsControllerTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"SettingsTest_{Guid.NewGuid()}");
        _settings = new KenkuSettings { AppData = _tmpDir };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, true);
    }

    private SettingsController CreateController()
    {
        var controller = new SettingsController(_settings);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return controller;
    }

    [Fact]
    public void GetSettings_ReturnsApiKeyAndProjectedLists()
    {
        _settings.SyncedIndexers.Add(new SyncedIndexerConfig(1, "Nyaa", "http://p/1/api", "indexer-key", [7030], "torrent", true));
        _settings.DownloadClients.Add(new DownloadClientConfig(1, "qbit", DownloadClientType.QBittorrent, "http://qbit", "admin", "s3cret", "comics", true, 1));

        var result = CreateController().GetSettings();

        var ok = Assert.IsType<Ok<API.Controllers.Responses.SettingsResponse>>(result);
        Assert.Equal(_settings.ApiKey, ok.Value!.ApiKey);
        Assert.Single(ok.Value.SyncedIndexers);
        Assert.Equal("Nyaa", ok.Value.SyncedIndexers[0].Name);
        Assert.Single(ok.Value.DownloadClients);
        Assert.Equal("qbit", ok.Value.DownloadClients[0].Name);
    }

    [Fact]
    public void GetUserAgent_ReturnsCurrentUserAgent()
    {
        _settings.UserAgent = "TestAgent/1.0";

        var result = CreateController().GetUserAgent();

        var ok = Assert.IsType<Ok<string>>(result);
        Assert.Equal("TestAgent/1.0", ok.Value);
    }

    [Fact]
    public void SetUserAgent_UpdatesUserAgent()
    {
        CreateController().SetUserAgent("NewAgent/2.0");

        Assert.Equal("NewAgent/2.0", _settings.UserAgent);
    }

    [Fact]
    public void ResetUserAgent_RestoresDefaultUserAgent()
    {
        _settings.UserAgent = "CustomAgent/1.0";

        CreateController().ResetUserAgent();

        Assert.Equal(KenkuSettings.DefaultUserAgent, _settings.UserAgent);
    }

    [Fact]
    public void GetImageCompression_ReturnsCurrentLevel()
    {
        _settings.ImageCompression = 75;

        var result = CreateController().GetImageCompression();

        var ok = Assert.IsType<Ok<int>>(result);
        Assert.Equal(75, ok.Value);
    }

    [Fact]
    public void SetImageCompression_ValidLevel_UpdatesAndReturnsOk()
    {
        var result = CreateController().SetImageCompression(60);

        Assert.IsType<Ok>(result.Result);
        Assert.Equal(60, _settings.ImageCompression);
    }

    [Fact]
    public void SetImageCompression_LevelZero_ReturnsBadRequest()
    {
        var result = CreateController().SetImageCompression(0);

        Assert.IsType<BadRequest>(result.Result);
        Assert.Equal(40, _settings.ImageCompression); // unchanged from default
    }

    [Fact]
    public void SetImageCompression_LevelAbove100_ReturnsBadRequest()
    {
        var result = CreateController().SetImageCompression(101);

        Assert.IsType<BadRequest>(result.Result);
    }

    [Fact]
    public void GetBwImagesToggle_ReturnsCurrentState()
    {
        _settings.BlackWhiteImages = true;

        var result = CreateController().GetBwImagesToggle();

        var ok = Assert.IsType<Ok<bool>>(result);
        Assert.True(ok.Value);
    }

    [Fact]
    public void SetBwImagesToggle_UpdatesState()
    {
        CreateController().SetBwImagesToggle(true);

        Assert.True(_settings.BlackWhiteImages);
    }

    [Fact]
    public void GetCustomNamingScheme_ReturnsCurrentScheme()
    {
        _settings.ChapterNamingScheme = "%M - Ch.%C";

        var result = CreateController().GetCustomNamingScheme();

        var ok = Assert.IsType<Ok<string>>(result);
        Assert.Equal("%M - Ch.%C", ok.Value);
    }

    [Fact]
    public void SetCustomNamingScheme_UpdatesScheme()
    {
        CreateController().SetCustomNamingScheme("?V(Vol.%V/)%M - Ch.%C");

        Assert.Equal("?V(Vol.%V/)%M - Ch.%C", _settings.ChapterNamingScheme);
    }

    [Fact]
    public void GetDownloadLanguage_ReturnsCurrentLanguage()
    {
        _settings.DownloadLanguage = "jp";

        var result = CreateController().GetDownloadLanguage();

        var ok = Assert.IsType<Ok<string>>(result);
        Assert.Equal("jp", ok.Value);
    }

    [Fact]
    public void SetDownloadLanguage_UpdatesLanguage()
    {
        CreateController().SetDownloadLanguage("fr");

        Assert.Equal("fr", _settings.DownloadLanguage);
    }

    [Fact]
    public void SetFlareSolverrUrl_UpdatesUrl()
    {
        CreateController().SetFlareSolverrUrl("http://localhost:8191");

        Assert.Equal("http://localhost:8191", _settings.FlareSolverrUrl);
    }

    [Fact]
    public void ClearFlareSolverrUrl_SetsUrlToEmpty()
    {
        _settings.FlareSolverrUrl = "http://localhost:8191";

        CreateController().ClearFlareSolverrUrl();

        Assert.Equal(string.Empty, _settings.FlareSolverrUrl);
    }

    [Fact]
    public void SetMetron_PersistsCredentials()
    {
        CreateController().SetMetron(new API.Controllers.Requests.SetMetronRecord { Username = "u", Password = "p" });

        Assert.Equal("u", _settings.MetronUsername);
        Assert.Equal("p", _settings.MetronPassword);
    }

    [Fact]
    public void ClearMetron_EmptiesCredentials()
    {
        _settings.MetronUsername = "u";
        _settings.MetronPassword = "p";

        CreateController().ClearMetron();

        Assert.Equal(string.Empty, _settings.MetronUsername);
        Assert.Equal(string.Empty, _settings.MetronPassword);
    }

    [Fact]
    public void GetApiKey_ReturnsConfiguredKey()
    {
        Ok<string> result = CreateController().GetApiKey();

        Assert.Equal(_settings.ApiKey, result.Value);
    }

    [Fact]
    public void RegenerateApiKey_ChangesKeyAndReturnsIt()
    {
        Directory.CreateDirectory(_settings.WorkingDirectory);
        var original = _settings.ApiKey;

        Ok<string> result = CreateController().RegenerateApiKey();

        Assert.NotEqual(original, _settings.ApiKey);
        Assert.Equal(_settings.ApiKey, result.Value);
    }

    [Fact]
    public void AddDownloadClient_WithBlankName_ReturnsBadRequest()
    {
        var result = CreateController().AddDownloadClient(new API.Controllers.Requests.SetDownloadClientRecord(
            0, "", DownloadClientType.QBittorrent, "http://qbit", null, null, null, true, 1));

        Assert.IsType<BadRequest>(result.Result);
    }

    [Fact]
    public void AddDownloadClient_WithValidRecord_PersistsAndReturnsCreated()
    {
        Directory.CreateDirectory(_settings.WorkingDirectory);

        var result = CreateController().AddDownloadClient(new API.Controllers.Requests.SetDownloadClientRecord(
            0, "qbit", DownloadClientType.QBittorrent, "http://qbit", "u", "p", "comics", true, 1));

        var ok = Assert.IsType<Ok<API.Controllers.Responses.DownloadClientResponse>>(result.Result);
        Assert.NotNull(ok.Value);
        Assert.True(ok.Value!.Id > 0);
        Assert.Single(_settings.DownloadClients);
        Assert.Equal("qbit", _settings.DownloadClients[0].Name);
        Assert.Equal("p", _settings.DownloadClients[0].Password); // persisted, even though not returned
    }

    [Fact]
    public void RemoveDownloadClient_Unknown_ReturnsNotFound()
    {
        var result = CreateController().RemoveDownloadClient(999);

        Assert.IsType<NotFound>(result.Result);
    }

    // GetSettings returns a secret-free DTO. Serialized through the API's System.Text.Json pipeline,
    // no credential must appear on the wire — only the (UI-displayed) Prowlarr push key.
    [Fact]
    public void GetSettings_SerializedForApi_OmitsSecrets()
    {
        _settings.DownloadClients.Add(new DownloadClientConfig(1, "qbit", DownloadClientType.QBittorrent, "http://qbit", "admin", "s3cret", "comics", true, 1));
        _settings.SyncedIndexers.Add(new SyncedIndexerConfig(1, "Nyaa", "http://p/1/api", "indexer-key", [7030], "torrent", true));
        _settings.MetronPassword = "metron-pw";

        var ok = Assert.IsType<Ok<API.Controllers.Responses.SettingsResponse>>(CreateController().GetSettings());
        string json = System.Text.Json.JsonSerializer.Serialize(ok.Value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.DoesNotContain("s3cret", json);
        Assert.DoesNotContain("indexer-key", json);
        Assert.DoesNotContain("metron-pw", json);
        // Non-secret config the UI relies on must still be present.
        Assert.Contains("qbit", json);
        Assert.Contains("Nyaa", json);
        Assert.Contains(_settings.ApiKey, json); // the Prowlarr push key is shown in the UI by design
    }

    [Fact]
    public void GetDownloadClients_OmitsPasswords()
    {
        _settings.DownloadClients.Add(new DownloadClientConfig(1, "qbit", DownloadClientType.QBittorrent, "http://qbit", "admin", "s3cret", "comics", true, 1));

        var ok = Assert.IsType<Ok<List<API.Controllers.Responses.DownloadClientResponse>>>(CreateController().GetDownloadClients());
        string json = System.Text.Json.JsonSerializer.Serialize(ok.Value,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.DoesNotContain("s3cret", json);
        Assert.Contains("qbit", json);
    }

    [Fact]
    public void Save_KeepsSecrets_OnDisk()
    {
        Directory.CreateDirectory(_settings.WorkingDirectory);
        _settings.DownloadClients.Add(new DownloadClientConfig(1, "qbit", DownloadClientType.QBittorrent, "http://qbit", "admin", "s3cret", "comics", true, 1));
        _settings.Save();

        string onDisk = File.ReadAllText(_settings.SettingsFilePath);

        Assert.Contains("s3cret", onDisk); // persistence must retain the secret
    }
}

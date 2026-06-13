using API.Tests;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using API;
using API.Controllers.DTOs;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// The search → add-preview contract through the real GetComics connector and HTTP API — the exact
/// path the AddSeriesModal walks. Replicates the reported one-shot case (a comic that appears in
/// search but previewed as zero chapters) and pins it next to the competing title shapes, so the
/// one-shot rule can't regress numbered/volume listing. Only the network edge is faked.
/// </summary>
public class GetComicsChapterListingEndToEndTests : IAsyncLifetime
{
    private static string Article(string url, string title) => $"""
        <article>
          <div class="post-header-image"><a href="{url}"><img src="https://img.test/c.jpg" alt="{title}"></a></div>
          <div class="post-info"><h1 class="post-title"><a href="{url}">{title}</a></h1></div>
        </article>
        """;

    private static string ArchivePage(params string[] articles) => $"""
        <html><body>
        <section class="page-contents post-list post-list-masonry"><div class="post-list-posts">
        {string.Join("\n", articles)}
        </div></section></body></html>
        """;

    // A God Somewhere is an original graphic novel: its post title carries a year, no issue number.
    private static readonly string OneShotPage = ArchivePage(
        Article("https://getcomics.org/other-comics/a-god-somewhere-2010/", "A God Somewhere (2010)"));

    // Saga's archive lists numbered issues AND a numberless edition post — the regression trap:
    // the one-shot rule must not turn that numberless post into a phantom "chapter 1".
    private static readonly string SagaPage = ArchivePage(
        Article("https://getcomics.org/c/saga-60/", "Saga #60 (2024)"),
        Article("https://getcomics.org/c/saga-61/", "Saga #61 (2024)"),
        Article("https://getcomics.org/c/saga-ed/", "Saga (2024)"));

    // The API serializes enums (MinimalSeries.ReleaseStatus) as strings.
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly PostgresFixture _postgres = new();
    private string _dbName = null!;
    private KenkuApplicationFactory _app = null!;

    public async Task InitializeAsync()
    {
        _dbName = await _postgres.CreateDatabaseAsync();

        var inner = new FakeHttpMessageHandler(req =>
        {
            string url = req.RequestUri!.ToString();
            // Pages past the first end the archive walk.
            if (url.Contains("/page/")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (url.Contains("god-somewhere", StringComparison.OrdinalIgnoreCase) || url.Contains("God+Somewhere", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OneShotPage) };
            if (url.Contains("saga", StringComparison.OrdinalIgnoreCase))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SagaPage) };
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        _app = new KenkuApplicationFactory
        {
            PostgresConnectionString = _postgres.GetConnectionString(_dbName),
            RateLimit = (inner, RequestsPerMinute: 600, QueueLimit: 100, RequestTimeout: TimeSpan.FromSeconds(5)),
        };
    }

    public async Task DisposeAsync()
    {
        _app.Dispose();
        await _postgres.DropDatabaseAsync(_dbName);
    }

    private async Task<List<ChapterPreview>> PreviewChapters(HttpClient http, string seriesId) =>
        await http.GetFromJsonAsync<List<ChapterPreview>>(
            $"/v2/Search/GetComics/Chapters?ConnectorSeriesId={Uri.EscapeDataString(seriesId)}", Json)
        ?? throw new InvalidOperationException("null chapter list");

    [Fact]
    public async Task OneShot_AppearsInSearch_AndPreviewsAsASingleChapter()
    {
        using HttpClient http = _app.CreateClient();

        var results = await http.GetFromJsonAsync<List<MinimalSeries>>(
            $"/v2/Search/GetComics/{Uri.EscapeDataString("A God Somewhere")}", Json);
        Assert.Contains(results!, s => s.Name == "A God Somewhere");

        var chapters = await PreviewChapters(http, "A God Somewhere");

        var chapter = Assert.Single(chapters);
        Assert.Equal("1", chapter.ChapterNumber);
        Assert.Null(chapter.VolumeNumber);
    }

    [Fact]
    public async Task NumberedSeries_PreviewsItsIssues_AndIgnoresANumberlessEditionPost()
    {
        using HttpClient http = _app.CreateClient();

        var chapters = await PreviewChapters(http, "Saga");

        Assert.Equal(["60", "61"], chapters.Select(c => c.ChapterNumber).OrderBy(n => n));
        Assert.DoesNotContain(chapters, c => c.ChapterNumber == "1");
    }
}

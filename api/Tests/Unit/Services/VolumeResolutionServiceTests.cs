using API.Services.Interfaces;
using System.Collections.Concurrent;
using System.IO.Compression;
using API;
using API.Schema.ActionsContext;
using API.Schema.SeriesContext;
using API.Services;
using API.MetadataResolvers;
using API.MetadataResolvers.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SixLabors.ImageSharp;
using Xunit;

namespace API.Tests.Unit.Services;

public class VolumeResolutionServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly SeriesContext _mangaContext;
    private readonly ActionsContext _actionsContext;
    private readonly Mock<IMangaDexVolumeResolver> _mockMangaDexResolver;
    private readonly Mock<IMangaDexSearchService> _mockSearchService;

    public VolumeResolutionServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ResolveForMangaTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);

        var mangaOptions = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _mangaContext = new SeriesContext(mangaOptions);

        var actionsOptions = new DbContextOptionsBuilder<ActionsContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _actionsContext = new ActionsContext(actionsOptions);


        _mockMangaDexResolver = new Mock<IMangaDexVolumeResolver>();
        _mockMangaDexResolver
            .Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int>());

        _mockSearchService = new Mock<IMangaDexSearchService>();
        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>());
        _mockSearchService
            .Setup(s => s.GetChapterToVolumeMapAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
            Directory.Delete(_testRoot, true);
        _mangaContext.Dispose();
        _actionsContext.Dispose();
    }

    private async Task Resolve(KenkuSettings settings, string mangaKey,
        IMangaDexVolumeResolver? resolver = null, SeriesContext? context = null)
    {
        await new VolumeResolutionService(settings, resolver ?? _mockMangaDexResolver.Object, _mockSearchService.Object)
            .ResolveAsync(context ?? _mangaContext, mangaKey, CancellationToken.None);
    }

    private async Task ResolveAll(KenkuSettings settings, IEnumerable<string> mangaKeys)
    {
        foreach (var key in mangaKeys)
            await Resolve(settings, key);
    }

    // Absorbs the former RefreshMetadataSource worker: a Confirmed link is synced from its source even when
    // auto-resolution is Disabled, applying the exact map and stamping LastSyncedAt.
    [Fact]
    public async Task Resolve_ConfirmedSource_AppliesExactMap_AndStampsLastSyncedAt_EvenWhenDisabled()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.Disabled };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("One Piece", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.Confirmed;
        manga.MetadataSource.ExternalId = "op-id";
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, null));
        _mangaContext.Chapters.Add(new Chapter(manga, "12", null, null));
        await _mangaContext.SaveChangesAsync();

        _mockMangaDexResolver
            .Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 1 }, { "12", 2 } });

        await Resolve(settings, manga.Key);

        var ch = await _mangaContext.Chapters.ToDictionaryAsync(c => c.ChapterNumber, c => c);
        Assert.Equal(1, ch["1"].VolumeNumber);
        Assert.Equal(MetadataConfidence.Exact, ch["1"].MetadataConfidence);
        Assert.Equal(2, ch["12"].VolumeNumber);
        var source = await _mangaContext.Series.Include(m => m.MetadataSource).FirstAsync(m => m.Key == manga.Key);
        Assert.NotNull(source.MetadataSource!.LastSyncedAt);
    }

    [Fact]
    public async Task DoWork_WhenExactLookupFails_FallsBackToColorHeuristic()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));
        CreateGrayscaleCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key);

        Assert.NotNull((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.NotNull((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenFirstChapterNotColor_AbortsHeuristic()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test No Cover", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateGrayscaleCbz(Path.Combine(mangaDir, "chap1.cbz"));
        CreateColorCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key);

        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenStrategyExactOnly_DoesNotRunHeuristic()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Exact Only", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));

        await Resolve(settings, manga.Key);

        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenFileMissing_SkipsGracefullyAndEvaluatesNext()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Missing File", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        // chap1.cbz intentionally absent
        CreateGrayscaleCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key);

        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenMangaHasExistingVolumes_HeuristicStartsFromMaxVolume()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        string dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<SeriesContext>().UseInMemoryDatabase(dbName).Options;

        FileLibrary library;
        Series manga;
        using (var setupContext = new SeriesContext(options))
        {
            library = new FileLibrary(_testRoot, "Test Library");
            setupContext.FileLibraries.Add(library);
            manga = new Series("Test Continuation", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            setupContext.Series.Add(manga);
            setupContext.Chapters.Add(new Chapter(manga, "1", 10, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
            setupContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
            await setupContext.SaveChangesAsync();
        }

        using var workerContext = new SeriesContext(options);

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key, context: workerContext);

        Assert.Equal(11, (await workerContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenMangaDexReturnsFullMap_AllChaptersGetVolumes()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test MangaDex Full", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        var resolver = new Mock<IMangaDexVolumeResolver>();
        resolver.Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 3 }, { "2", 3 } });

        await Resolve(settings, manga.Key, resolver.Object);

        Assert.Equal(3, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.Equal(3, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenMangaDexReturnsPartialMap_UnmappedChaptersRemainNull()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test MangaDex Partial", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "3", null, "Title 3") { Downloaded = true, FileName = "chap3.cbz" });
        await _mangaContext.SaveChangesAsync();

        var resolver = new Mock<IMangaDexVolumeResolver>();
        resolver.Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 1 }, { "2", 1 } });

        await Resolve(settings, manga.Key, resolver.Object);

        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "3")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenMangaDexReturnsEmptyMap_FallsBackToHeuristic()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Fallback", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));
        CreateGrayscaleCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key);

        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenConsecutiveColorChapters_AbortsHeuristic()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Consecutive", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));
        CreateColorCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key);

        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenGrayscaleContinuationAfterExistingVolume_AssignedToCurrentVolume()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        string dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<SeriesContext>().UseInMemoryDatabase(dbName).Options;

        FileLibrary library;
        Series manga;
        using (var setupContext = new SeriesContext(options))
        {
            library = new FileLibrary(_testRoot, "Test Library");
            setupContext.FileLibraries.Add(library);
            manga = new Series("Test Grayscale Continuation", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            setupContext.Series.Add(manga);
            setupContext.Chapters.Add(new Chapter(manga, "1", 10, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
            setupContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
            await setupContext.SaveChangesAsync();
        }

        using var workerContext = new SeriesContext(options);

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateGrayscaleCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key, context: workerContext);

        Assert.Equal(10, (await workerContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task Resolve_WhenVolumesUpdated_UpdatesVolumeNumber()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Moves", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));

        await Resolve(settings, manga.Key);

        // Resolution only updates the DB; the file move is the placement reconciler's job.
        Assert.NotNull((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenZipHasNoImages_SkipsChapterAndTreatsNextAsFirst()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Empty Zip", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        using (var zip = ZipFile.Open(Path.Combine(mangaDir, "chap1.cbz"), ZipArchiveMode.Create))
            zip.CreateEntry("readme.txt");
        CreateColorCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key);

        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenExactOnlyAndMangaDexReturnsEmpty_ChaptersRemainNull()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Exact Empty", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));

        await Resolve(settings, manga.Key);

        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenCorruptZipAfterVolumeEstablished_AssignsCurrentVolume()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Corrupt Zip", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));
        await File.WriteAllBytesAsync(Path.Combine(mangaDir, "chap2.cbz"), [0x00, 0x01, 0x02, 0x03]);

        await Resolve(settings, manga.Key);

        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenChapterNotDownloaded_ColorHeuristicSkipsIt()
    {
        // The color heuristic reads the downloaded .cbz, so it can't place a chapter that isn't
        // downloaded. With no exact source to cover it, the chapter stays unresolved.
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Not Downloaded", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = false, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        await Resolve(settings, manga.Key);

        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_AssignsExactVolume_ToChapterNotYetDownloaded()
    {
        // Exact sources map chapter number -> volume and need no files, so resolution must assign
        // volumes even to chapters that aren't downloaded yet — the full volume layout should be
        // visible before the series finishes downloading.
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Undownloaded Exact", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = false });
        await _mangaContext.SaveChangesAsync();

        var resolver = new Mock<IMangaDexVolumeResolver>();
        resolver.Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 3 } });

        await Resolve(settings, manga.Key, resolver.Object);

        Assert.Equal(3, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WithMultipleMangaInQueue_EachProcessedIndependently()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga1 = new Series("Test Multi One", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        var manga2 = new Series("Test Multi Two", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.AddRange(manga1, manga2);
        _mangaContext.Chapters.Add(new Chapter(manga1, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga2, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        foreach (var manga in new[] { manga1, manga2 })
        {
            string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
            Directory.CreateDirectory(mangaDir);
            CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));
        }

        await ResolveAll(settings, new[] { manga1.Key, manga2.Key });

        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ParentMangaId == manga1.Key)).VolumeNumber);
        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ParentMangaId == manga2.Key)).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenCoverNamedCoverJpg_ColorCoverDetected()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Cover Name", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        using (var zip = ZipFile.Open(Path.Combine(mangaDir, "chap1.cbz"), ZipArchiveMode.Create))
        {
            WriteColorImage(zip, "cover.jpg");
            WriteGrayscaleImage(zip, "001.jpg");
        }

        await Resolve(settings, manga.Key);

        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenMangaDexThrowsException_FallsBackToColorHeuristic()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Exception Fallback", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));

        var throwingResolver = new Mock<IMangaDexVolumeResolver>();
        throwingResolver
            .Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("MangaDex unavailable"));

        await Resolve(settings, manga.Key, throwingResolver.Object);

        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenMangaDexMapHasNoMatchingChapters_FallsBackToHeuristic()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test No Match Fallback", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "50", null, "Title") { Downloaded = true, FileName = "chap50.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "51", null, "Title") { Downloaded = true, FileName = "chap51.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap50.cbz"));
        CreateGrayscaleCbz(Path.Combine(mangaDir, "chap51.cbz"));

        // MangaDex map is non-empty but contains chapter numbers that don't match ours
        var resolver = new Mock<IMangaDexVolumeResolver>();
        resolver
            .Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["1"] = 1, ["2"] = 1 });

        await Resolve(settings, manga.Key, resolver.Object);

        // Mapped=0 → resolvedExact=false → heuristic runs → color cover assigns vol 1
        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "50")).VolumeNumber);
        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "51")).VolumeNumber);
    }

    // ─── MetadataConfidence tests ────────────────────────────────────────────

    [Fact]
    public async Task DoWork_WhenStatusConfirmed_AssignsExactConfidenceToChapters()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Exact Confidence", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.ExternalId = "confirmed-uuid";
        manga.MetadataSource.Status = MetadataSourceStatus.Confirmed;
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        var resolver = new Mock<IMangaDexVolumeResolver>();
        resolver.Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 1 }, { "2", 1 } });

        await Resolve(settings, manga.Key, resolver.Object);

        var ch1 = await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1");
        var ch2 = await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2");
        Assert.Equal(MetadataConfidence.Exact, ch1.MetadataConfidence);
        Assert.Equal(MetadataConfidence.Exact, ch2.MetadataConfidence);
    }

    [Fact]
    public async Task DoWork_WhenHeuristicUsed_AssignsHeuristicConfidenceToChapters()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Heuristic Confidence", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        // Status remains Unlinked but search returns nothing → heuristic fallback
        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>());
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz"));
        CreateGrayscaleCbz(Path.Combine(mangaDir, "chap2.cbz"));

        await Resolve(settings, manga.Key);

        var ch1 = await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1");
        var ch2 = await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2");
        Assert.Equal(MetadataConfidence.Heuristic, ch1.MetadataConfidence);
        Assert.Equal(MetadataConfidence.Heuristic, ch2.MetadataConfidence);
    }

    // ─── Auto-match tests ────────────────────────────────────────────────────

    [Fact]
    public async Task DoWork_WhenNoMatchButHasAniListLink_ReMatchesByIdOverHigherScoringDecoy()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        // Previously NoMatch, but now carries an AniList link (e.g. backfilled from the connector page).
        var manga = new Series("Berserk", "Desc", "url", SeriesReleaseStatus.Continuing, [], [],
            [new Link("AniList", "https://anilist.co/manga/87170")], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.NoMatch;
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        // Decoy wins title (exact) + chapter count; the true entry only matches by AniList id.
        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>
            {
                new() { MangaDexId = "decoy-uuid", Title = "Berserk", ChapterCount = 1, AniListId = "999" },
                new() { MangaDexId = "true-uuid", Title = "Berserk Deluxe", ChapterCount = 999, AniListId = "87170" }
            });
        _mockSearchService
            .Setup(s => s.GetChapterToVolumeMapAsync("true-uuid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 1 } });

        await Resolve(settings, manga.Key);

        var updatedSource = await _mangaContext.Set<MetadataSource>().FirstAsync(s => s.MangaId == manga.Key);
        Assert.Equal(MetadataSourceStatus.AutoMatched, updatedSource.Status);
        Assert.Equal("true-uuid", updatedSource.ExternalId);
    }

    [Fact]
    public async Task DoWork_WhenStatusUnlinked_AttemptsAutoMatch_AndSetsAutoMatchedOnStrongCandidate()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        // Series with 2 chapters — chapter count matches the search result
        var manga = new Series("Berserk", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", null, "Title 2") { Downloaded = true, FileName = "chap2.cbz" });
        await _mangaContext.SaveChangesAsync();

        // Strong candidate: high title similarity, chapter count matches
        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>
            {
                new() { MangaDexId = "berserk-uuid", Title = "Berserk", Author = null, ChapterCount = 2 }
            });
        _mockSearchService
            .Setup(s => s.GetChapterToVolumeMapAsync("berserk-uuid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 1 }, { "2", 1 } });

        await Resolve(settings, manga.Key);

        var updatedSource = await _mangaContext.Set<MetadataSource>().FirstAsync(s => s.MangaId == manga.Key);
        Assert.Equal(MetadataSourceStatus.AutoMatched, updatedSource.Status);
        Assert.Equal("berserk-uuid", updatedSource.ExternalId);
        Assert.NotNull(updatedSource.MatchScore);

        var ch1 = await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1");
        Assert.Equal(1, ch1.VolumeNumber);
        Assert.Equal(MetadataConfidence.Exact, ch1.MetadataConfidence);
    }

    [Fact]
    public async Task DoWork_WhenAutoMatchScoreBelowThreshold_SetsNoMatch()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("XYZ Series", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        // Return a candidate with very low title similarity
        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>
            {
                new() { MangaDexId = "some-uuid", Title = "Completely Different Title ABCDEF", Author = null, ChapterCount = 999 }
            });

        await Resolve(settings, manga.Key);

        var updatedSource = await _mangaContext.Set<MetadataSource>().FirstAsync(s => s.MangaId == manga.Key);
        Assert.Equal(MetadataSourceStatus.NoMatch, updatedSource.Status);
        Assert.Null(updatedSource.ExternalId);

        // No volumes should be assigned
        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_AutoMatch_DoesNotOverwriteManualAssignment()
    {
        // Regression: the auto-match volume application must respect the manual floor. If the matched
        // aggregate happens to cover a manually-pinned chapter, the manual assignment must survive.
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Berserk", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        _mangaContext.Chapters.Add(new Chapter(manga, "2", 99, "Title 2")
            { Downloaded = true, FileName = "chap2.cbz", MetadataConfidence = MetadataConfidence.Manual });
        await _mangaContext.SaveChangesAsync();

        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>
            {
                new() { MangaDexId = "berserk-uuid", Title = "Berserk", Author = null, ChapterCount = 2 }
            });
        // The aggregate covers BOTH chapters, including the manually-pinned one.
        _mockSearchService
            .Setup(s => s.GetChapterToVolumeMapAsync("berserk-uuid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 1 }, { "2", 1 } });

        await Resolve(settings, manga.Key);

        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
        var ch2 = await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "2");
        Assert.Equal(99, ch2.VolumeNumber);                              // manual value, not the aggregate's 1
        Assert.Equal(MetadataConfidence.Manual, ch2.MetadataConfidence);
    }

    [Fact]
    public async Task DoWork_WhenAutoMatchAmbiguous_SetsAmbiguous()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        // Use a title that produces similar scores for two candidates
        var manga = new Series("Berserk", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        // Two candidates with very close scores (both identical title, same chapter count)
        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>
            {
                new() { MangaDexId = "candidate-a", Title = "Berserk", Author = null, ChapterCount = 1 },
                new() { MangaDexId = "candidate-b", Title = "Berserk", Author = null, ChapterCount = 1 }
            });

        await Resolve(settings, manga.Key);

        var updatedSource = await _mangaContext.Set<MetadataSource>().FirstAsync(s => s.MangaId == manga.Key);
        Assert.Equal(MetadataSourceStatus.Ambiguous, updatedSource.Status);
        Assert.Null(updatedSource.ExternalId);
        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenAutoMatchSucceedsButVolumeFetchFails_RollsBackToUnlinked()
    {
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Berserk", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        // Strong match candidate
        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>
            {
                new() { MangaDexId = "berserk-uuid", Title = "Berserk", Author = null, ChapterCount = 1 }
            });
        // But volume fetch returns empty
        _mockSearchService
            .Setup(s => s.GetChapterToVolumeMapAsync("berserk-uuid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int>());

        await Resolve(settings, manga.Key);

        var updatedSource = await _mangaContext.Set<MetadataSource>().FirstAsync(s => s.MangaId == manga.Key);
        Assert.Equal(MetadataSourceStatus.Unlinked, updatedSource.Status);
        Assert.Null(updatedSource.ExternalId);
        Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenSoleCandidateHasNoChapterCount_AutoMatchesOnTitle()
    {
        // MangaDex leaves lastChapter empty for ongoing series (e.g. Dandadan), so the candidate's
        // ChapterCount is 0. A perfect, sole title match must still auto-match — not fall to NoMatch.
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Dandadan", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        manga.MetadataSource!.Status = MetadataSourceStatus.Unlinked;
        _mangaContext.Series.Add(manga);
        _mangaContext.Chapters.Add(new Chapter(manga, "1", null, "Title 1") { Downloaded = true, FileName = "chap1.cbz" });
        await _mangaContext.SaveChangesAsync();

        _mockSearchService
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MangaDexSearchResult>
            {
                new() { MangaDexId = "dandadan-uuid", Title = "Dandadan", Author = null, ChapterCount = 0 }
            });
        _mockSearchService
            .Setup(s => s.GetChapterToVolumeMapAsync("dandadan-uuid", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { { "1", 1 } });

        await Resolve(settings, manga.Key);

        var updatedSource = await _mangaContext.Set<MetadataSource>().FirstAsync(s => s.MangaId == manga.Key);
        Assert.Equal(MetadataSourceStatus.AutoMatched, updatedSource.Status);
        Assert.Equal("dandadan-uuid", updatedSource.ExternalId);
        Assert.Equal(1, (await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == "1")).VolumeNumber);
    }

    [Fact]
    public async Task DoWork_WhenHeuristicVolumeExceedsPlausibleSize_LeavesChaptersUnassigned()
    {
        // A single color cover followed by a long run of black-and-white covers (the Dandadan vol-17
        // failure) must NOT dump every chapter into one giant volume. The heuristic should abort and
        // leave them unassigned rather than fabricate a bogus volume.
        var settings = new KenkuSettings { VolumeResolutionStrategy = VolumeResolutionStrategy.ExactThenGuess };
        var library = new FileLibrary(_testRoot, "Test Library");
        _mangaContext.FileLibraries.Add(library);
        var manga = new Series("Test Bloat", "Desc", "url", SeriesReleaseStatus.Continuing, [], [], [], [], library);
        _mangaContext.Series.Add(manga);

        const int chapterCount = 45; // > MaxPlausibleVolumeChapters (40)
        for (int i = 1; i <= chapterCount; i++)
            _mangaContext.Chapters.Add(new Chapter(manga, i.ToString(), null, $"Title {i}")
                { Downloaded = true, FileName = $"chap{i}.cbz" });
        await _mangaContext.SaveChangesAsync();

        string mangaDir = Path.Combine(_testRoot, manga.DirectoryName);
        Directory.CreateDirectory(mangaDir);
        CreateColorCbz(Path.Combine(mangaDir, "chap1.cbz")); // the one color cover
        for (int i = 2; i <= chapterCount; i++)
            CreateGrayscaleCbz(Path.Combine(mangaDir, $"chap{i}.cbz"));

        await Resolve(settings, manga.Key);

        // None of the bloated run should be assigned a volume.
        foreach (var num in new[] { "1", "20", "45" })
            Assert.Null((await _mangaContext.Chapters.FirstAsync(c => c.ChapterNumber == num)).VolumeNumber);
    }

    private static void CreateColorCbz(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteColorImage(zip, "01.jpg");
    }

    private static void CreateGrayscaleCbz(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteGrayscaleImage(zip, "01.jpg");
    }

    private static void WriteColorImage(ZipArchive zip, string name)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(10, 10);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgb24(255, 0, 0);
        img.SaveAsJpeg(stream);
    }

    private static void WriteGrayscaleImage(ZipArchive zip, string name)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(10, 10);
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                img[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgb24(128, 128, 128);
        img.SaveAsJpeg(stream);
    }
}

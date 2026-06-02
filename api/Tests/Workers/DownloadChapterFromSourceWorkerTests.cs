using API.MangaConnectors;
using API.HttpRequesters;
using API.Schema.SeriesContext;
using API.Workers;
using API.Workers.MangaDownloadWorkers;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace API.Tests.Workers;

public class DownloadChapterFromSourceWorkerTests
{
    /// <summary>A MemoryStream that records whether it was disposed.</summary>
    private sealed class TrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }

        public TrackingStream(byte[] buffer) : base(buffer.Length)
        {
            Write(buffer, 0, buffer.Length);
            Position = 0;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return base.DisposeAsync();
        }
    }

    private static byte[] CreateJpegBytes()
    {
        using var image = new Image<Rgba32>(8, 8);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task DoWorkInternal_DisposesSourceImageStream_AfterProcessing()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kenku-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var settings = new KenkuSettings { AppData = tempRoot, ImageCompression = 40 }; // 40 => processing path
            var libraryPath = Path.Combine(tempRoot, "library");
            Directory.CreateDirectory(libraryPath);

            var options = new DbContextOptionsBuilder<SeriesContext>()
                .UseInMemoryDatabase(databaseName: "DownloadWorkerDispose-" + Guid.NewGuid().ToString("N"))
                .Options;
            using var context = new SeriesContext(options);

            var library = new FileLibrary(libraryPath, "Test Lib");
            context.FileLibraries.Add(library);
            var manga = new Series("Test Series", "Desc", "http://cover.com/c.jpg", SeriesReleaseStatus.Continuing,
                new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
                library, 0f, 2024, "en");

            // Provide a cached cover so EnsureCoverInPublicationFolder does not make a network request.
            Directory.CreateDirectory(settings.CoverImageCacheOriginal);
            string cachedCover = "cached-cover.jpg";
            await File.WriteAllBytesAsync(Path.Combine(settings.CoverImageCacheOriginal, cachedCover), CreateJpegBytes());
            manga.CoverFileNameInCache = cachedCover;

            context.Series.Add(manga);
            var chapter = new Chapter(manga, "1", null, "Title");
            context.Chapters.Add(chapter);
            var connectorId = new SourceId<Chapter>(chapter, "MockConnector", "site1", "url1", true);
            context.MangaConnectorToChapter.Add(connectorId);
            await context.SaveChangesAsync();

            var mockConnector = new Mock<SeriesSource>("MockConnector", new[] { "en" }, new[] { "mock.com" }, "icon", settings);
            mockConnector.Setup(c => c.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>()))
                .ReturnsAsync(["http://img/1.jpg"]);

            var sourceStream = new TrackingStream(CreateJpegBytes());
            mockConnector.Setup(c => c.DownloadImage(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(sourceStream);

            var services = new ServiceCollection();
            services.AddSingleton(context);
            services.AddDbContext<API.Schema.ActionsContext.ActionsContext>(o => o.UseInMemoryDatabase("Actions-" + Guid.NewGuid().ToString("N")));
            services.AddDbContext<API.Schema.NotificationsContext.NotificationsContext>(o => o.UseInMemoryDatabase("Notifications-" + Guid.NewGuid().ToString("N")));
            var serviceProvider = services.BuildServiceProvider();

            var worker = new DownloadChapterFromSourceWorker(connectorId, new[] { mockConnector.Object }, settings);

            await worker.DoWork(serviceProvider.CreateScope());

            var updated = await context.Chapters.FirstAsync(c => c.Key == chapter.Key);
            Assert.True(updated.Downloaded, "Chapter should be marked downloaded on the happy path.");
            Assert.True(sourceStream.Disposed, "The source image stream returned by DownloadImage must be disposed (no leak).");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DoWorkInternal_OnFailure_DoesNotMarkAsDownloaded()
    {
        // 1. Setup - Create a real DB but mock the Connector
        var options = new DbContextOptionsBuilder<SeriesContext>()
            .UseInMemoryDatabase(databaseName: "DownloadWorkerFailure")
            .Options;

        using var context = new SeriesContext(options);
        
        var library = new FileLibrary("/tmp/manga", "Test Lib");
        context.FileLibraries.Add(library);
        
        var manga = new Series("Test Series", "Desc", "http://cover.com", SeriesReleaseStatus.Continuing, 
            new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
            library, 0f, 2024, "en");
        context.Series.Add(manga);
        
        var chapter = new Chapter(manga, "1", null, "Title");
        context.Chapters.Add(chapter);
        
        var connectorId = new SourceId<Chapter>(chapter, "MockConnector", "site1", "url1", true);
        context.MangaConnectorToChapter.Add(connectorId);
        await context.SaveChangesAsync();

        var settings = new KenkuSettings { AppData = "/tmp", ChapterNamingScheme = "%M - %C" };
        
        var mockConnector = new Mock<SeriesSource>("MockConnector", new[] { "en" }, new[] { "mock.com" }, "icon", settings);
        
        // Simulate a crash during image URL retrieval
        mockConnector.Setup(c => c.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>()))
            .ThrowsAsync(new Exception("Network failure during image retrieval"));

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddDbContext<API.Schema.ActionsContext.ActionsContext>(o => o.UseInMemoryDatabase("Actions"));
        services.AddDbContext<API.Schema.NotificationsContext.NotificationsContext>(o => o.UseInMemoryDatabase("Notifications"));

        var serviceProvider = services.BuildServiceProvider();

        var worker = new DownloadChapterFromSourceWorker(connectorId, new[] { mockConnector.Object }, settings);
        
        // 2. Act - Try to download
        await worker.DoWork(serviceProvider.CreateScope());

        // 3. Assert - The chapter should NOT be marked as downloaded
        var updatedChapter = await context.Chapters.FirstAsync(c => c.Key == chapter.Key);
        Assert.False(updatedChapter.Downloaded, "Chapter should NOT be marked as downloaded after a failure.");
    }

    [Fact]
    public async Task DoWorkInternal_VolumeFolderLayout_WritesChapterIntoVolumeSubfolder()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kenku-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var settings = new KenkuSettings { AppData = tempRoot, ImageCompression = 100 }; // 100 => no re-encode
            var libraryPath = Path.Combine(tempRoot, "library");
            Directory.CreateDirectory(libraryPath);

            var options = new DbContextOptionsBuilder<SeriesContext>()
                .UseInMemoryDatabase("DownloadWorkerLayout-" + Guid.NewGuid().ToString("N"))
                .Options;
            using var context = new SeriesContext(options);

            var library = new FileLibrary(libraryPath, "Test Lib");
            context.FileLibraries.Add(library);
            var manga = new Series("Test Series", "Desc", "http://cover.com/c.jpg", SeriesReleaseStatus.Continuing,
                new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
                library, 0f, 2024, "en");
            manga.LibraryLayout = LibraryLayout.VolumeFolder;
            context.Series.Add(manga);

            var chapter = new Chapter(manga, "1", 5, "Title");
            context.Chapters.Add(chapter);
            var connectorId = new SourceId<Chapter>(chapter, "MockConnector", "site1", "url1", true);
            context.MangaConnectorToChapter.Add(connectorId);
            await context.SaveChangesAsync();

            var mockConnector = new Mock<SeriesSource>("MockConnector", new[] { "en" }, new[] { "mock.com" }, "icon", settings);
            mockConnector.Setup(c => c.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>()))
                .ReturnsAsync(["http://img/1.jpg"]);
            mockConnector.Setup(c => c.DownloadImage(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(CreateJpegBytes()));

            var services = new ServiceCollection();
            services.AddSingleton(context);
            services.AddDbContext<API.Schema.ActionsContext.ActionsContext>(o => o.UseInMemoryDatabase("Actions-" + Guid.NewGuid().ToString("N")));
            services.AddDbContext<API.Schema.NotificationsContext.NotificationsContext>(o => o.UseInMemoryDatabase("Notifications-" + Guid.NewGuid().ToString("N")));
            var serviceProvider = services.BuildServiceProvider();

            var worker = new DownloadChapterFromSourceWorker(connectorId, new[] { mockConnector.Object }, settings);
            await worker.DoWork(serviceProvider.CreateScope());

            string expectedFile = chapter.GetArchiveFileName(settings.ChapterNamingScheme);
            string expectedRelative = Path.Join("Vol 5", expectedFile);
            string expectedFullPath = Path.Combine(libraryPath, manga.DirectoryName, "Vol 5", expectedFile);

            var updated = await context.Chapters.FirstAsync(c => c.Key == chapter.Key);
            Assert.True(updated.Downloaded, "Chapter should be marked downloaded.");
            Assert.Equal(expectedRelative, updated.FileName);
            Assert.True(File.Exists(expectedFullPath), $"Expected chapter archive at {expectedFullPath}");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task DoWorkInternal_VolumeCBZ_WhenClosedVolumeCompletes_QueuesBundleWorker()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kenku-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var settings = new KenkuSettings { AppData = tempRoot, ImageCompression = 100 };
            var libraryPath = Path.Combine(tempRoot, "library");
            Directory.CreateDirectory(libraryPath);

            var options = new DbContextOptionsBuilder<SeriesContext>()
                .UseInMemoryDatabase("DownloadWorkerBundle-" + Guid.NewGuid().ToString("N"))
                .Options;
            using var context = new SeriesContext(options);

            var library = new FileLibrary(libraryPath, "Test Lib");
            context.FileLibraries.Add(library);
            var manga = new Series("Test Series", "Desc", "http://cover.com/c.jpg", SeriesReleaseStatus.Continuing,
                new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
                library, 0f, 2024, "en");
            manga.LibraryLayout = LibraryLayout.VolumeCBZ;
            context.Series.Add(manga);

            // Volume 1: one chapter already downloaded, the other is the one we download now.
            var ch1 = new Chapter(manga, "1", 1, null) { Downloaded = true, FileName = "Vol 1/ch1.cbz" };
            var ch2 = new Chapter(manga, "2", 1, null);
            // Volume 2 exists (not downloaded) → volume 1 is "closed".
            var ch3 = new Chapter(manga, "3", 2, null);
            context.Chapters.AddRange(ch1, ch2, ch3);
            var connectorId = new SourceId<Chapter>(ch2, "MockConnector", "site2", "url2", true);
            context.MangaConnectorToChapter.Add(connectorId);
            await context.SaveChangesAsync();

            var mockConnector = new Mock<SeriesSource>("MockConnector", new[] { "en" }, new[] { "mock.com" }, "icon", settings);
            mockConnector.Setup(c => c.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>()))
                .ReturnsAsync(["http://img/1.jpg"]);
            mockConnector.Setup(c => c.DownloadImage(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(CreateJpegBytes()));

            var services = new ServiceCollection();
            services.AddSingleton(context);
            services.AddDbContext<API.Schema.ActionsContext.ActionsContext>(o => o.UseInMemoryDatabase("Actions-" + Guid.NewGuid().ToString("N")));
            services.AddDbContext<API.Schema.NotificationsContext.NotificationsContext>(o => o.UseInMemoryDatabase("Notifications-" + Guid.NewGuid().ToString("N")));
            var serviceProvider = services.BuildServiceProvider();

            var worker = new DownloadChapterFromSourceWorker(connectorId, new[] { mockConnector.Object }, settings);
            BaseWorker[] created = await worker.DoWork(serviceProvider.CreateScope());

            var bundle = created.OfType<BundleVolumeWorker>().FirstOrDefault();
            Assert.NotNull(bundle);
            Assert.Equal(manga.Key, bundle!.MangaId);
            Assert.Equal(1, bundle.VolumeNumber);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}

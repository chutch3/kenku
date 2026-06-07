using System.IO.Compression;
using API.Acquirers;
using API.Connectors;
using API.Schema.SeriesContext;
using Moq;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace API.Tests.Unit.Acquirers;

public class ImageListAcquirerTests
{
    /// <summary>A stream that throws when read, simulating a network read cancelled mid-transfer.</summary>
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }
        public override int Read(byte[] buffer, int offset, int count) => throw new OperationCanceledException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new OperationCanceledException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static byte[] CreateJpegBytes()
    {
        using var image = new Image<Rgba32>(8, 8);
        using var ms = new MemoryStream();
        image.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    private static (SourceId<Chapter> chapter, KenkuSettings settings) NewChapter(string tempRoot)
    {
        var settings = new KenkuSettings { AppData = tempRoot, ImageCompression = 100 };
        var library = new FileLibrary(Path.Combine(tempRoot, "library"), "Test Lib");
        var manga = new Series("Test Series", "Desc", "http://cover.com/c.jpg", SeriesReleaseStatus.Continuing,
            new List<Author>(), new List<SeriesTag>(), new List<Link>(), new List<AltTitle>(),
            library, 0f, 2024, "en");
        var chapter = new Chapter(manga, "1", null, "Title");
        return (new SourceId<Chapter>(chapter, "MockConnector", "site1", "url1", true), settings);
    }

    [Fact]
    public async Task AcquireAsync_AllImagesFetched_WritesValidCbzAtFinalPath()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kenku-acq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            (SourceId<Chapter> chapter, KenkuSettings settings) = NewChapter(tempRoot);
            string finalPath = Path.Combine(tempRoot, "chapter.cbz");

            var source = new Mock<SeriesSource>("MockConnector", new[] { "en" }, new[] { "mock.com" }, "icon", settings);
            source.Setup(s => s.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>()))
                .ReturnsAsync(["u1", "u2", "u3"]);
            source.Setup(s => s.DownloadImage(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(CreateJpegBytes()));

            var acquirer = new ImageListAcquirer(settings);
            string? result = await acquirer.AcquireAsync(chapter, source.Object, finalPath, CancellationToken.None);

            Assert.Equal(finalPath, result);
            Assert.True(File.Exists(finalPath));
            Assert.False(File.Exists(finalPath + ".part"), "the temp file must be moved into place, not left behind");

            using ZipArchive archive = ZipFile.OpenRead(finalPath);
            Assert.Equal(3, archive.Entries.Count(e => e.FullName.EndsWith(".jpg")));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task AcquireAsync_FailsMidWrite_LeavesNoFileAtFinalPath()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "kenku-acq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            (SourceId<Chapter> chapter, KenkuSettings settings) = NewChapter(tempRoot);
            string finalPath = Path.Combine(tempRoot, "chapter.cbz");

            var source = new Mock<SeriesSource>("MockConnector", new[] { "en" }, new[] { "mock.com" }, "icon", settings);
            source.Setup(s => s.GetChapterImageUrls(It.IsAny<SourceId<Chapter>>()))
                .ReturnsAsync(["good", "poison"]);
            source.Setup(s => s.DownloadImage("good", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MemoryStream(CreateJpegBytes()));
            source.Setup(s => s.DownloadImage("poison", It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new ThrowingStream());

            var acquirer = new ImageListAcquirer(settings);
            string? result = await acquirer.AcquireAsync(chapter, source.Object, finalPath, CancellationToken.None);

            Assert.Null(result);
            Assert.False(File.Exists(finalPath), "a mid-write failure must never leave a partial .cbz at the final path");
            Assert.False(File.Exists(finalPath + ".part"), "the temp file must be cleaned up on failure");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}

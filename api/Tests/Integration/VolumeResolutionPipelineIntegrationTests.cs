using System.Collections.Concurrent;
using API;
using API.Schema.SeriesContext;
using API.Services;
using API.Workers.MaintenanceWorkers;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace API.Tests.Integration;

/// <summary>
/// Drives the real <see cref="ResolveMissingVolumesForMangaWorker"/> end-to-end against the shared
/// in-memory store, in a fresh scope from the one that seeded it — so the worker's own queries (and the
/// merge it performs) are exercised, not in-memory object references. Mocks only the network sources.
/// </summary>
[Trait("Category", "Integration")]
public class VolumeResolutionPipelineIntegrationTests : IDisposable
{
    private readonly IntegrationHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>An <see cref="IVolumeResolver"/> that returns a fixed map, standing in for Wikipedia.</summary>
    private sealed class FakeResolver(Dictionary<string, int> map) : IVolumeResolver
    {
        public string SourceName => "Fake";
        public MetadataConfidence Confidence => MetadataConfidence.Exact;
        public Task<IReadOnlyDictionary<string, int>> ResolveAsync(
            Series manga, IReadOnlyList<Chapter> chapters, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, int>>(map);
    }

    [Fact]
    public async Task ExactSourcesMerge_OverwriteStaleHeuristic_AndRespectManualLock()
    {
        _harness.Settings.VolumeResolutionStrategy = VolumeResolutionStrategy.ExactOnly;

        string mangaKey = null!;
        await _harness.Seed(async ctx =>
        {
            var library = new FileLibrary(_harness.TempDir, "Lib");
            ctx.FileLibraries.Add(library);
            var manga = new Series("Test", "d", "u", SeriesReleaseStatus.Continuing, [], [], [], [], library);
            // Already linked, so the worker skips auto-match and goes straight to exact resolution.
            manga.MetadataSource!.Status = MetadataSourceStatus.AutoMatched;
            manga.MetadataSource.ExternalId = "external-id";
            ctx.Series.Add(manga);

            ctx.Chapters.Add(new Chapter(manga, "1", null, null) { Downloaded = true, FileName = "ch1.cbz" });
            ctx.Chapters.Add(new Chapter(manga, "2", null, null) { Downloaded = true, FileName = "ch2.cbz" });
            // Manually pinned — must survive resolution untouched.
            ctx.Chapters.Add(new Chapter(manga, "3", 99, null)
                { Downloaded = true, FileName = "ch3.cbz", MetadataConfidence = MetadataConfidence.Manual });
            // A stale heuristic guess — must be corrected by an exact source.
            ctx.Chapters.Add(new Chapter(manga, "4", 5, null)
                { Downloaded = true, FileName = "ch4.cbz", MetadataConfidence = MetadataConfidence.Heuristic });

            mangaKey = manga.Key;
        });

        // MangaDex covers ch 1–2; the Fake (Wikipedia-like) source covers ch 3–4 — and disagrees with
        // the manual pin on ch 3 and the stale guess on ch 4.
        var mangaDex = new Mock<IMangaDexVolumeResolver>();
        mangaDex.Setup(r => r.GetChapterToVolumeMapAsync(It.IsAny<Series>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int> { ["1"] = 1, ["2"] = 1 });
        var search = new Mock<IMangaDexSearchService>();

        var worker = new ResolveMissingVolumesForMangaWorker(
            new ConcurrentQueue<string>([mangaKey]), _harness.Settings, mangaDex.Object, search.Object,
            new IVolumeResolver[] { new FakeResolver(new() { ["3"] = 1, ["4"] = 6 }) });

        await worker.DoWork(_harness.CreateScope());

        var byNumber = await _harness.Query(ctx =>
            ctx.Chapters.ToDictionaryAsync(c => c.ChapterNumber, c => c));

        Assert.Equal(1, byNumber["1"].VolumeNumber);                                  // filled from MangaDex
        Assert.Equal(MetadataConfidence.Exact, byNumber["1"].MetadataConfidence);
        Assert.Equal(1, byNumber["2"].VolumeNumber);
        Assert.Equal(99, byNumber["3"].VolumeNumber);                                 // manual lock held
        Assert.Equal(MetadataConfidence.Manual, byNumber["3"].MetadataConfidence);
        Assert.Equal(6, byNumber["4"].VolumeNumber);                                  // stale heuristic corrected
        Assert.Equal(MetadataConfidence.Exact, byNumber["4"].MetadataConfidence);
    }
}

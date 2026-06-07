using API.JobRuntime.Reconcilers;
using API.JobRuntime;
using API.JobRuntime.Handlers;
using Xunit;

namespace API.Tests.JobRuntime;

/// <summary>
/// The reconciler that replaced UpdateChaptersDownloaded: it enqueues a single deduped
/// VerifyDownloadState job to reconcile Downloaded flags with disk.
/// </summary>
public class DownloadStateReconcilerTests
{
    [Fact]
    public async Task Enqueue_AddsOneVerifyDownloadStateJob()
    {
        var store = new InMemoryJobStore();

        await DownloadStateReconciler.EnqueueAsync(store, DateTime.UtcNow, default);

        var job = Assert.Single(await store.GetAllAsync());
        Assert.Equal(VerifyDownloadStateHandler.Type, job.Type);
    }

    [Fact]
    public async Task Enqueue_IsDeduped_SoTicksDoNotPileUp()
    {
        var store = new InMemoryJobStore();

        await DownloadStateReconciler.EnqueueAsync(store, DateTime.UtcNow, default);
        await DownloadStateReconciler.EnqueueAsync(store, DateTime.UtcNow, default);

        Assert.Single(await store.GetAllAsync());
    }
}

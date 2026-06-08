using API.JobRuntime;
using API.Schema.JobsContext;
using Xunit;
using JobEntity = API.Schema.JobsContext.Job;

namespace API.Tests.Unit.JobRuntime;

public class InMemoryJobStoreTests
{
    [Fact]
    public async Task Delete_RemovesTheJob()
    {
        var store = new InMemoryJobStore();
        var job = await store.EnqueueAsync(new JobEntity("boom", "{}", DateTime.UtcNow));

        await store.DeleteAsync(job.Key);

        Assert.Null(await store.GetAsync(job.Key));
        Assert.Empty(await store.GetAllAsync());
    }
}

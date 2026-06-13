using API.JobRuntime.Handlers;
using Xunit;

namespace API.Tests.Unit.JobRuntime;

/// <summary>
/// The payload property names are a cross-boundary contract, not an implementation detail:
/// QueueList.vue parses <c>ChapterKey</c> out of the raw payload to open the download chooser, and
/// the OpenAPI spec only sees an opaque string — renaming would break the UI with every test green.
/// </summary>
public class DownloadChapterPayloadTests
{
    [Fact]
    public void PayloadFor_KeepsThePropertyNamesTheQueueUiParses()
    {
        string payload = DownloadChapterHandler.PayloadFor("src-1", "https://getcomics.org/dls/series");

        Assert.Contains("\"ChapterKey\":\"src-1\"", payload);
        Assert.Contains("\"PinnedArchiveUrl\":\"https://getcomics.org/dls/series\"", payload);
    }
}

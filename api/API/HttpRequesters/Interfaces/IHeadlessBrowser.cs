namespace API.HttpRequesters.Interfaces;

/// <summary>
/// Owns the shared headless-browser lifecycle, page pooling, and request policy. Connectors that need
/// a JavaScript-rendered DOM depend on this seam instead of constructing a browser themselves, so the
/// whole app shares one browser process and one global concurrency cap.
/// </summary>
public interface IHeadlessBrowser
{
    /// <summary>
    /// Navigates to <paramref name="url"/>, lets the page settle, and returns the rendered HTML.
    /// With <paramref name="waitForSelector"/>, settling means waiting until an element matching that CSS
    /// selector appears (the HTML is returned either way once the wait times out); without it, the page
    /// is scrolled to the bottom and given a fixed delay. Returns an empty string if the page could not
    /// be loaded. With <paramref name="session"/>, the page loads in that named session's context (sharing its
    /// cookies, e.g. a passed human check) instead of an isolated one.
    /// </summary>
    Task<string> RenderHtmlAsync(string url, string? referrer = null, string? waitForSelector = null,
        string? session = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens <paramref name="url"/> under the same isolation and policy as <see cref="RenderHtmlAsync"/> and runs
    /// <paramref name="script"/> against the loaded page, for sites whose content only appears through
    /// interaction (e.g. a virtualized list that mounts items as it scrolls). The page is torn down when the
    /// script returns. Throws <see cref="HttpRequestException"/> if the page could not be loaded. With
    /// <paramref name="session"/>, as for <see cref="RenderHtmlAsync"/>.
    /// </summary>
    Task<T> DrivePageAsync<T>(string url, Func<IBrowserPage, Task<T>> script, string? session = null,
        CancellationToken cancellationToken = default);
}

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
    /// be loaded.
    /// </summary>
    Task<string> RenderHtmlAsync(string url, string? referrer = null, string? waitForSelector = null,
        CancellationToken cancellationToken = default);
}

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
    /// Returns an empty string if the page could not be loaded.
    /// </summary>
    Task<string> RenderHtmlAsync(string url, string? referrer = null, CancellationToken cancellationToken = default);
}

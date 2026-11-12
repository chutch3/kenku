namespace API.HttpRequesters.Interfaces;

/// <summary>
/// The page-driving primitives a connector's script gets inside <see cref="IHeadlessBrowser.DrivePageAsync{T}"/>.
/// Deliberately minimal and kenku-owned, so no browser-library types cross the seam; add a primitive only when a
/// source needs it.
/// </summary>
public interface IBrowserPage
{
    /// <summary>Waits until an element matching <paramref name="selector"/> appears; false if it never did before the timeout.</summary>
    Task<bool> WaitForSelectorAsync(string selector);

    /// <summary>The <paramref name="attribute"/> of every element matching <paramref name="selector"/>, in DOM order
    /// (an empty string where the element lacks it).</summary>
    Task<IReadOnlyList<string>> QueryAttributesAsync(string selector, string attribute);

    /// <summary>
    /// Scrolls the element matching <paramref name="containerSelector"/> (the document when null) down by
    /// <paramref name="pixels"/>, waits <paramref name="settleMs"/> for lazily mounted content, and reports whether
    /// it has reached the bottom. A missing or non-scrollable container reports true.
    /// </summary>
    Task<bool> ScrollByAsync(string? containerSelector, int pixels, int settleMs);
}

/** Turn a PascalCase action name (e.g. "ChapterDownloaded") into readable words ("Chapter Downloaded").
 * Shared by the global Activity table and the per-series history so they read identically. */
export const humanizeAction = (action?: string | null) => (action ?? '').replace(/([a-z])([A-Z])/g, '$1 $2');

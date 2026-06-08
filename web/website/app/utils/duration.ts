/** Human-readable elapsed time for a millisecond span (e.g. "850ms", "1.2s", "3m 4s", "1h 2m"). */
export function formatDuration(ms: number): string {
    if (!Number.isFinite(ms) || ms < 0) ms = 0;
    if (ms < 1000) return `${Math.round(ms)}ms`;
    const seconds = ms / 1000;
    if (seconds < 60) return `${seconds.toFixed(1)}s`;
    const minutes = Math.floor(seconds / 60);
    if (minutes < 60) return `${minutes}m ${Math.round(seconds % 60)}s`;
    const hours = Math.floor(minutes / 60);
    return `${hours}h ${minutes % 60}m`;
}

/** Relative time from a past instant to now (e.g. "5s ago", "2m ago", "3h ago", "1d ago"). */
export function formatRelative(fromMs: number, nowMs: number): string {
    const diff = nowMs - fromMs;
    if (diff < 0) return 'just now';
    if (diff < 60_000) return `${Math.max(1, Math.round(diff / 1000))}s ago`;
    if (diff < 3_600_000) return `${Math.round(diff / 60_000)}m ago`;
    if (diff < 86_400_000) return `${Math.round(diff / 3_600_000)}h ago`;
    return `${Math.round(diff / 86_400_000)}d ago`;
}

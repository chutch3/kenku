import type { components } from '#open-fetch-schemas/api';

type QueuedJob = components['schemas']['QueuedJob'];

/** Owns the Activity queue: fetch, derived views, mutations, and polling. Polling is adaptive and
 * visibility-aware — fast (2s) while work is in flight, slow (15s) when idle, paused when the tab is
 * hidden. The live clock ticks only while a job is actually Running. The old component ran a 1s tick
 * plus a 2s poll forever, even in a background tab.
 *
 * Everything that needs the Nuxt/component context (useApi, lifecycle hooks) is registered before the
 * trailing await — calling them after an await would lose the context ("[nuxt] instance unavailable"). */
export async function useJobQueue() {
    const { $api } = useNuxtApp();
    const jobQuery = useApi('/v2/JobQueue', { key: FetchKeys.JobQueue.All, server: false });
    const seriesQuery = useApi('/v2/Series', { key: FetchKeys.Series.All, server: false });
    const { data, refresh } = jobQuery;
    const seriesData = seriesQuery.data;

    const jobs = computed<QueuedJob[]>(() => data.value ?? []);
    const seriesName = (job: QueuedJob) => seriesData.value?.find((s) => s.key === job.resourceKey)?.name;

    const now = ref(Date.now());
    const busy = ref<string | null>(null);

    const displayed = computed(() => displayedJobs(jobs.value));
    const attentionCount = computed(() => needsAttentionCount(jobs.value));
    const activeCount = computed(() => activeJobCount(jobs.value));
    const hasRunning = computed(() => jobs.value.some((j) => j.status === 'Running'));

    let tick: ReturnType<typeof setInterval> | undefined;
    let poll: ReturnType<typeof setInterval> | undefined;
    let pollMs = -1;

    const hidden = () => typeof document !== 'undefined' && document.visibilityState === 'hidden';
    const desiredPollMs = () => (hidden() ? 0 : activeCount.value > 0 ? 2000 : 15000);

    const sync = () => {
        const want = desiredPollMs();
        if (want !== pollMs) {
            pollMs = want;
            if (poll) clearInterval(poll), (poll = undefined);
            if (want > 0) poll = setInterval(refresh, want);
        }
        const wantTick = !hidden() && hasRunning.value;
        if (wantTick && !tick) tick = setInterval(() => (now.value = Date.now()), 1000);
        if (!wantTick && tick) clearInterval(tick), (tick = undefined);
    };

    onMounted(() => {
        sync();
        if (typeof document !== 'undefined') document.addEventListener('visibilitychange', sync);
    });
    watch([activeCount, hasRunning], sync);
    onBeforeUnmount(() => {
        if (tick) clearInterval(tick);
        if (poll) clearInterval(poll);
        if (typeof document !== 'undefined') document.removeEventListener('visibilitychange', sync);
    });

    const withBusy = async (jobId: string, action: () => Promise<unknown>) => {
        busy.value = jobId;
        try {
            await action();
            await refresh();
        } finally {
            busy.value = null;
        }
    };
    const retry = (jobId: string) => withBusy(jobId, () => $api('/v2/JobQueue/{JobId}/Retry', { method: 'POST', path: { JobId: jobId } }));
    const cancel = (jobId: string) => withBusy(jobId, () => $api('/v2/JobQueue/{JobId}/Cancel', { method: 'POST', path: { JobId: jobId } }));
    const dismiss = (jobId: string) => withBusy(jobId, () => $api('/v2/JobQueue/{JobId}/Dismiss', { method: 'POST', path: { JobId: jobId } }));

    // Suspend until both lists are loaded; context-sensitive calls are all above this line.
    await Promise.all([jobQuery, seriesQuery]);

    return { jobs, displayed, attentionCount, seriesName, now, busy, refresh, retry, cancel, dismiss };
}

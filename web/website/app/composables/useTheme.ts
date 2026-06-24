import { THEMES, getTheme } from '~/theme/registry';
import type { Seeds } from '~/theme/generate';

/** The id used for a user-built theme (not in the registry — its seeds live in their own cookie). */
export const CUSTOM_THEME_ID = 'custom';

/** The app-wide colour theme. Persisted in a cookie so it survives reloads and is SSR-stable (the
 * theme plugin renders `data-theme` server-side from it, so there's no flash); a shared useState
 * keeps every consumer in sync within a session. Orthogonal to light/dark, which stays on colorMode. */
export function useTheme() {
    const cookie = useCookie<string>('kenku-theme', { default: () => 'karasu', sameSite: 'lax' });
    const current = useState<string>('kenku-theme', () => cookie.value ?? 'karasu');

    const customCookie = useCookie<Seeds | null>('kenku-theme-custom', { default: () => null, sameSite: 'lax' });
    const customSeeds = useState<Seeds | null>('kenku-theme-custom', () => customCookie.value ?? null);

    const set = (id: string) => {
        // Catalogue ids must exist; the custom id is valid only once seeds have been saved.
        if (id !== CUSTOM_THEME_ID && !getTheme(id)) return;
        if (id === CUSTOM_THEME_ID && !customSeeds.value) return;
        current.value = id;
        cookie.value = id;
    };

    const setCustom = (seeds: Seeds) => {
        customSeeds.value = seeds;
        customCookie.value = seeds;
        current.value = CUSTOM_THEME_ID;
        cookie.value = CUSTOM_THEME_ID;
    };

    return { current, themes: THEMES, customSeeds, set, setCustom };
}

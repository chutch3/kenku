import { renderThemeCss } from '~/theme/generate';
import { CUSTOM_THEME_ID } from '~/composables/useTheme';

// Drives the `data-theme` attribute on <html> from the persisted theme. Runs on server and client,
// so the attribute is in the initial SSR HTML and the scoped theme tokens apply on first paint —
// no flash of the wrong theme. Catalogue themes ship as static CSS; a custom theme has no static
// block, so its generated CSS is injected here from the saved seeds (also SSR-rendered, no flash).
export default defineNuxtPlugin(() => {
    const { current, customSeeds } = useTheme();
    useHead({
        htmlAttrs: { 'data-theme': () => current.value },
        style: () =>
            current.value === CUSTOM_THEME_ID && customSeeds.value
                ? [{ id: 'kenku-custom-theme', innerHTML: renderThemeCss({ id: CUSTOM_THEME_ID, seeds: customSeeds.value }) }]
                : [],
    });
});

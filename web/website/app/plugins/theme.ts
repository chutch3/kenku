// Drives the `data-theme` attribute on <html> from the persisted theme. Runs on server and client,
// so the attribute is in the initial SSR HTML and the scoped theme tokens apply on first paint —
// no flash of the wrong theme.
export default defineNuxtPlugin(() => {
    const { current } = useTheme();
    useHead({ htmlAttrs: { 'data-theme': () => current.value } });
});

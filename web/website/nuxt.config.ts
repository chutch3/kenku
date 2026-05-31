import tailwindcss from '@tailwindcss/vite';
// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
    compatibilityDate: '2025-07-15',
    devtools: { enabled: true },
    css: ['~/assets/css/main.css'],
    modules: ['@nuxt/content', '@nuxt/eslint', '@nuxt/image', '@nuxt/ui', 'nuxt-open-fetch', '@nuxtjs/mdc'],
    devServer: { host: '127.0.0.1' },
    openFetch: {
        clients: {
            // The .NET app serves this SPA and the API from the same origin, so requests are root-relative.
            // The OpenAPI schema is read from the sibling api/ project at build time (no network dependency).
            api: { baseURL: '/', schema: '../../api/API/openapi/API_v2.json' },
        },
    },
    vite: { plugins: [tailwindcss()] },
    nitro: { prerender: { failOnError: false } },
    app: { head: { title: 'Kenku', htmlAttrs: { lang: 'en' }, link: [{ rel: 'icon', type: 'image/png', href: '/blahaj.png' }] } },
});

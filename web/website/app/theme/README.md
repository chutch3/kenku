# Theming

The app ships a catalogue of colour themes plus a build-your-own option. Themes are orthogonal to
light/dark — that stays on the header colour-mode toggle, and every theme defines both modes.

## How it works

- **`registry.ts`** — the source of truth. Each theme is `{ id, name, jaName, package, seeds }`, where
  `seeds` is three colours: `primary`, `secondary`, `neutral`.
- **`generate.ts`** — pure and isomorphic. Turns seeds into the CSS variables a theme needs: colour
  ramps (`buildRamp`), surfaces derived from the neutral seed, and a WCAG `contrastRatio` helper. Runs
  at build time (the catalogue) **and** in the browser (the custom builder), so both share one path.
- **`themes.generated.css`** — committed output of the generator, `@import`ed by `main.css`. Each theme
  is a `[data-theme="<id>"]` block plus a `[data-theme="<id>"].dark` override.
- **`useTheme()`** — persists the choice in a cookie; the `theme` plugin renders `data-theme` on
  `<html>` server-side, so the right theme is on the first paint (no flash). A custom theme has no
  static block, so its CSS is injected as a runtime `<style>` from the saved seeds.

Karasu (the default) is hand-tuned in `main.css` and marked `builtin`; the generator skips it.

## Add a theme

1. Add an entry to `THEMES` in `registry.ts`.
2. Run `npm run gen:themes` to regenerate `themes.generated.css`.
3. Run `npm run test:component` — the registry-shape test, the per-theme contrast audit, and the
   "generated CSS matches the registry" drift guard all run against it.

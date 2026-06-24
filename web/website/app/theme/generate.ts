// Pure, isomorphic theme generation: turns a handful of seed colours into the full set of CSS
// variables a theme needs. Runs at build time (the static catalogue) and in the browser/SSR (the
// custom-theme builder), so it must stay free of DOM and Node APIs.

/** The Tailwind/Nuxt UI shade stops every colour ramp must emit. */
export const RAMP_STOPS = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950] as const;

export type Ramp = Record<string, string>;

// How far each stop sits from the 500 anchor: positive mixes toward white (lighter), negative
// toward black (darker), 0 is the seed itself. Chosen for a roughly even perceptual spread.
const MIX: Record<number, number> = {
    50: 0.95,
    100: 0.88,
    200: 0.75,
    300: 0.58,
    400: 0.3,
    500: 0,
    600: -0.18,
    700: -0.34,
    800: -0.5,
    900: -0.62,
    950: -0.78,
};

function parseHex(hex: string): [number, number, number] {
    const n = parseInt(hex.slice(1), 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

/** WCAG relative luminance of a hex colour. */
function luminance(hex: string): number {
    const ch = parseHex(hex).map((v) => {
        const s = v / 255;
        return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * ch[0]! + 0.7152 * ch[1]! + 0.0722 * ch[2]!;
}

/** WCAG contrast ratio between two hex colours (1..21). */
export function contrastRatio(a: string, b: string): number {
    const la = luminance(a);
    const lb = luminance(b);
    return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

function toHex(rgb: [number, number, number]): string {
    return '#' + rgb.map((c) => Math.max(0, Math.min(255, Math.round(c))).toString(16).padStart(2, '0')).join('');
}

export interface Seeds {
    primary: string;
    secondary: string;
    neutral: string;
}

/** The CSS-variable map for one colour mode. */
export type ThemeVars = Record<string, string>;

/** A colour ramp as Nuxt UI's `--ui-color-{role}-{stop}` variables. */
function rampVars(role: string, ramp: Ramp): ThemeVars {
    const out: ThemeVars = {};
    for (const [stop, hex] of Object.entries(ramp)) out[`--ui-color-${role}-${stop}`] = hex;
    return out;
}

/**
 * Turns seed colours into the light + dark CSS-variable maps a theme needs. This must reach parity
 * with the hand-authored Karasu block in main.css: the full surface set (bg/border/text families)
 * derived from the neutral ramp, the accents (anchored to the seed in light and the 400 stop in dark
 * so solid controls stay legible on dark surfaces), and the `--kenku-*` atmosphere vars that actually
 * drive the page background, screentone and grain. Omitting any of these renders the theme broken.
 */
export function generateTheme(seeds: Seeds): { light: ThemeVars; dark: ThemeVars } {
    const primary = buildRamp(seeds.primary);
    const secondary = buildRamp(seeds.secondary);
    const n = buildRamp(seeds.neutral);
    return {
        light: {
            // The shade ramps are mode-independent and live in the base block; Nuxt UI components read
            // these (not the bare --ui-primary) for fills, hovers, rings — so they must be themed too.
            ...rampVars('primary', primary),
            ...rampVars('secondary', secondary),
            ...rampVars('neutral', n),
            '--ui-bg': n['100']!,
            '--ui-bg-muted': n['200']!,
            '--ui-bg-elevated': n['50']!,
            '--ui-bg-accented': n['200']!,
            '--ui-bg-inverted': n['900']!,
            '--ui-border': n['200']!,
            '--ui-border-muted': n['100']!,
            '--ui-border-accented': n['300']!,
            '--ui-border-inverted': n['900']!,
            '--ui-text-dimmed': n['400']!,
            '--ui-text-muted': n['500']!,
            '--ui-text-toned': n['700']!,
            '--ui-text': n['800']!,
            '--ui-text-highlighted': n['950']!,
            '--ui-text-inverted': n['50']!,
            '--ui-primary': primary['500']!,
            '--ui-secondary': secondary['500']!,
            '--kenku-paper': n['100']!,
            '--kenku-screentone': '0, 0, 0',
            '--kenku-grain-opacity': '0.05',
            '--kenku-glow-strength': '0.1',
        },
        dark: {
            '--ui-bg': n['950']!,
            '--ui-bg-muted': n['900']!,
            '--ui-bg-elevated': n['800']!,
            '--ui-bg-accented': n['700']!,
            '--ui-bg-inverted': n['50']!,
            '--ui-border': n['800']!,
            '--ui-border-muted': n['900']!,
            '--ui-border-accented': n['700']!,
            '--ui-border-inverted': n['50']!,
            '--ui-text-dimmed': n['500']!,
            '--ui-text-muted': n['400']!,
            '--ui-text-toned': n['300']!,
            '--ui-text': n['100']!,
            '--ui-text-highlighted': n['50']!,
            '--ui-text-inverted': n['950']!,
            '--ui-primary': primary['400']!,
            '--ui-secondary': secondary['400']!,
            '--kenku-paper': n['950']!,
            '--kenku-screentone': '255, 255, 255',
            '--kenku-grain-opacity': '0.035',
            '--kenku-glow-strength': '0.16',
        },
    };
}

/** Minimum a theme needs to be rendered to CSS — kept local so this module doesn't depend on the registry. */
export interface RenderableTheme {
    id: string;
    seeds: Seeds;
    builtin?: boolean;
}

function declarations(vars: ThemeVars): string {
    return Object.entries(vars)
        .map(([k, v]) => `        ${k}: ${v};`)
        .join('\n');
}

/** Renders one theme as its `[data-theme]` light block plus a `.dark` override. */
export function renderThemeCss(theme: RenderableTheme): string {
    const { light, dark } = generateTheme(theme.seeds);
    return (
        `    [data-theme="${theme.id}"] {\n${declarations(light)}\n    }\n` +
        `    [data-theme="${theme.id}"].dark {\n${declarations(dark)}\n    }`
    );
}

/** Renders the full catalogue (skipping builtin/hand-authored themes) wrapped in `@layer base`. */
export function renderCatalogCss(themes: RenderableTheme[]): string {
    const blocks = themes.filter((t) => !t.builtin).map(renderThemeCss);
    return `@layer base {\n${blocks.join('\n\n')}\n}\n`;
}

/** Builds an 11-stop ramp from a seed, anchoring the 500 stop to the seed colour. */
export function buildRamp(seed: string): Ramp {
    const [r, g, b] = parseHex(seed);
    const ramp: Ramp = {};
    for (const stop of RAMP_STOPS) {
        const f = MIX[stop]!;
        if (f === 0) {
            ramp[stop] = seed;
        } else if (f > 0) {
            ramp[stop] = toHex([r + (255 - r) * f, g + (255 - g) * f, b + (255 - b) * f]);
        } else {
            const k = 1 + f; // f is negative — scale toward black
            ramp[stop] = toHex([r * k, g * k, b * k]);
        }
    }
    return ramp;
}

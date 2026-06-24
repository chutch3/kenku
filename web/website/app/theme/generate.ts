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

/**
 * Turns seed colours into the light + dark CSS-variable maps a theme needs. Surfaces come from the
 * neutral ramp (lightest/darkest ends); the accent semantics anchor to the seed in light and the
 * 400 stop in dark (matching Nuxt UI's own ramp), so solid controls stay legible on dark surfaces.
 */
export function generateTheme(seeds: Seeds): { light: ThemeVars; dark: ThemeVars } {
    const primary = buildRamp(seeds.primary);
    const secondary = buildRamp(seeds.secondary);
    const neutral = buildRamp(seeds.neutral);
    return {
        light: {
            '--ui-bg': neutral['50']!,
            '--ui-text': neutral['900']!,
            '--ui-border': neutral['200']!,
            '--ui-primary': primary['500']!,
            '--ui-secondary': secondary['500']!,
        },
        dark: {
            '--ui-bg': neutral['950']!,
            '--ui-text': neutral['50']!,
            '--ui-border': neutral['800']!,
            '--ui-primary': primary['400']!,
            '--ui-secondary': secondary['400']!,
        },
    };
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

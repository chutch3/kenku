import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { buildRamp, RAMP_STOPS, generateTheme, contrastRatio, renderThemeCss, renderCatalogCss } from '~/theme/generate';

// sRGB relative luminance (WCAG) — used here to assert a ramp gets monotonically darker.
function luminance(hex: string): number {
    const n = parseInt(hex.slice(1), 16);
    const ch = [(n >> 16) & 255, (n >> 8) & 255, n & 255].map((v) => {
        const s = v / 255;
        return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
    });
    return 0.2126 * ch[0]! + 0.7152 * ch[1]! + 0.0722 * ch[2]!;
}

const HEX = /^#[0-9a-f]{6}$/i;
const SEED = '#e5483a'; // Karasu vermillion-500

describe('buildRamp', () => {
    it('emits the 11 Tailwind stops 50..950', () => {
        expect(Object.keys(buildRamp(SEED))).toEqual(RAMP_STOPS.map(String));
    });

    it('produces a valid 6-digit hex for every stop', () => {
        for (const v of Object.values(buildRamp(SEED))) expect(v).toMatch(HEX);
    });

    it('anchors the 500 stop to the seed colour', () => {
        expect(buildRamp(SEED)['500']!.toLowerCase()).toBe(SEED);
    });

    it('darkens monotonically from 50 to 950', () => {
        const ramp = buildRamp(SEED);
        const ls = RAMP_STOPS.map((s) => luminance(ramp[String(s)]!));
        for (let i = 1; i < ls.length; i++) expect(ls[i]!).toBeLessThan(ls[i - 1]!);
    });
});

describe('contrastRatio', () => {
    it('is 21 for black on white and 1 for a colour on itself', () => {
        expect(contrastRatio('#000000', '#ffffff')).toBeCloseTo(21, 1);
        expect(contrastRatio('#3a7bd5', '#3a7bd5')).toBeCloseTo(1, 5);
    });
});

describe('generateTheme', () => {
    const KARASU = { primary: '#e5483a', secondary: '#12b299', neutral: '#585d70' };

    it('emits surface and semantic variables for both light and dark', () => {
        const t = generateTheme(KARASU);
        for (const mode of [t.light, t.dark]) {
            for (const v of ['--ui-bg', '--ui-text', '--ui-border', '--ui-primary', '--ui-secondary']) {
                expect(mode[v]).toMatch(HEX);
            }
        }
    });

    it('anchors --ui-primary to the primary seed in light mode', () => {
        expect(generateTheme(KARASU).light['--ui-primary']!.toLowerCase()).toBe('#e5483a');
    });

    it('emits the full primary, secondary and neutral shade ramps Nuxt UI components read', () => {
        const t = generateTheme(KARASU);
        for (const stop of RAMP_STOPS) {
            expect(t.light[`--ui-color-primary-${stop}`]).toMatch(HEX);
            expect(t.light[`--ui-color-secondary-${stop}`]).toMatch(HEX);
            expect(t.light[`--ui-color-neutral-${stop}`]).toMatch(HEX);
        }
        // The 500 stop anchors to the seed, so shaded components (buttons, progress, rings) track the theme.
        expect(t.light['--ui-color-primary-500']!.toLowerCase()).toBe('#e5483a');
    });

    it('scales the atmosphere by profile so themes feel distinct (vivid > standard > calm)', () => {
        const glow = (p?: 'calm' | 'standard' | 'vivid') => Number(generateTheme(KARASU, p).light['--kenku-glow-strength']);
        const grain = (p?: 'calm' | 'standard' | 'vivid') => Number(generateTheme(KARASU, p).light['--kenku-grain-opacity']);
        expect(glow('vivid')).toBeGreaterThan(glow('standard'));
        expect(glow('standard')).toBeGreaterThan(glow('calm'));
        expect(grain('vivid')).toBeGreaterThan(grain('calm'));
        expect(glow()).toBe(glow('standard')); // defaults to standard
    });

    it('keeps body text readable (WCAG AA) on the background in both modes', () => {
        const t = generateTheme(KARASU);
        expect(contrastRatio(t.light['--ui-text']!, t.light['--ui-bg']!)).toBeGreaterThanOrEqual(4.5);
        expect(contrastRatio(t.dark['--ui-text']!, t.dark['--ui-bg']!)).toBeGreaterThanOrEqual(4.5);
    });
});

describe('generateTheme parity with the Karasu reference block', () => {
    // The hand-authored Karasu block in main.css is the known-good set of variables every theme must
    // define — surfaces (--ui-*) and the atmosphere vars (--kenku-paper/screentone/grain/glow) that
    // actually drive the page background. A generated theme that omits any of them renders broken
    // (white background, full-opacity noise grain, dead dark mode).
    const KARASU = { primary: '#e5483a', secondary: '#12b299', neutral: '#585d70' };
    const css = readFileSync('app/assets/css/main.css', 'utf8');
    const varsDefinedIn = (selector: string) => {
        const open = css.indexOf('{', css.indexOf(selector));
        const close = css.indexOf('}', open);
        return [...css.slice(open, close).matchAll(/(--[a-z-]+)\s*:/g)].map((m) => m[1]!);
    };

    it('emits every variable the Karasu light block defines', () => {
        const keys = Object.keys(generateTheme(KARASU).light);
        for (const v of varsDefinedIn("[data-theme='karasu'] {")) expect(keys).toContain(v);
    });

    it('emits every variable the Karasu dark block defines', () => {
        const keys = Object.keys(generateTheme(KARASU).dark);
        for (const v of varsDefinedIn("[data-theme='karasu'].dark {")) expect(keys).toContain(v);
    });
});

describe('renderThemeCss', () => {
    const pride = { id: 'trans-pride', name: 'Trans', package: 'Pride' as const, seeds: { primary: '#87ceeb', secondary: '#ffc0cb', neutral: '#94a3b8' } };

    it('emits a data-theme block and a .dark override for the theme', () => {
        const css = renderThemeCss(pride);
        expect(css).toContain('[data-theme="trans-pride"] {');
        expect(css).toContain('[data-theme="trans-pride"].dark {');
        expect(css).toContain('--ui-bg:');
        expect(css).toContain('--ui-primary: #87ceeb');
    });
});

describe('renderCatalogCss', () => {
    const karasu = { id: 'karasu', name: 'Karasu', package: 'House' as const, builtin: true, seeds: { primary: '#e5483a', secondary: '#12b299', neutral: '#585d70' } };
    const pride = { id: 'trans-pride', name: 'Trans', package: 'Pride' as const, seeds: { primary: '#87ceeb', secondary: '#ffc0cb', neutral: '#94a3b8' } };

    it('wraps generated themes in @layer base and skips builtin (hand-authored) themes', () => {
        const css = renderCatalogCss([karasu, pride]);
        expect(css).toContain('@layer base');
        expect(css).toContain('[data-theme="trans-pride"]');
        expect(css).not.toContain('[data-theme="karasu"]');
    });
});

import { readFileSync } from 'node:fs';
import { describe, it, expect } from 'vitest';
import { THEMES, getTheme } from '~/theme/registry';
import { renderCatalogCss } from '~/theme/generate';

const HEX = /^#[0-9a-f]{6}$/i;

describe('theme registry', () => {
    it('lists themes with an id, name and three valid seed colours', () => {
        expect(THEMES.length).toBeGreaterThan(1);
        for (const t of THEMES) {
            expect(t.id).toMatch(/^[a-z0-9-]+$/);
            expect(t.name.length).toBeGreaterThan(0);
            for (const seed of [t.seeds.primary, t.seeds.secondary, t.seeds.neutral]) {
                expect(seed).toMatch(HEX);
            }
        }
    });

    it('has unique ids', () => {
        const ids = THEMES.map((t) => t.id);
        expect(new Set(ids).size).toBe(ids.length);
    });

    it('includes the hand-authored karasu default (marked builtin) and the trans-pride pilot', () => {
        const karasu = getTheme('karasu');
        expect(karasu?.builtin).toBe(true);
        expect(getTheme('trans-pride')).toBeDefined();
    });

    it('getTheme returns undefined for an unknown id', () => {
        expect(getTheme('not-a-theme')).toBeUndefined();
    });

    // Guards against editing the registry without re-running `npm run gen:themes`.
    it('has a committed themes.generated.css that matches the registry', () => {
        const committed = readFileSync('app/assets/css/themes.generated.css', 'utf8');
        expect(committed).toContain(renderCatalogCss(THEMES));
    });
});

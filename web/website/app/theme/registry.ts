import type { Seeds } from './generate';

/** A package groups themes by cause/identity for display in the picker. */
export type ThemePackage = 'House' | 'Pride';

export interface Theme {
    /** Stable id used as the `data-theme` value and persisted choice. */
    id: string;
    /** English display name. */
    name: string;
    /** Japanese display name, in keeping with the Karasu aesthetic. */
    jaName?: string;
    package: ThemePackage;
    seeds: Seeds;
    /** Hand-authored themes (currently only Karasu) live in main.css; the generator skips them. */
    builtin?: boolean;
}

// The seeds mirror Karasu's vermillion-500 / jade-500 / sumi-500 so the picker can preview it, but
// its CSS stays the bespoke hand-tuned block in main.css (builtin).
export const THEMES: Theme[] = [
    {
        id: 'karasu',
        name: 'Karasu',
        jaName: '烏',
        package: 'House',
        builtin: true,
        seeds: { primary: '#e5483a', secondary: '#12b299', neutral: '#585d70' },
    },
    {
        id: 'trans-pride',
        name: 'Transgender Pride',
        jaName: 'トランスジェンダー・プライド',
        package: 'Pride',
        seeds: { primary: '#87ceeb', secondary: '#ffc0cb', neutral: '#94a3b8' },
    },
];

export function getTheme(id: string): Theme | undefined {
    return THEMES.find((t) => t.id === id);
}

import type { Seeds, AtmosphereProfile } from './generate';

/** A package groups themes by cause/identity for display in the picker. */
export type ThemePackage = 'House' | 'Health' | 'Pride' | 'Service' | 'Justice' | 'Heritage';

export interface Theme {
    /** Stable id used as the `data-theme` value and persisted choice. */
    id: string;
    /** English display name. */
    name: string;
    /** Japanese display name, in keeping with the Karasu aesthetic. */
    jaName?: string;
    package: ThemePackage;
    seeds: Seeds;
    /** Ambient mood (glow + grain intensity); defaults to 'standard'. */
    atmosphere?: AtmosphereProfile;
    /** Hand-authored themes (currently only Karasu) live in main.css; the generator skips them. */
    builtin?: boolean;
}

// Each theme maps its two strongest identity colours to primary/secondary and a balanced neutral that
// drives the light/dark surfaces. Karasu's seeds mirror its hand-tuned palette so the picker can
// preview it, but its CSS stays the bespoke block in main.css (builtin).
export const THEMES: Theme[] = [
    { id: 'karasu', name: 'Karasu', jaName: '烏', package: 'House', builtin: true, seeds: { primary: '#e5483a', secondary: '#12b299', neutral: '#585d70' } },

    // ---- Health -----------------------------------------------------------------------------------
    { id: 'breast-cancer', name: 'Breast Cancer Awareness', jaName: '乳がん啓発', package: 'Health', seeds: { primary: '#ff69b4', secondary: '#ffb6c1', neutral: '#a8a29e' } },
    { id: 'ovarian', name: 'Gynecologic Health', jaName: '卵巣がん啓発', package: 'Health', seeds: { primary: '#008080', secondary: '#20b2aa', neutral: '#94a3b8' } },
    { id: 'heart-disease', name: 'Heart Disease Awareness', jaName: '心臓病啓発', package: 'Health', seeds: { primary: '#dc143c', secondary: '#8b0000', neutral: '#2c3e50' } },
    { id: 'mental-health', name: 'Mental Health Awareness', jaName: 'メンタルヘルス啓発', package: 'Health', atmosphere: 'calm', seeds: { primary: '#32cd32', secondary: '#228b22', neutral: '#9ca3af' } },

    // ---- Pride ------------------------------------------------------------------------------------
    { id: 'progress-pride', name: 'Progress Pride', jaName: 'プログレス・プライド', package: 'Pride', seeds: { primary: '#5bcefa', secondary: '#4a3525', neutral: '#6b7280' } },
    { id: 'trans-pride', name: 'Transgender Pride', jaName: 'トランスジェンダー・プライド', package: 'Pride', seeds: { primary: '#87ceeb', secondary: '#ffc0cb', neutral: '#94a3b8' } },
    { id: 'non-binary', name: 'Non-Binary Pride', jaName: 'ノンバイナリー・プライド', package: 'Pride', atmosphere: 'vivid', seeds: { primary: '#fff700', secondary: '#9b59b6', neutral: '#2c3e50' } },
    { id: 'bisexual', name: 'Bisexual Pride', jaName: 'バイセクシュアル・プライド', package: 'Pride', seeds: { primary: '#d60270', secondary: '#0038a8', neutral: '#7c6f86' } },
    { id: 'pansexual', name: 'Pansexual Pride', jaName: 'パンセクシュアル・プライド', package: 'Pride', atmosphere: 'vivid', seeds: { primary: '#ff007f', secondary: '#00bfff', neutral: '#a3a3a3' } },
    { id: 'lesbian', name: 'Lesbian Pride', jaName: 'レズビアン・プライド', package: 'Pride', seeds: { primary: '#d52d00', secondary: '#ff9a56', neutral: '#9b7d86' } },

    // ---- Service ----------------------------------------------------------------------------------
    { id: 'military', name: 'Military Support', jaName: '軍隊・退役軍人支援', package: 'Service', seeds: { primary: '#ffd700', secondary: '#556b2f', neutral: '#0a192f' } },
    { id: 'firefighter', name: 'Firefighter Support', jaName: '消防士支援', package: 'Service', atmosphere: 'vivid', seeds: { primary: '#ff0000', secondary: '#d3d3d3', neutral: '#2b2b2b' } },
    { id: 'ems', name: 'Emergency Medical Services', jaName: '救急医療支援', package: 'Service', seeds: { primary: '#4dd0e1', secondary: '#34495e', neutral: '#64748b' } },

    // ---- Justice ----------------------------------------------------------------------------------
    { id: 'racial-justice', name: 'Racial Justice & Equity', jaName: '人種的正義と平等', package: 'Justice', seeds: { primary: '#ffd700', secondary: '#000000', neutral: '#6b7280' } },
    { id: 'global-peace', name: 'Global Peace', jaName: '世界平和', package: 'Justice', atmosphere: 'calm', seeds: { primary: '#64b5f6', secondary: '#b39ddb', neutral: '#aeb6c2' } },
    { id: 'environmental', name: 'Environmental Action', jaName: '環境保護活動', package: 'Justice', seeds: { primary: '#2e7d32', secondary: '#a5d6a7', neutral: '#4a3525' } },

    // ---- Heritage ---------------------------------------------------------------------------------
    { id: 'hinomaru', name: 'Traditional Heritage', jaName: '日本の伝統', package: 'Heritage', seeds: { primary: '#bc002d', secondary: '#d4af37', neutral: '#a8a29e' } },
    { id: 'miyabi', name: 'Imperial Elegance', jaName: '雅', package: 'Heritage', seeds: { primary: '#4a0e4e', secondary: '#ffb7c5', neutral: '#8b7d8b' } },
    { id: 'wabi-sabi', name: 'Wabi-Sabi', jaName: '侘寂', package: 'Heritage', atmosphere: 'calm', seeds: { primary: '#8fbc8f', secondary: '#708090', neutral: '#a8a29e' } },
    { id: 'aizome', name: 'Indigo Dye', jaName: '藍染め', package: 'Heritage', atmosphere: 'calm', seeds: { primary: '#0f2042', secondary: '#4682b4', neutral: '#7a8290' } },
    { id: 'akihabara', name: 'Modern Pop Culture', jaName: '現代カルチャー', package: 'Heritage', atmosphere: 'vivid', seeds: { primary: '#ff007f', secondary: '#00ffff', neutral: '#121212' } },
];

export function getTheme(id: string): Theme | undefined {
    return THEMES.find((t) => t.id === id);
}

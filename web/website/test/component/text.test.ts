import { describe, it, expect } from 'vitest';
import { deCamel } from '~/utils/text';

describe('deCamel', () => {
    it('splits camelCase into Title Case words', () => {
        expect(deCamel('downloadedChapters')).toBe('Downloaded Chapters');
        expect(deCamel('totalSeries')).toBe('Total Series');
    });

    it('capitalises a single lowercase word', () => {
        expect(deCamel('chapters')).toBe('Chapters');
    });

    it('leaves already-spaced or single-letter input sane', () => {
        expect(deCamel('series')).toBe('Series');
        expect(deCamel('')).toBe('');
    });
});

import { describe, it, expect } from 'vitest';
import { isUrl, parseUrl } from '~/utils/url';

describe('url util', () => {
    it('recognises absolute URLs and rejects plain titles', () => {
        expect(isUrl('https://weebcentral.com/series/01ABC')).toBe(true);
        expect(isUrl('http://localhost:8080')).toBe(true);
        expect(isUrl('Berserk')).toBe(false);
        expect(isUrl('chainsaw man chapter 1')).toBe(false);
        expect(isUrl('')).toBe(false);
    });

    it('parses a URL or returns null', () => {
        expect(parseUrl('https://komga.local/')?.host).toBe('komga.local');
        expect(parseUrl('not a url')).toBeNull();
    });
});

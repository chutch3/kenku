import { describe, it, expect } from 'vitest';
import { formatDuration, formatRelative } from '~/utils/duration';

describe('formatDuration', () => {
    it('renders sub-second spans in milliseconds', () => {
        expect(formatDuration(0)).toBe('0ms');
        expect(formatDuration(850)).toBe('850ms');
    });

    it('renders seconds with one decimal', () => {
        expect(formatDuration(1200)).toBe('1.2s');
        expect(formatDuration(59_900)).toBe('59.9s');
    });

    it('renders minutes and seconds', () => {
        expect(formatDuration(184_000)).toBe('3m 4s');
    });

    it('renders hours and minutes', () => {
        expect(formatDuration(3_720_000)).toBe('1h 2m');
    });

    it('clamps negative or non-finite input to zero', () => {
        expect(formatDuration(-5)).toBe('0ms');
        expect(formatDuration(Number.NaN)).toBe('0ms');
    });
});

describe('formatRelative', () => {
    const now = 1_000_000_000_000;
    it('floors very recent and future instants to a sane label', () => {
        expect(formatRelative(now, now)).toBe('1s ago');
        expect(formatRelative(now + 5000, now)).toBe('just now');
    });
    it('scales through seconds, minutes, hours, days', () => {
        expect(formatRelative(now - 30_000, now)).toBe('30s ago');
        expect(formatRelative(now - 120_000, now)).toBe('2m ago');
        expect(formatRelative(now - 7_200_000, now)).toBe('2h ago');
        expect(formatRelative(now - 172_800_000, now)).toBe('2d ago');
    });
});

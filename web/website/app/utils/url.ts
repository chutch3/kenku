/** True when the input parses as an absolute URL — used to tell a pasted series URL from a title. */
export const isUrl = (input: string): boolean => parseUrl(input) !== null;

/** Parse an absolute URL, or null if it isn't one. Avoids the try/catch repeated at call sites. */
export const parseUrl = (input: string): URL | null => {
    try {
        return new URL(input);
    } catch {
        return null;
    }
};

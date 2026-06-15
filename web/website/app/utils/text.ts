/** Turn a camelCase or PascalCase identifier into spaced Title Case (e.g. "downloadedChapters" →
 * "Downloaded Chapters"). Used for stat labels and action names. */
export const deCamel = (camel: string): string =>
    camel
        .replace(/([a-z])([A-Z])/g, '$1 $2')
        .replace(/(^\w)|(\s+\w)/g, (letter) => letter.toUpperCase());

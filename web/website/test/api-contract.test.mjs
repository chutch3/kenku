// Dependency-free contract test (run via `node --test`).
//
// Catches the class of frontend/backend drift that produced two runtime 404s:
//   1. calling an API path that does not exist in the OpenAPI schema, and
//   2. sending a path/query parameter whose name the endpoint doesn't declare
//      (e.g. `connectorMangaId` where the API expects `connectorSeriesId`).
//
// It statically scans every useApi/useLazyApi/$api call in app/ and asserts the
// path string and its path/query parameter names exist in API_v2.json. This is a
// belt-and-suspenders to `npm run typecheck` (which also catches these), so the
// guard still fires if the type-check is ever skipped.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const appDir = join(here, '..', 'app');
const schemaPath = join(here, '..', '..', '..', 'api', 'API', 'openapi', 'API_v2.json');

const schema = JSON.parse(readFileSync(schemaPath, 'utf8'));

/** Valid path + query parameter names for an OpenAPI path (union across methods). */
function paramsForPath(p) {
    const item = schema.paths[p];
    if (!item) return null;
    const pathParams = new Set();
    const queryParams = new Set();
    const collect = (params) => {
        for (const pr of params ?? []) {
            if (pr.in === 'path') pathParams.add(pr.name);
            else if (pr.in === 'query') queryParams.add(pr.name);
        }
    };
    collect(item.parameters);
    for (const m of ['get', 'post', 'put', 'patch', 'delete', 'options', 'head']) {
        if (item[m]) collect(item[m].parameters);
    }
    return { pathParams, queryParams };
}

function walk(dir) {
    const out = [];
    for (const name of readdirSync(dir)) {
        const full = join(dir, name);
        if (statSync(full).isDirectory()) out.push(...walk(full));
        else if (/\.(vue|ts|mts)$/.test(name)) out.push(full);
    }
    return out;
}

// --- tiny source scanner -------------------------------------------------

const isIdentStart = (c) => /[A-Za-z_$]/.test(c);

/** Read a JS/TS string literal starting at quote index `i`; returns {value, end} or null. */
function readString(src, i) {
    const quote = src[i];
    if (quote !== '"' && quote !== "'" && quote !== '`') return null;
    let j = i + 1;
    let value = '';
    while (j < src.length) {
        const c = src[j];
        if (c === '\\') {
            value += src[j + 1] ?? '';
            j += 2;
            continue;
        }
        if (c === quote) return { value, end: j + 1 };
        value += c;
        j++;
    }
    return null;
}

/** Given index of an opening bracket, return index just past its match (string-aware). */
function matchBracket(src, open) {
    const pairs = { '{': '}', '[': ']', '(': ')' };
    const close = pairs[src[open]];
    let depth = 0;
    for (let j = open; j < src.length; j++) {
        const c = src[j];
        if (c === '"' || c === "'" || c === '`') {
            const s = readString(src, j);
            if (s) {
                j = s.end - 1;
                continue;
            }
        }
        if (c === '{' || c === '[' || c === '(') depth++;
        else if (c === '}' || c === ']' || c === ')') {
            depth--;
            if (c === close && depth === 0) return j + 1;
        }
    }
    return -1;
}

const skipWs = (src, i) => {
    while (i < src.length && /\s/.test(src[i])) i++;
    return i;
};

/** Top-level keys of a flat object-literal `{ ... }` (handles shorthand + nesting). */
function objectKeys(src, open) {
    const end = matchBracket(src, open);
    if (end < 0) return [];
    const keys = [];
    let depth = 0;
    let expectKey = true;
    for (let j = open + 1; j < end - 1; j++) {
        const c = src[j];
        if (c === '"' || c === "'" || c === '`') {
            const s = readString(src, j);
            if (s) {
                j = s.end - 1;
                expectKey = false;
                continue;
            }
        }
        if (c === '{' || c === '[' || c === '(') {
            depth++;
            continue;
        }
        if (c === '}' || c === ']' || c === ')') {
            depth--;
            continue;
        }
        if (depth !== 0) continue;
        if (c === ',') {
            expectKey = true;
            continue;
        }
        if (c === ':') {
            expectKey = false;
            continue;
        }
        if (expectKey && isIdentStart(c)) {
            let k = '';
            while (j < end && /[\w$]/.test(src[j])) k += src[j++];
            j--;
            keys.push(k);
            // stays "expecting key" only flips off when we hit ':' (value follows)
        }
    }
    return keys;
}

/** Find a `name: { ... }` sub-object inside an options object and return its keys. */
function subObjectKeys(optsSrc, name) {
    const re = new RegExp(`\\b${name}\\s*:\\s*\\{`, 'g');
    const m = re.exec(optsSrc);
    if (!m) return [];
    const braceIdx = m.index + m[0].length - 1;
    return objectKeys(optsSrc, braceIdx);
}

const CALL_RE = /(?<![\w$.])(useApi|useLazyApi|\$api)\s*\(/g;

function extractCalls(src) {
    const calls = [];
    let m;
    while ((m = CALL_RE.exec(src))) {
        let i = skipWs(src, m.index + m[0].length);
        const str = readString(src, i);
        if (!str) continue; // dynamic path — can't statically validate
        const path = str.value;
        let rest = skipWs(src, str.end);
        let pathKeys = [];
        let queryKeys = [];
        if (src[rest] === ',') {
            rest = skipWs(src, rest + 1);
            if (src[rest] === '{') {
                const optsEnd = matchBracket(src, rest);
                const optsSrc = src.slice(rest, optsEnd);
                pathKeys = subObjectKeys(optsSrc, 'path');
                queryKeys = subObjectKeys(optsSrc, 'query');
            }
        }
        calls.push({ fn: m[1], path, pathKeys, queryKeys });
    }
    return calls;
}

// --- the test ------------------------------------------------------------

const files = walk(appDir);

test('every frontend API call matches the OpenAPI contract', () => {
    const problems = [];
    let checked = 0;

    for (const file of files) {
        const src = readFileSync(file, 'utf8');
        const rel = file.slice(appDir.length + 1);
        for (const call of extractCalls(src)) {
            // Only validate calls whose path is an OpenAPI path template.
            if (!call.path.startsWith('/')) continue;
            checked++;
            const params = paramsForPath(call.path);
            if (!params) {
                problems.push(`${rel}: ${call.fn}('${call.path}') — path not found in OpenAPI schema`);
                continue;
            }
            for (const k of call.pathKeys) {
                if (!params.pathParams.has(k)) {
                    problems.push(
                        `${rel}: ${call.fn}('${call.path}') — path param '${k}' not declared (valid: ${[...params.pathParams].join(', ') || 'none'})`,
                    );
                }
            }
            for (const k of call.queryKeys) {
                if (!params.queryParams.has(k)) {
                    problems.push(
                        `${rel}: ${call.fn}('${call.path}') — query param '${k}' not declared (valid: ${[...params.queryParams].join(', ') || 'none'})`,
                    );
                }
            }
        }
    }

    assert.ok(checked > 0, 'expected to find API calls to validate');
    assert.deepEqual(problems, [], `\n${problems.join('\n')}\n`);
});

// --- response/request body shape contract --------------------------------
// The call-path scan above can't see the response/request BODY fields a component reads or sends.
// LooseChapters.vue reads `unassigned[].chapterId/chapterNumber` from GET /volumes and posts
// `{ assignments }` to POST /volumes/assignments — pin those so a backend rename can't silently break it.

function resolveRef(ref) {
    let node = schema;
    for (const part of ref.replace(/^#\//, '').split('/')) node = node?.[part];
    return node;
}

/** The schema under the first `application/json...` content type of a content map. */
function jsonSchema(content) {
    const key = Object.keys(content ?? {}).find((k) => k.startsWith('application/json'));
    return key ? content[key].schema : null;
}

/** Property names of a schema node, resolving a top-level $ref. */
function propsOf(node) {
    if (!node) return null;
    if (node.$ref) node = resolveRef(node.$ref);
    return node?.properties ? Object.keys(node.properties) : null;
}

test('LooseChapters contract: /volumes exposes loose chapters and assignments accepts a chapter→volume map', () => {
    const volContent = schema.paths['/v2/Series/{MangaId}/volumes']?.get?.responses?.['200']?.content;
    const volSchema = jsonSchema(volContent);
    assert.ok(propsOf(volSchema)?.includes('unassigned'), 'VolumeListResult must expose `unassigned`');

    const volNode = volSchema.$ref ? resolveRef(volSchema.$ref) : volSchema;
    const entryProps = propsOf(volNode.properties.unassigned.items);
    for (const field of ['chapterId', 'chapterNumber']) {
        assert.ok(entryProps?.includes(field), `loose chapter entries must expose \`${field}\``);
    }

    const asnContent = schema.paths['/v2/Series/{MangaId}/volumes/assignments']?.post?.requestBody?.content;
    assert.ok(propsOf(jsonSchema(asnContent))?.includes('assignments'), 'assignment request must accept `assignments`');
});

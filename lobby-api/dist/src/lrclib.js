export const LRCLIB_USER_AGENT = 'AuraLauncher (github.com/qutlawsoasis-debug/Aura-Launcher)';
export const LRCLIB_TIMEOUT_MS = 4000;
/**
 * Reads JSON from a Response while respecting the AbortSignal throughout body streaming.
 */
async function readJsonWithSignal(res, signal) {
    if (signal.aborted) {
        const err = new Error('This operation was aborted');
        err.name = 'AbortError';
        throw err;
    }
    return new Promise((resolve, reject) => {
        let settled = false;
        const onAbort = () => {
            if (settled)
                return;
            settled = true;
            const err = new Error('This operation was aborted');
            err.name = 'AbortError';
            reject(err);
        };
        signal.addEventListener('abort', onAbort, { once: true });
        Promise.resolve(res.json())
            .then((data) => {
            if (settled)
                return;
            settled = true;
            signal.removeEventListener('abort', onAbort);
            resolve(data);
        })
            .catch((err) => {
            if (settled)
                return;
            settled = true;
            signal.removeEventListener('abort', onAbort);
            reject(err);
        });
    });
}
/**
 * Fetches JSON while keeping the timeout active until headers AND body (res.json())
 * are completely read and parsed.
 */
export async function fetchJsonWithTimeout(url, timeoutMs = LRCLIB_TIMEOUT_MS, customFetch = fetch) {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
    try {
        const res = await customFetch(url, {
            headers: {
                'User-Agent': LRCLIB_USER_AGENT,
                Accept: 'application/json'
            },
            signal: controller.signal
        });
        if (!res.ok) {
            return { ok: false, status: res.status, signal: controller.signal };
        }
        const data = await readJsonWithSignal(res, controller.signal);
        return { ok: true, status: res.status, data, signal: controller.signal };
    }
    finally {
        clearTimeout(timeoutId);
    }
}
/**
 * Convenience fetch wrapper with timeout.
 */
export async function fetchWithTimeout(url, timeoutMs = LRCLIB_TIMEOUT_MS, customFetch = fetch) {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
    try {
        return await customFetch(url, {
            headers: {
                'User-Agent': LRCLIB_USER_AGENT,
                Accept: 'application/json'
            },
            signal: controller.signal
        });
    }
    finally {
        clearTimeout(timeoutId);
    }
}
/**
 * Fetches lyrics from LRCLIB:
 * 1. Tries /api/get
 * 2. On miss, falls back to /api/search and selects candidate by duration ±3 s.
 * Maintains timeout limit throughout all network transfers and response parsing.
 */
export async function fetchLyricsFromLrclib(artist, title, durationSec, customFetch = fetch, timeoutMs = LRCLIB_TIMEOUT_MS) {
    const cleanA = artist.trim();
    const cleanT = title.trim();
    if (!cleanA || !cleanT)
        return null;
    // 1. Try /api/get
    const getParams = new URLSearchParams({
        artist_name: cleanA,
        track_name: cleanT
    });
    if (typeof durationSec === 'number' && durationSec > 0) {
        getParams.set('duration', Math.round(durationSec).toString());
    }
    const getUrl = `https://lrclib.net/api/get?${getParams.toString()}`;
    try {
        const res = await fetchJsonWithTimeout(getUrl, timeoutMs, customFetch);
        if (res.ok && res.data) {
            if (res.data.syncedLyrics || res.data.plainLyrics) {
                return res.data;
            }
        }
    }
    catch (err) {
        if (err?.name === 'AbortError' || err?.message?.includes('abort')) {
            throw err;
        }
        // Otherwise proceed to search fallback
    }
    // 2. Fallback to /api/search
    const searchParams = new URLSearchParams({
        artist_name: cleanA,
        track_name: cleanT
    });
    const searchUrl = `https://lrclib.net/api/search?${searchParams.toString()}`;
    let items = [];
    try {
        const searchRes = await fetchJsonWithTimeout(searchUrl, timeoutMs, customFetch);
        if (searchRes.ok && Array.isArray(searchRes.data)) {
            items = searchRes.data;
        }
    }
    catch (err) {
        if (err?.name === 'AbortError' || err?.message?.includes('abort')) {
            throw err;
        }
    }
    // If search with track_name & artist_name returned nothing, try query `q`
    if (!Array.isArray(items) || items.length === 0) {
        const qUrl = `https://lrclib.net/api/search?q=${encodeURIComponent(`${cleanA} ${cleanT}`)}`;
        try {
            const qRes = await fetchJsonWithTimeout(qUrl, timeoutMs, customFetch);
            if (qRes.ok && Array.isArray(qRes.data)) {
                items = qRes.data;
            }
        }
        catch (err) {
            if (err?.name === 'AbortError' || err?.message?.includes('abort')) {
                throw err;
            }
        }
    }
    if (!Array.isArray(items) || items.length === 0) {
        return null;
    }
    // Filter and pick candidate
    const hasLyrics = (item) => Boolean(item.syncedLyrics || item.plainLyrics);
    const candidatesWithLyrics = items.filter(hasLyrics);
    if (candidatesWithLyrics.length === 0) {
        return null;
    }
    if (typeof durationSec === 'number' && durationSec > 0) {
        // Selection by duration ±3 s
        const durationMatches = candidatesWithLyrics.filter(item => typeof item.duration === 'number' && Math.abs(item.duration - durationSec) <= 3);
        if (durationMatches.length === 0) {
            return null;
        }
        // Sort: prefer syncedLyrics, then smallest duration delta
        durationMatches.sort((a, b) => {
            const aSynced = Boolean(a.syncedLyrics);
            const bSynced = Boolean(b.syncedLyrics);
            if (aSynced !== bSynced) {
                return aSynced ? -1 : 1;
            }
            const aDelta = Math.abs((a.duration || 0) - durationSec);
            const bDelta = Math.abs((b.duration || 0) - durationSec);
            return aDelta - bDelta;
        });
        return durationMatches[0];
    }
    // If duration was not provided, prefer syncedLyrics
    candidatesWithLyrics.sort((a, b) => {
        const aSynced = Boolean(a.syncedLyrics);
        const bSynced = Boolean(b.syncedLyrics);
        if (aSynced !== bSynced) {
            return aSynced ? -1 : 1;
        }
        return 0;
    });
    return candidatesWithLyrics[0];
}

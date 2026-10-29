export const LRCLIB_USER_AGENT = 'AuraLauncher (github.com/qutlawsoasis-debug/Aura-Launcher)';
export const LRCLIB_TIMEOUT_MS = 4000;
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
 */
export async function fetchLyricsFromLrclib(artist, title, durationSec, customFetch = fetch) {
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
        const res = await fetchWithTimeout(getUrl, LRCLIB_TIMEOUT_MS, customFetch);
        if (res.ok) {
            const data = await res.json();
            if (data && (data.syncedLyrics || data.plainLyrics)) {
                return data;
            }
        }
    }
    catch (err) {
        // If request aborted/timeout or network error, let caller handle timeout if needed
        if (err?.name === 'AbortError') {
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
        const searchRes = await fetchWithTimeout(searchUrl, LRCLIB_TIMEOUT_MS, customFetch);
        if (searchRes.ok) {
            items = await searchRes.json();
        }
    }
    catch (err) {
        if (err?.name === 'AbortError') {
            throw err;
        }
    }
    // If search with track_name & artist_name returned nothing, try query `q`
    if (!Array.isArray(items) || items.length === 0) {
        const qUrl = `https://lrclib.net/api/search?q=${encodeURIComponent(`${cleanA} ${cleanT}`)}`;
        try {
            const qRes = await fetchWithTimeout(qUrl, LRCLIB_TIMEOUT_MS, customFetch);
            if (qRes.ok) {
                items = await qRes.json();
            }
        }
        catch (err) {
            if (err?.name === 'AbortError') {
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
        // Выбор по длительности ±3 с
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

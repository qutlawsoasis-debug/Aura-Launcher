// Cyrillic vowels: а, е, ё, и, о, у, ы, э, ю, я + Ukrainian і, ї, є
// Latin vowels: a, e, i, o, u, y
const VOWELS_REGEX = /[aeiouyаеёиоуыэюяієї]/gi;
/**
 * Counts syllables based on Cyrillic and Latin vowels.
 * Minimum weight is 1.
 */
export function countSyllables(word) {
    if (!word)
        return 1;
    const matches = word.match(VOWELS_REGEX);
    const count = matches ? matches.length : 0;
    return Math.max(count, 1);
}
/**
 * Checks if a word is followed by a pause:
 * pause +120 ms after , . ! ? … (or ...)
 */
export function hasPauseAfter(word) {
    if (!word)
        return false;
    const trimmed = word.trim();
    // Strip trailing quotes or brackets to inspect the punctuation: e.g. 'hello,"' -> 'hello,'
    const stripped = trimmed.replace(/['"»”\)\]]+$/, '');
    return /[,\.!\?…]$/.test(stripped) || /\.{3}$/.test(stripped);
}
/**
 * Parses LRC timestamp string: mm:ss.xx or mm:ss.xxx into milliseconds.
 */
export function parseLrcTimestamp(mm, ss, frac) {
    const m = parseInt(mm, 10);
    const s = parseInt(ss, 10);
    let ms = 0;
    if (frac) {
        if (frac.length === 1)
            ms = parseInt(frac, 10) * 100;
        else if (frac.length === 2)
            ms = parseInt(frac, 10) * 10;
        else if (frac.length === 3)
            ms = parseInt(frac, 10);
        else
            ms = Math.round(parseFloat(`0.${frac}`) * 1000);
    }
    return m * 60000 + s * 1000 + ms;
}
/**
 * Distributes line duration across words by syllable weight,
 * adding +120 ms pause after , . ! ? …
 * The words and pauses strictly stay within [lineStartMs, lineStartMs + lineDurationMs].
 */
export function estimateLineWords(rawWords, lineStartMs, lineDurationMs) {
    if (!rawWords || rawWords.length === 0) {
        return [];
    }
    if (lineDurationMs <= 0) {
        return rawWords.map(w => ({
            t: lineStartMs,
            d: 0,
            w
        }));
    }
    const totalDuration = lineDurationMs;
    const weights = rawWords.map(w => countSyllables(w));
    const pauses = rawWords.map(w => (hasPauseAfter(w) ? 120 : 0));
    const totalWeight = weights.reduce((acc, w) => acc + w, 0);
    const totalPauses = pauses.reduce((acc, p) => acc + p, 0);
    // Reserve a minimum amount of singing time so pauses never completely consume the line
    const minRequiredSinging = Math.min(rawWords.length, totalDuration);
    let availableSinging = totalDuration - totalPauses;
    let pausesToUse = pauses.slice();
    if (availableSinging < minRequiredSinging) {
        const maxPauseBudget = Math.max(0, totalDuration - minRequiredSinging);
        const pauseScale = totalPauses > 0 ? maxPauseBudget / totalPauses : 0;
        pausesToUse = pauses.map(p => Math.floor(p * pauseScale));
        availableSinging = totalDuration - pausesToUse.reduce((acc, p) => acc + p, 0);
    }
    // Allocate durations proportional to syllable weight
    const durations = [];
    let allocated = 0;
    for (let i = 0; i < rawWords.length; i++) {
        if (i === rawWords.length - 1) {
            durations.push(Math.max(0, availableSinging - allocated));
        }
        else {
            const d = Math.max(0, Math.floor(availableSinging * (weights[i] / totalWeight)));
            durations.push(d);
            allocated += d;
        }
    }
    // Build LyricWord array with start times
    let currentT = lineStartMs;
    const result = [];
    for (let i = 0; i < rawWords.length; i++) {
        result.push({
            t: currentT,
            d: durations[i],
            w: rawWords[i]
        });
        currentT += durations[i] + pausesToUse[i];
    }
    return result;
}
const ENHANCED_TAG_REGEX = /<(\d{1,2}):(\d{2})(?:\.(\d{2,3}))?>/g;
/**
 * Parses enhanced LRC tags inside a line: <mm:ss.xx>
 * Applies globalOffset to all enhanced timestamps.
 */
export function parseEnhancedLineWords(lineContent, lineStartMs, lineDurationMs, globalOffset = 0) {
    // Extract all tag positions
    const tags = [];
    let match;
    const regex = new RegExp(ENHANCED_TAG_REGEX.source, 'g');
    while ((match = regex.exec(lineContent)) !== null) {
        const rawTagTime = parseLrcTimestamp(match[1], match[2], match[3]);
        tags.push({
            index: match.index,
            length: match[0].length,
            timeMs: Math.max(0, rawTagTime + globalOffset)
        });
    }
    const cleanText = lineContent
        .replace(new RegExp(ENHANCED_TAG_REGEX.source, 'g'), '')
        .replace(/\s+/g, ' ')
        .trim();
    if (tags.length === 0) {
        const rawWords = cleanText.split(/\s+/).filter(w => w.length > 0);
        return {
            text: cleanText,
            words: estimateLineWords(rawWords, lineStartMs, lineDurationMs)
        };
    }
    const resultWords = [];
    const lineEndMs = lineStartMs + Math.max(0, lineDurationMs);
    // Segment before first tag (if any)
    const prefixText = lineContent.substring(0, tags[0].index).trim();
    if (prefixText) {
        const segWords = prefixText.split(/\s+/).filter(w => w.length > 0);
        const segDuration = Math.max(0, tags[0].timeMs - lineStartMs);
        resultWords.push(...estimateLineWords(segWords, lineStartMs, segDuration));
    }
    // Segments between tags
    for (let i = 0; i < tags.length; i++) {
        const currentTag = tags[i];
        const nextTag = tags[i + 1];
        const segStart = currentTag.index + currentTag.length;
        const segEnd = nextTag ? nextTag.index : lineContent.length;
        const segText = lineContent.substring(segStart, segEnd).trim();
        if (!segText) {
            continue;
        }
        const segWords = segText.split(/\s+/).filter(w => w.length > 0);
        const segStartMs = currentTag.timeMs;
        const segEndMs = nextTag ? nextTag.timeMs : lineEndMs;
        const segDuration = Math.max(0, segEndMs - segStartMs);
        if (segWords.length === 1) {
            resultWords.push({
                t: segStartMs,
                d: segDuration,
                w: segWords[0]
            });
        }
        else {
            resultWords.push(...estimateLineWords(segWords, segStartMs, segDuration));
        }
    }
    return {
        text: cleanText,
        words: resultWords
    };
}
/**
 * Parses full LRC content (standard or enhanced LRC).
 */
export function parseLrc(lrcText, songDurationMs) {
    if (!lrcText || typeof lrcText !== 'string') {
        return { source: 'lrclib', synced: false, lines: [] };
    }
    const rawLines = lrcText.split(/\r?\n/);
    // Look for [offset:+/-ms] tag
    let globalOffset = 0;
    for (const line of rawLines) {
        const offsetMatch = line.match(/^\[offset:\s*([+-]?\d+)\s*\]/i);
        if (offsetMatch) {
            globalOffset = parseInt(offsetMatch[1], 10) || 0;
            break;
        }
    }
    const intermediateLines = [];
    const LINE_TAGS_REGEX = /^((?:\[\d{1,2}:\d{2}(?:\.\d{2,3})?\])+)(.*)$/;
    for (const line of rawLines) {
        const trimmed = line.trim();
        if (!trimmed)
            continue;
        const match = trimmed.match(LINE_TAGS_REGEX);
        if (!match)
            continue; // Skip metadata tags like [ti:Title] or plain lines
        const tagsPart = match[1];
        const content = match[2];
        const tagRegex = /\[(\d{1,2}):(\d{2})(?:\.(\d{2,3}))?\]/g;
        let tagMatch;
        while ((tagMatch = tagRegex.exec(tagsPart)) !== null) {
            const timeMs = parseLrcTimestamp(tagMatch[1], tagMatch[2], tagMatch[3]) + globalOffset;
            intermediateLines.push({
                timeMs: Math.max(0, timeMs),
                content
            });
        }
    }
    // If no timestamp tags were found, treat as plain unsynced lyrics
    if (intermediateLines.length === 0) {
        const plainLines = rawLines
            .map(l => l.trim())
            .filter(l => l.length > 0 && !/^\[[a-zA-Z]+:.*\]$/.test(l));
        return {
            source: 'lrclib',
            synced: false,
            lines: plainLines.map(text => ({
                t: 0,
                text,
                words: []
            }))
        };
    }
    // Sort chronologically
    intermediateLines.sort((a, b) => a.timeMs - b.timeMs);
    const parsedLines = [];
    for (let i = 0; i < intermediateLines.length; i++) {
        const current = intermediateLines[i];
        const next = intermediateLines[i + 1];
        // Line duration = until next line (maximum 8 s)
        let lineDurationMs;
        if (next) {
            const diff = next.timeMs - current.timeMs;
            lineDurationMs = Math.max(0, Math.min(diff, 8000));
        }
        else {
            if (songDurationMs && songDurationMs > current.timeMs) {
                lineDurationMs = Math.max(0, Math.min(songDurationMs - current.timeMs, 8000));
            }
            else {
                lineDurationMs = 4000;
            }
        }
        const hasEnhancedTags = /<(\d{1,2}):(\d{2})(?:\.(\d{2,3}))?>/.test(current.content);
        if (hasEnhancedTags) {
            const parsed = parseEnhancedLineWords(current.content, current.timeMs, lineDurationMs, globalOffset);
            parsedLines.push({
                t: current.timeMs,
                text: parsed.text,
                words: parsed.words
            });
        }
        else {
            const cleanText = current.content.trim();
            const rawWords = cleanText.split(/\s+/).filter(w => w.length > 0);
            const words = estimateLineWords(rawWords, current.timeMs, lineDurationMs);
            parsedLines.push({
                t: current.timeMs,
                text: cleanText,
                words
            });
        }
    }
    return {
        source: 'lrclib',
        synced: true,
        lines: parsedLines
    };
}

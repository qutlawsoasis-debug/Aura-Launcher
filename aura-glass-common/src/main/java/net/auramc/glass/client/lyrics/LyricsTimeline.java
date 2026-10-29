package net.auramc.glass.client.lyrics;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;

/**
 * High-performance timeline for song lyrics.
 * Uses binary search to locate current line, current word, and next N words
 * with zero heap allocations on the hot path (per-frame seek/update).
 */
public class LyricsTimeline {
    private final Lyrics lyrics;
    private final List<Line> lines;
    private final int lineCount;

    // Precomputed line arrays for binary search
    private final long[] lineStartTimes;
    private final long[] lineEndTimes;

    // Flattened words across all lines
    private final Word[] allWords;
    private final long[] wordStartTimes;
    private final long[] wordEndTimes;
    private final int[] wordLineIndices;

    // Mutable state updated during seek/update (zero allocations)
    private long currentPositionMs = -1L;
    private int currentLineIndex = -1;
    private Line currentLine = null;
    private int currentWordIndex = -1; // Index in allWords
    private Word currentWord = null;
    private boolean isWordActive = false;
    private int nextWordStartIndex = 0;

    public LyricsTimeline(Lyrics lyrics) {
        this.lyrics = lyrics != null ? lyrics : new Lyrics("", false, Collections.emptyList());
        this.lines = this.lyrics.lines();
        this.lineCount = lines.size();

        this.lineStartTimes = new long[lineCount];
        this.lineEndTimes = new long[lineCount];

        List<Word> wordAccumulator = new ArrayList<>();
        List<Integer> lineIndexAccumulator = new ArrayList<>();

        for (int i = 0; i < lineCount; i++) {
            Line line = lines.get(i);
            lineStartTimes[i] = line.t();

            long nextStart = (i + 1 < lineCount) ? lines.get(i + 1).t() : (line.t() + 8000L);
            long end = nextStart;
            if (!line.words().isEmpty()) {
                Word lastWord = line.words().get(line.words().size() - 1);
                end = Math.max(end, lastWord.end());
            }
            lineEndTimes[i] = end;

            for (Word w : line.words()) {
                wordAccumulator.add(w);
                lineIndexAccumulator.add(i);
            }
        }

        int totalWords = wordAccumulator.size();
        this.allWords = new Word[totalWords];
        this.wordStartTimes = new long[totalWords];
        this.wordEndTimes = new long[totalWords];
        this.wordLineIndices = new int[totalWords];

        for (int i = 0; i < totalWords; i++) {
            Word w = wordAccumulator.get(i);
            allWords[i] = w;
            wordStartTimes[i] = w.t();
            wordEndTimes[i] = w.end();
            wordLineIndices[i] = lineIndexAccumulator.get(i);
        }

        // Initialize at position 0
        seek(0L);
    }

    /**
     * Updates the timeline position in milliseconds.
     * Uses binary search with fast-path optimization for sequential playback.
     * Guaranteed zero memory allocations.
     *
     * @param positionMs Current audio track position in milliseconds
     */
    public void seek(long positionMs) {
        this.currentPositionMs = positionMs;

        // 1. Locate current line
        if (lineCount == 0 || positionMs < 0) {
            currentLineIndex = -1;
            currentLine = null;
        } else if (currentLineIndex >= 0 && currentLineIndex < lineCount
                && positionMs >= lineStartTimes[currentLineIndex]
                && positionMs < lineEndTimes[currentLineIndex]) {
            // Fast-path: position is still within current line
            currentLine = lines.get(currentLineIndex);
        } else if (positionMs < lineStartTimes[0]) {
            // Before first line
            currentLineIndex = -1;
            currentLine = null;
        } else if (positionMs >= lineEndTimes[lineCount - 1]) {
            // After all lines
            currentLineIndex = -1;
            currentLine = null;
        } else {
            // Binary search in lineStartTimes
            int res = Arrays.binarySearch(lineStartTimes, positionMs);
            int idx = res >= 0 ? res : -res - 2;
            if (idx >= 0 && idx < lineCount && positionMs < lineEndTimes[idx]) {
                currentLineIndex = idx;
                currentLine = lines.get(idx);
            } else {
                currentLineIndex = -1;
                currentLine = null;
            }
        }

        // 2. Locate current word in allWords
        int totalWords = allWords.length;
        if (totalWords == 0 || positionMs < 0) {
            currentWordIndex = -1;
            currentWord = null;
            isWordActive = false;
            nextWordStartIndex = 0;
            return;
        }

        if (positionMs < wordStartTimes[0]) {
            // Before the very first word
            currentWordIndex = -1;
            currentWord = null;
            isWordActive = false;
            nextWordStartIndex = 0;
            return;
        }

        if (positionMs >= wordEndTimes[totalWords - 1]) {
            // After the last word
            currentWordIndex = totalWords - 1;
            currentWord = allWords[totalWords - 1];
            isWordActive = false;
            nextWordStartIndex = totalWords;
            return;
        }

        // Binary search in wordStartTimes
        int res = Arrays.binarySearch(wordStartTimes, positionMs);
        int wIdx = res >= 0 ? res : -res - 2;

        if (wIdx >= 0 && wIdx < totalWords) {
            currentWordIndex = wIdx;
            currentWord = allWords[wIdx];
            isWordActive = positionMs < wordEndTimes[wIdx];
            nextWordStartIndex = wIdx + 1;
        } else {
            currentWordIndex = -1;
            currentWord = null;
            isWordActive = false;
            nextWordStartIndex = 0;
        }
    }

    /**
     * Alias for seek(positionMs) for API compatibility.
     */
    public void update(long positionMs) {
        seek(positionMs);
    }

    public long getCurrentPositionMs() {
        return currentPositionMs;
    }

    public Line getCurrentLine() {
        return currentLine;
    }

    public int getCurrentLineIndex() {
        return currentLineIndex;
    }

    public Word getCurrentWord() {
        return currentWord;
    }

    public int getCurrentWordIndex() {
        return currentWordIndex;
    }

    public boolean isWordActive() {
        return isWordActive;
    }

    public Word getActiveWord() {
        return isWordActive ? currentWord : null;
    }

    public Lyrics getLyrics() {
        return lyrics;
    }

    public int getTotalWords() {
        return allWords.length;
    }

    public int getTotalLines() {
        return lineCount;
    }

    /**
     * Copies the next words into the provided buffer with ZERO allocations.
     *
     * @param out Preallocated buffer to fill
     * @return Number of words written to the buffer
     */
    public int getNextWords(Word[] out) {
        return getNextWords(out, out != null ? out.length : 0);
    }

    /**
     * Copies up to maxCount next words into the provided buffer with ZERO allocations.
     *
     * @param out      Preallocated buffer to fill
     * @param maxCount Maximum number of words to copy
     * @return Number of words written to the buffer
     */
    public int getNextWords(Word[] out, int maxCount) {
        if (out == null || maxCount <= 0 || nextWordStartIndex >= allWords.length) {
            return 0;
        }
        int available = allWords.length - nextWordStartIndex;
        int count = Math.min(maxCount, Math.min(available, out.length));
        System.arraycopy(allWords, nextWordStartIndex, out, 0, count);
        return count;
    }

    /**
     * Returns the next word at relative offset from the upcoming words.
     * Offset 0 = immediate next word.
     * Zero allocations.
     */
    public Word getNextWord(int offset) {
        int idx = nextWordStartIndex + offset;
        if (idx >= 0 && idx < allWords.length) {
            return allWords[idx];
        }
        return null;
    }

    public int getNextWordsCount() {
        return Math.max(0, allWords.length - nextWordStartIndex);
    }

    /**
     * Allocates and returns a list of next N words (convenience method for tests / UI setup).
     */
    public List<Word> getNextWords(int n) {
        if (n <= 0 || nextWordStartIndex >= allWords.length) {
            return Collections.emptyList();
        }
        int count = Math.min(n, allWords.length - nextWordStartIndex);
        List<Word> result = new ArrayList<>(count);
        for (int i = 0; i < count; i++) {
            result.add(allWords[nextWordStartIndex + i]);
        }
        return result;
    }
}

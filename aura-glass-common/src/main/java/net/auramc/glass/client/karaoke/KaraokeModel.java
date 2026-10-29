package net.auramc.glass.client.karaoke;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/**
 * Lyrics model supporting word-level timestamps (lines/words with t, d)
 * matching the /api/lyrics format from w3.md.
 */
public final class KaraokeModel {

    public static final class KaraokeWord {
        public final long t; // Start time in ms
        public final long d; // Duration in ms
        public final String w; // Word text

        public KaraokeWord(long t, long d, String w) {
            this.t = t;
            this.d = Math.max(50L, d);
            this.w = w != null ? w : "";
        }

        public long end() {
            return t + d;
        }

        @Override
        public String toString() {
            return String.format("[%dms +%dms: %s]", t, d, w);
        }
    }

    public static final class KaraokeLine {
        public final long t; // Start time in ms
        public final String text;
        public final List<KaraokeWord> words;

        public KaraokeLine(long t, String text, List<KaraokeWord> words) {
            this.t = t;
            this.text = text != null ? text : "";
            this.words = words != null ? Collections.unmodifiableList(new ArrayList<>(words)) : Collections.emptyList();
        }

        public long end() {
            if (!words.isEmpty()) {
                return words.get(words.size() - 1).end();
            }
            return t + 3000L;
        }
    }

    public static final class KaraokeLyrics {
        public final String source;
        public final boolean synced;
        public final List<KaraokeLine> lines;

        public KaraokeLyrics(String source, boolean synced, List<KaraokeLine> lines) {
            this.source = source != null ? source : "unknown";
            this.synced = synced;
            this.lines = lines != null ? Collections.unmodifiableList(new ArrayList<>(lines)) : Collections.emptyList();
        }

        public boolean isEmpty() {
            return lines.isEmpty();
        }

        public int totalWords() {
            int count = 0;
            for (KaraokeLine line : lines) {
                count += line.words.size();
            }
            return count;
        }
    }

    private KaraokeModel() {}

    /**
     * Parses JSON lyrics matching the /api/lyrics endpoint:
     * { "source": "lrclib", "synced": true, "lines": [ { "t": 1000, "text": "...", "words": [...] } ] }
     */
    public static KaraokeLyrics fromJson(String json) {
        if (json == null || json.isBlank()) {
            return new KaraokeLyrics("empty", false, Collections.emptyList());
        }

        try {
            JsonObject root = JsonParser.parseString(json).getAsJsonObject();
            String source = root.has("source") ? root.get("source").getAsString() : "unknown";
            boolean synced = !root.has("synced") || root.get("synced").getAsBoolean();

            List<KaraokeLine> lines = new ArrayList<>();
            if (root.has("lines") && root.get("lines").isJsonArray()) {
                JsonArray linesArray = root.getAsJsonArray("lines");
                for (int i = 0; i < linesArray.size(); i++) {
                    JsonObject lineObj = linesArray.get(i).getAsJsonObject();
                    long lineT = lineObj.has("t") ? lineObj.get("t").getAsLong() : 0L;
                    String lineText = lineObj.has("text") ? lineObj.get("text").getAsString() : "";

                    List<KaraokeWord> words = new ArrayList<>();
                    if (lineObj.has("words") && lineObj.get("words").isJsonArray()) {
                        JsonArray wordsArray = lineObj.getAsJsonArray("words");
                        for (JsonElement we : wordsArray) {
                            if (!we.isJsonObject()) continue;
                            JsonObject wo = we.getAsJsonObject();
                            long wt = wo.has("t") ? wo.get("t").getAsLong() : lineT;
                            long wd = wo.has("d") ? wo.get("d").getAsLong() : 300L;
                            String ww = wo.has("w") ? wo.get("w").getAsString() : "";
                            if (!ww.isBlank()) {
                                words.add(new KaraokeWord(wt, wd, ww.trim()));
                            }
                        }
                    }

                    // Fallback word estimation if line has text but no words
                    if (words.isEmpty() && !lineText.isBlank()) {
                        long nextLineT = (i + 1 < linesArray.size() && linesArray.get(i + 1).isJsonObject() && linesArray.get(i + 1).getAsJsonObject().has("t"))
                                ? linesArray.get(i + 1).getAsJsonObject().get("t").getAsLong()
                                : lineT + 4000L;
                        words = estimateWordsForLine(lineText, lineT, nextLineT);
                    }

                    if (!words.isEmpty() || !lineText.isBlank()) {
                        lines.add(new KaraokeLine(lineT, lineText, words));
                    }
                }
            }

            return new KaraokeLyrics(source, synced, lines);
        } catch (Exception e) {
            return new KaraokeLyrics("error", false, Collections.emptyList());
        }
    }

    private static final Pattern VOWELS_PATTERN = Pattern.compile("[аеёиоуыэюяaeiouyАЕЁИОУЫЭЮЯAEIOUY]");
    private static final Pattern PUNCT_PAUSE_PATTERN = Pattern.compile("[,.!?;:…]");

    /**
     * Syllable-based timing estimation algorithm per w3.md:
     * Line duration = up to next line (capped at 8000 ms), distributed by syllable weight (vowels count >= 1),
     * with +120 ms pause after punctuation marks.
     */
    public static List<KaraokeWord> estimateWordsForLine(String lineText, long lineStartMs, long nextLineStartMs) {
        List<KaraokeWord> result = new ArrayList<>();
        String[] tokens = lineText.trim().split("\\s+");
        if (tokens.length == 0 || tokens[0].isBlank()) {
            return result;
        }

        long availableDuration = Math.min(8000L, Math.max(1000L, nextLineStartMs - lineStartMs));
        int totalWeight = 0;
        int[] weights = new int[tokens.length];
        boolean[] hasPause = new boolean[tokens.length];

        for (int i = 0; i < tokens.length; i++) {
            String token = tokens[i];
            Matcher m = VOWELS_PATTERN.matcher(token);
            int vowels = 0;
            while (m.find()) vowels++;
            weights[i] = Math.max(1, vowels);
            totalWeight += weights[i];

            if (PUNCT_PAUSE_PATTERN.matcher(token).find()) {
                hasPause[i] = true;
            }
        }

        long currentT = lineStartMs;
        for (int i = 0; i < tokens.length; i++) {
            long wordDuration = (long) ((double) weights[i] / totalWeight * availableDuration);
            wordDuration = Math.max(120L, wordDuration);
            long pause = hasPause[i] ? 120L : 0L;
            result.add(new KaraokeWord(currentT, wordDuration - (pause > 0 ? 50L : 0L), tokens[i]));
            currentT += wordDuration + pause;
        }

        return result;
    }
}

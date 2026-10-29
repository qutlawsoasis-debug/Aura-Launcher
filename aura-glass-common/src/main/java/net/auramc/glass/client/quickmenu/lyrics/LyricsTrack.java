package net.auramc.glass.client.quickmenu.lyrics;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.regex.Pattern;

public final class LyricsTrack {
    private static final Pattern LINE = Pattern.compile("\\[(\\d+):(\\d{2})(?:[.:](\\d{1,3}))?]");
    private static final Pattern WORD = Pattern.compile("<(\\d+):(\\d{2})(?:[.:](\\d{1,3}))?>");
    private final List<Word> words;
    public record Word(int index, int phrase, String text, long startMs, long endMs, boolean enhanced) {}
    private record Phrase(long time, String text) {}
    private LyricsTrack(List<Word> words) { this.words = List.copyOf(words); }
    public List<Word> words() { return words; }
    public Word activeWord(long timeMs) {
        int low = 0, high = words.size() - 1;
        while (low <= high) {
            int mid = (low + high) >>> 1;
            var word = words.get(mid);
            if (timeMs < word.startMs()) high = mid - 1;
            else if (timeMs >= word.endMs()) low = mid + 1;
            else return word;
        }
        return null;
    }
    public static LyricsTrack parse(String source) {
        var phrases = new ArrayList<Phrase>();
        if (source == null) return new LyricsTrack(List.of());
        for (String line : source.split("\\R")) {
            var matcher = LINE.matcher(line);
            var times = new ArrayList<Long>(); int end = 0;
            while (matcher.find() && (matcher.start() == end || end == 0 && matcher.start() == 0)) {
                times.add(time(matcher.group(1), matcher.group(2), matcher.group(3))); end = matcher.end();
            }
            if (times.isEmpty()) continue;
            String text = line.substring(end).strip();
            for (long stamp : times) phrases.add(new Phrase(stamp, text));
        }
        phrases.sort(Comparator.comparingLong(Phrase::time));
        var result = new ArrayList<Word>();
        for (int phrase = 0; phrase < phrases.size(); phrase++) {
            var current = phrases.get(phrase);
            long finish = phrase + 1 < phrases.size() ? phrases.get(phrase + 1).time() : current.time() + 4000;
            var markers = WORD.matcher(current.text());
            var starts = new ArrayList<Long>(); var texts = new ArrayList<String>();
            int previousEnd = -1;
            while (markers.find()) {
                if (previousEnd >= 0) texts.add(current.text().substring(previousEnd, markers.start()).strip());
                starts.add(time(markers.group(1), markers.group(2), markers.group(3))); previousEnd = markers.end();
            }
            if (previousEnd >= 0) {
                texts.add(current.text().substring(previousEnd).strip());
                for (int i = 0; i < starts.size(); i++) {
                    String text = texts.get(i);
                    if (text.isBlank()) continue;
                    long start = starts.get(i), stop = i + 1 < starts.size() ? starts.get(i + 1) : Math.max(start + 80, finish);
                    String[] tokens = text.split("\\s+");
                    appendWeighted(result, phrase, tokens, start, Math.max(start + 1, stop), true);
                }
            } else if (!current.text().isBlank()) {
                appendWeighted(result, phrase, current.text().split("\\s+"), current.time(), Math.max(current.time() + 1, finish), false);
            }
        }
        result.sort(Comparator.comparingLong(Word::startMs));
        var indexed = new ArrayList<Word>();
        for (var word : result) indexed.add(new Word(indexed.size(), word.phrase(), word.text(), word.startMs(), word.endMs(), word.enhanced()));
        return new LyricsTrack(indexed);
    }
    private static void appendWeighted(List<Word> output, int phrase, String[] words, long start, long stop, boolean enhanced) {
        double total = 0; for (String word : words) total += weight(word);
        double used = 0;
        for (String word : words) {
            long onset = start + Math.round((stop - start) * used / total); used += weight(word);
            long end = start + Math.round((stop - start) * used / total);
            output.add(new Word(output.size(), phrase, word, onset, Math.max(onset + 1, end), enhanced));
        }
    }
    private static double weight(String word) {
        long vowels = word.toLowerCase(java.util.Locale.ROOT).codePoints().filter(c -> "aeiouyаеёиоуыэюя".indexOf(c) >= 0).count();
        double value = Math.max(1, vowels);
        if (word.matches(".*[.!?…]$")) value += 1.5;
        else if (word.matches(".*[,;:]$")) value += .65;
        return value;
    }
    private static long time(String minutes, String seconds, String fraction) {
        int ms = fraction == null ? 0 : Integer.parseInt((fraction + "000").substring(0, 3));
        return Long.parseLong(minutes) * 60_000 + Long.parseLong(seconds) * 1000 + ms;
    }
}
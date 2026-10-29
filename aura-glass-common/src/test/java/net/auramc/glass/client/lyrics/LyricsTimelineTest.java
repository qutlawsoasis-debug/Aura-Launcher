package net.auramc.glass.client.lyrics;

import java.util.Collections;
import java.util.List;

public final class LyricsTimelineTest {
    public static void main(String[] args) {
        testWordBoundaries();
        testSeekForwardAndBackward();
        testNextWordsZeroAllocation();
        testEmptyAndEdgeCases();
        System.out.println("LyricsTimelineTest: PASS");
    }

    private static Lyrics buildSampleLyrics() {
        // Line 0: [1000..3000]
        // Word 0: "Never" [1000..1500] (gap 100ms)
        // Word 1: "gonna" [1600..2200]
        Line line0 = new Line(1000L, "Never gonna", List.of(
                new Word(1000L, 500L, "Never"),
                new Word(1600L, 600L, "gonna")
        ));

        // Line 1: [5000..8000]
        // Word 2: "give" [5000..5500]
        // Word 3: "you" [5600..6000]
        // Word 4: "up" [6100..6700]
        Line line1 = new Line(5000L, "give you up", List.of(
                new Word(5000L, 500L, "give"),
                new Word(5600L, 400L, "you"),
                new Word(6100L, 600L, "up")
        ));

        return new Lyrics("lrclib", true, List.of(line0, line1));
    }

    public static void testWordBoundaries() {
        Lyrics lyrics = buildSampleLyrics();
        LyricsTimeline timeline = new LyricsTimeline(lyrics);

        // Position 500 ms (before line 0 starts at 1000 ms)
        timeline.seek(500L);
        check(timeline.getCurrentLine() == null, "pos 500: no line active");
        check(timeline.getCurrentWord() == null, "pos 500: no word active");
        check(!timeline.isWordActive(), "pos 500: word not active");
        check(timeline.getNextWordsCount() == 5, "pos 500: all 5 words upcoming");
        check("Never".equals(timeline.getNextWord(0).w()), "pos 500: next word is Never");

        // Position 1000 ms (exact start of Word 0 "Never")
        timeline.seek(1000L);
        check(timeline.getCurrentLine() != null, "pos 1000: line 0 active");
        check("Never gonna".equals(timeline.getCurrentLine().text()), "pos 1000: line text");
        check(timeline.getCurrentWord() != null, "pos 1000: word active");
        check("Never".equals(timeline.getCurrentWord().w()), "pos 1000: word is Never");
        check(timeline.isWordActive(), "pos 1000: word is active");
        check("gonna".equals(timeline.getNextWord(0).w()), "pos 1000: next word is gonna");

        // Position 1250 ms (middle of Word 0)
        timeline.seek(1250L);
        check("Never".equals(timeline.getCurrentWord().w()), "pos 1250: word is Never");
        check(timeline.isWordActive(), "pos 1250: word is active");

        // Position 1500 ms (exact end of Word 0: [1000..1500), word ends at 1500)
        timeline.seek(1500L);
        check("Never".equals(timeline.getCurrentWord().w()), "pos 1500: word was Never");
        check(!timeline.isWordActive(), "pos 1500: word has ended (in gap)");
        check("gonna".equals(timeline.getNextWord(0).w()), "pos 1500: next word is gonna");

        // Position 1550 ms (in pause gap between Never and gonna: [1500..1600))
        timeline.seek(1550L);
        check(!timeline.isWordActive(), "pos 1550: inside pause gap");
        check("gonna".equals(timeline.getNextWord(0).w()), "pos 1550: upcoming word is gonna");

        // Position 1600 ms (exact start of Word 1 "gonna")
        timeline.seek(1600L);
        check("gonna".equals(timeline.getCurrentWord().w()), "pos 1600: word is gonna");
        check(timeline.isWordActive(), "pos 1600: word active");
        check("give".equals(timeline.getNextWord(0).w()), "pos 1600: next word is give");
    }

    public static void testSeekForwardAndBackward() {
        Lyrics lyrics = buildSampleLyrics();
        LyricsTimeline timeline = new LyricsTimeline(lyrics);

        // Seek forward to line 1: position 5600 ms ("you")
        timeline.seek(5600L);
        check(timeline.getCurrentLineIndex() == 1, "seek forward: line index 1");
        check("give you up".equals(timeline.getCurrentLine().text()), "seek forward: line text");
        check(timeline.getCurrentWord() != null, "seek forward: word not null");
        check("you".equals(timeline.getCurrentWord().w()), "seek forward: word is you");
        check(timeline.isWordActive(), "seek forward: word is active");
        check("up".equals(timeline.getNextWord(0).w()), "seek forward: next word is up");

        // Seek forward to position 6200 ms ("up")
        timeline.seek(6200L);
        check("up".equals(timeline.getCurrentWord().w()), "seek forward: word is up");
        check(timeline.isWordActive(), "seek forward: word is active");
        check(timeline.getNextWordsCount() == 0, "pos 6200: no words left after last word");

        // Seek backward to position 1050 ms ("Never")
        timeline.seek(1050L);
        check(timeline.getCurrentLineIndex() == 0, "seek backward: line index 0");
        check("Never gonna".equals(timeline.getCurrentLine().text()), "seek backward: line text");
        check("Never".equals(timeline.getCurrentWord().w()), "seek backward: word is Never");
        check(timeline.isWordActive(), "seek backward: word is active");

        // Seek backward before first line (pos 0)
        timeline.seek(0L);
        check(timeline.getCurrentLine() == null, "seek backward 0: line is null");
        check(timeline.getCurrentWord() == null, "seek backward 0: word is null");
        check(timeline.getNextWordsCount() == 5, "seek backward 0: all words upcoming");

        // Seek forward again to position 5000 ms ("give")
        timeline.seek(5000L);
        check(timeline.getCurrentLineIndex() == 1, "seek forward: line index 1");
        check("give".equals(timeline.getCurrentWord().w()), "seek forward: word is give");
    }

    public static void testNextWordsZeroAllocation() {
        Lyrics lyrics = buildSampleLyrics();
        LyricsTimeline timeline = new LyricsTimeline(lyrics);

        timeline.seek(1000L); // at "Never" (index 0)
        // Next words are: gonna (1), give (2), you (3), up (4) -> 4 words

        // Zero-allocation buffer
        Word[] buffer = new Word[10];
        int count = timeline.getNextWords(buffer, 3);
        check(count == 3, "buffer filled with 3 words");
        check("gonna".equals(buffer[0].w()), "buf[0] is gonna");
        check("give".equals(buffer[1].w()), "buf[1] is give");
        check("you".equals(buffer[2].w()), "buf[2] is you");

        // Test clamping when requesting more than available
        int countAll = timeline.getNextWords(buffer, 10);
        check(countAll == 4, "available clamped to 4 words");
        check("up".equals(buffer[3].w()), "buf[3] is up");

        // Test List convenience overload
        List<Word> list = timeline.getNextWords(2);
        check(list.size() == 2, "list size 2");
        check("gonna".equals(list.get(0).w()), "list[0] is gonna");
        check("give".equals(list.get(1).w()), "list[1] is give");
    }

    public static void testEmptyAndEdgeCases() {
        LyricsTimeline emptyTimeline = new LyricsTimeline(null);
        check(emptyTimeline.getTotalLines() == 0, "empty timeline lines == 0");
        check(emptyTimeline.getTotalWords() == 0, "empty timeline words == 0");

        emptyTimeline.seek(5000L);
        check(emptyTimeline.getCurrentLine() == null, "empty timeline line is null");
        check(emptyTimeline.getCurrentWord() == null, "empty timeline word is null");
        check(emptyTimeline.getNextWordsCount() == 0, "empty timeline next words count 0");

        Word[] buf = new Word[5];
        check(emptyTimeline.getNextWords(buf) == 0, "empty timeline buffer copy 0");

        // Plain unsynced lyrics without words
        Line plainLine = new Line(0L, "Plain line without timing", Collections.emptyList());
        Lyrics plainLyrics = new Lyrics("plain", false, List.of(plainLine));
        LyricsTimeline plainTimeline = new LyricsTimeline(plainLyrics);

        plainTimeline.seek(1000L);
        check(plainTimeline.getCurrentLine() != null, "plain line retrieved");
        check(plainTimeline.getCurrentWord() == null, "no words in plain line");
        check(plainTimeline.getNextWordsCount() == 0, "no next words in plain lyrics");
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("Check failed: " + message);
        }
    }
}

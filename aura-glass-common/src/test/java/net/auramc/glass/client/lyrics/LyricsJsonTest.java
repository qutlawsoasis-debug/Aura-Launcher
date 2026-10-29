package net.auramc.glass.client.lyrics;

import com.google.gson.JsonObject;

import java.util.List;

public final class LyricsJsonTest {
    public static void main(String[] args) {
        testBasicParsing();
        testRoundTrip();
        testNullAndEmptySafety();
        System.out.println("LyricsJsonTest: PASS");
    }

    public static void testBasicParsing() {
        String json = """
        {
          "source": "lrclib",
          "synced": true,
          "lines": [
            {
              "t": 1000,
              "text": "Hello world",
              "words": [
                { "t": 1000, "d": 400, "w": "Hello" },
                { "t": 1520, "d": 600, "w": "world" }
              ]
            },
            {
              "t": 3000,
              "text": "Second line",
              "words": [
                { "t": 3000, "d": 800, "w": "Second" },
                { "t": 3920, "d": 700, "w": "line" }
              ]
            }
          ]
        }
        """;

        Lyrics lyrics = Lyrics.parseJson(json);
        check(lyrics != null, "lyrics not null");
        check("lrclib".equals(lyrics.source()), "source matches");
        check(lyrics.synced(), "synced is true");
        check(lyrics.lines().size() == 2, "2 lines parsed");

        Line line0 = lyrics.lines().get(0);
        check(line0.t() == 1000L, "line0 t == 1000");
        check("Hello world".equals(line0.text()), "line0 text");
        check(line0.words().size() == 2, "line0 has 2 words");

        Word w0 = line0.words().get(0);
        check(w0.t() == 1000L, "w0 t");
        check(w0.d() == 400L, "w0 d");
        check(w0.end() == 1400L, "w0 end");
        check("Hello".equals(w0.w()), "w0 text");
        check(w0.isActive(1000L), "w0 active at start");
        check(w0.isActive(1200L), "w0 active in middle");
        check(!w0.isActive(1400L), "w0 inactive at end");
        check(!w0.isActive(999L), "w0 inactive before start");

        Word w1 = line0.words().get(1);
        check(w1.t() == 1520L, "w1 t");
        check(w1.d() == 600L, "w1 d");
        check("world".equals(w1.w()), "w1 text");
    }

    public static void testRoundTrip() {
        Word w = new Word(5000L, 800L, "Sample");
        Line line = new Line(5000L, "Sample text", List.of(w));
        Lyrics lyrics = new Lyrics("custom", true, List.of(line));

        JsonObject obj = lyrics.toJson();
        Lyrics restored = Lyrics.fromJson(obj);

        check(restored.equals(lyrics), "restored equals original");
        check("custom".equals(restored.source()), "source retained");
        check(restored.synced(), "synced retained");
        check(restored.lines().size() == 1, "lines size retained");
        check(restored.lines().get(0).words().get(0).d() == 800L, "word duration retained");
    }

    public static void testNullAndEmptySafety() {
        Lyrics empty = Lyrics.parseJson("{}");
        check("".equals(empty.source()), "empty source defaults to empty string");
        check(!empty.synced(), "empty synced defaults to false");
        check(empty.lines().isEmpty(), "empty lines defaults to empty list");

        Lyrics nullParsed = Lyrics.parseJson(null);
        check(nullParsed.lines().isEmpty(), "null parse returns empty lines");

        Word nullWord = new Word(0L, 0L, null);
        check("".equals(nullWord.w()), "null word text coerced to empty string");

        Line nullLine = new Line(0L, null, null);
        check("".equals(nullLine.text()), "null line text coerced to empty string");
        check(nullLine.words().isEmpty(), "null line words coerced to empty list");
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("Check failed: " + message);
        }
    }
}

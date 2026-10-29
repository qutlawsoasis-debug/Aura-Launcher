package net.auramc.glass.client.lyrics;

/**
 * Runner executing all lyrics unit tests.
 */
public final class LyricsTestSuite {
    public static void main(String[] args) throws Exception {
        System.out.println("=== Running Lyrics Unit Tests ===");

        LyricsJsonTest.main(args);
        LyricsCacheTest.main(args);
        LyricsTimelineTest.main(args);

        System.out.println("=== ALL LYRICS TESTS PASSED (3/3) ===");
    }
}

package net.auramc.glass.client.music;

import java.util.List;

public final class MusicSourceTest {
    public static void main(String[] args) {
        testInitialStateAndNow();
        testPlaybackControls();
        testSeekAndExtrapolation();
        testVolumeBounds();
        testSearch();
        System.out.println("MusicSource & StubMusicSource: state, controls, seek, volume, search PASS");
    }

    private static void testInitialStateAndNow() {
        MusicSource source = new StubMusicSource();
        check(source.available(), "Music source must be available");
        NowPlaying np = source.now();
        check(np != null, "NowPlaying must not be null");
        check(np.track() != null, "Track must not be null");
        check(!np.track().title().isBlank(), "Track must have title");
        check(np.track().durationMs() > 0, "Track must have positive duration");
    }

    private static void testPlaybackControls() {
        StubMusicSource source = new StubMusicSource();
        source.play();
        check(source.now().isPlaying(), "State must be playing after play()");

        source.pause();
        check(source.now().isPaused(), "State must be paused after pause()");

        Track first = source.now().track();
        source.next();
        Track second = source.now().track();
        check(!first.id().equals(second.id()), "Next track must differ from first");

        source.prev();
        check(source.now().track().id().equals(first.id()), "Prev must return to first track");
    }

    private static void testSeekAndExtrapolation() {
        StubMusicSource source = new StubMusicSource();
        source.seek(60_000);
        check(source.now().positionMs() == 60_000, "Seek position must be exact");

        source.play();
        try { Thread.sleep(50); } catch (InterruptedException ignored) {}
        check(source.now().currentPositionMs() >= 60_040, "Position must advance while playing");
    }

    private static void testVolumeBounds() {
        StubMusicSource source = new StubMusicSource();
        source.volume(-0.5f);
        check(source.now().volume() == 0.0f, "Volume cannot be negative");

        source.volume(1.5f);
        check(source.now().volume() == 1.0f, "Volume cannot exceed 1.0");

        source.volume(0.75f);
        check(Math.abs(source.now().volume() - 0.75f) < 0.001f, "Volume must set correctly");
    }

    private static void testSearch() {
        StubMusicSource source = new StubMusicSource();
        List<Track> all = source.search("");
        check(all.size() >= 5, "Search with empty query must return all tracks");

        List<Track> skies = source.search("skies");
        check(!skies.isEmpty(), "Search for 'skies' must find track");
        check(skies.get(0).title().contains("Skies"), "Result must contain search term");

        List<Track> none = source.search("nonexistent_random_query_xyz");
        check(none.isEmpty(), "Nonexistent query must return empty list");
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}

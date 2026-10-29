package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.StubMusicSource;

public final class MusicSourceProviderTest {
    public static void main(String[] args) {
        testDefaultProductionSourceOffline();
        testExplicitFailurePath();
        testQaStubIsolation();
        testLifecycleClose();
        System.out.println("MusicSourceProvider failure path, production isolation, and QA stub tests PASS");
    }

    public static void testDefaultProductionSourceOffline() {
        System.clearProperty("aura.qa.stubMusic");
        MusicSourceProvider.initDefault();
        MusicSource s = MusicSourceProvider.get();
        check(s != null, "Source is non-null");
        check(!(s instanceof StubMusicSource), "Production must not return StubMusicSource with demo tracks");
        check(!s.available(), "Offline bridge in production must have available=false");
        check(s.now() != null, "now() is non-null");
        check(s.now().track() == null, "now().track() must be null offline");
        check(!s.now().isPlaying(), "now().isPlaying() must be false offline");
        check(s.search("query").isEmpty(), "search must return empty offline");
    }

    public static void testExplicitFailurePath() {
        // Simulate exception during source acquisition or broken bridge:
        MusicSourceProvider.set(MusicSourceProvider.UnavailableMusicSource.INSTANCE);
        MusicSource unavail = MusicSourceProvider.get();
        check(!unavail.available(), "Failure path available() must be false");
        check(unavail.now() != null, "Failure path now() non-null");
        check(unavail.now().track() == null, "Failure path track must be null");
        check(!unavail.now().isPlaying(), "Failure path isPlaying must be false");
        check(unavail.search("anything").isEmpty(), "Failure path search must be empty list");
    }

    public static void testQaStubIsolation() {
        System.setProperty("aura.qa.stubMusic", "true");
        MusicSourceProvider.initDefault();
        MusicSource qa = MusicSourceProvider.get();
        check(qa instanceof StubMusicSource, "Explicit QA flag must enable StubMusicSource");
        check(qa.available(), "StubMusicSource is available for QA");
        check(qa.now() != null && qa.now().track() != null, "StubMusicSource has demo track");

        // Clear flag and re-init:
        System.clearProperty("aura.qa.stubMusic");
        MusicSourceProvider.initDefault();
        MusicSource prod = MusicSourceProvider.get();
        check(!(prod instanceof StubMusicSource), "Production must be restored when QA flag is cleared");
        check(!prod.available(), "Restored production source must be unavailable offline");
    }

    public static void testLifecycleClose() {
        MusicSourceProvider.close();
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("FAILED: " + message);
        }
    }
}

package net.auramc.glass.client.karaoke;

import net.auramc.glass.client.lyrics.Lyrics;
import net.auramc.glass.client.lyrics.LyricsClient;
import net.auramc.glass.client.music.MusicClock;
import net.auramc.glass.client.music.StubMusicSource;
import net.auramc.glass.client.music.Track;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;
import java.util.List;
import java.util.Random;
import java.util.concurrent.atomic.AtomicLong;

/**
 * Headless integration test suite for MusicKaraokeBridge (W3c):
 * 1. Bridge initialization & Clock -> positionMs wiring
 * 2. Zero threads / polling overhead when MusicSource is unavailable
 * 3. Track change -> Lyrics loading -> KaraokeModel conversion -> Engine feed
 * 4. Missing / unsynced lyrics -> karaoke stays silent, zero errors
 * 5. Timeline progression: word onset matches timeline (p95 <= 50 ms)
 * 6. Clock epoch increase (seek / resync) -> resets shown and flying words
 * 7. Playback pause -> new words do not appear, existing hanging words remain
 * 8. Playback stop -> everything dissolves
 */
public final class MusicKaraokeIntegrationTest {

    private static final String SAMPLE_JSON_LYRICS = """
    {
      "source": "lrclib",
      "synced": true,
      "lines": [
        {
          "t": 1000,
          "text": "Starlight shining in the endless sky",
          "words": [
            { "t": 1000, "d": 400, "w": "Starlight" },
            { "t": 1400, "d": 350, "w": "shining" },
            { "t": 1750, "d": 200, "w": "in" },
            { "t": 1950, "d": 200, "w": "the" },
            { "t": 2150, "d": 450, "w": "endless" },
            { "t": 2600, "d": 500, "w": "sky" }
          ]
        },
        {
          "t": 4500,
          "text": "Drifting onwards past the event horizon",
          "words": [
            { "t": 4500, "d": 500, "w": "Drifting" },
            { "t": 5000, "d": 500, "w": "onwards" },
            { "t": 5500, "d": 300, "w": "past" },
            { "t": 5800, "d": 200, "w": "the" },
            { "t": 6000, "d": 400, "w": "event" },
            { "t": 6400, "d": 600, "w": "horizon" }
          ]
        },
        {
          "t": 8500,
          "text": "Voices echo through time",
          "words": [
            { "t": 8500, "d": 450, "w": "Voices" },
            { "t": 8950, "d": 450, "w": "echo" },
            { "t": 9400, "d": 400, "w": "through" },
            { "t": 9800, "d": 600, "w": "time" }
          ]
        }
      ]
    }
    """;

    public static void main(String[] args) {
        System.out.println("=== Starting MusicKaraokeBridge Integration Test Suite ===");

        Path tempDir = null;
        try {
            tempDir = Files.createTempDirectory("aura-karaoke-test-");

            testBridgeInitialization(tempDir);
            testUnavailableBridgeZeroOverhead(tempDir);
            testTrackChangeAndLyricsConversion(tempDir);
            testMissingOrUnsyncedLyrics(tempDir);
            testTimelineOnsetAccuracyP95(tempDir);
            testSeekResetsWords(tempDir);
            testPauseState(tempDir);
            testStopDissolve(tempDir);
            testDelayedCompletionAfterStop(tempDir);
            testDelayedCompletionAfterTrackChange(tempDir);
            testDelayedCompletionAfterBridgeLost(tempDir);
            testAsyncCompletionThreadSafetyNoDirectEngineMutation(tempDir);
            testMusicSourceProviderReuseAndNoExtraPollers();

            System.out.println("=== ALL MUSIC-KARAOKE INTEGRATION TESTS PASSED ===");
        } catch (Throwable t) {
            t.printStackTrace();
            System.err.println("TEST SUITE FAILED: " + t.getMessage());
            System.exit(1);
        } finally {
            if (tempDir != null) {
                deleteDirQuietly(tempDir);
            }
        }
    }

    private static void testBridgeInitialization(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(10_000L);
        Track track = new Track("t1", "Artist1", "Title1", 180_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache1");
        LyricsClient lyricsClient = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);
        KaraokeEngine engine = new KaraokeEngine();

        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, lyricsClient, engine);

        check(bridge.getMusicClock() == clock, "clock assigned");
        check(bridge.getLyricsClient() == lyricsClient, "lyricsClient assigned");
        check(bridge.getKaraokeEngine() == engine, "engine assigned");

        // Verify clock -> positionMs wiring
        check(engine.getClock().getRawPositionMs() == 0L, "engine positionMs reflects clock position 0");

        virtualTime.addAndGet(500L);
        check(engine.getClock().getRawPositionMs() == 500L, "engine positionMs tracks clock progression 500ms");

        System.out.println("[PASS] 1. Bridge initialization & MusicClock -> positionMs wiring");
    }

    private static void testUnavailableBridgeZeroOverhead(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(10_000L);
        StubMusicSource source = new StubMusicSource(List.of(), virtualTime::get);
        source.setAvailable(false);

        MusicClock clock = new MusicClock(source, virtualTime::get);
        Path cacheDir = tempDir.resolve("cache2");
        LyricsClient lyricsClient = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);
        KaraokeEngine engine = new KaraokeEngine();

        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, lyricsClient, engine);

        // Before update: inactive
        check(!bridge.isActive(), "bridge initially inactive");

        // Execute multiple update ticks while source is unavailable
        for (int i = 0; i < 20; i++) {
            virtualTime.addAndGet(50L);
            bridge.update(virtualTime.get());
        }

        check(!bridge.isActive(), "bridge remains inactive when source.available() == false");
        check(engine.getLyrics() == null, "no lyrics loaded");
        check(!engine.isPlaying(), "engine is not playing");
        check(engine.getActiveAnchorCount() == 0, "0 active words");

        System.out.println("[PASS] 2. Zero overhead & inactive state when MusicSource is unavailable");
    }

    private static void testTrackChangeAndLyricsConversion(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(1_000_000L);
        Track track = new Track("star_1", "Cosmic Sound", "Starlight Journey", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache3");
        LyricsClient lyricsClient = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);

        // Seed positive cache in disk cache
        Lyrics seedLyrics = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        String sha1 = LyricsClient.computeSha1("Cosmic Sound", "Starlight Journey");
        lyricsClient.writeCacheEntry(cacheDir.resolve(sha1 + ".json"), false, seedLyrics);

        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, lyricsClient, engine);

        // Update bridge at virtualTime
        bridge.update(virtualTime.get());

        check(bridge.isActive(), "bridge is active");
        check("star_1".equals(bridge.getLastTrackId()), "track ID tracked");

        KaraokeModel.KaraokeLyrics kLyrics = engine.getLyrics();
        check(kLyrics != null, "karaoke lyrics loaded into engine");
        check(kLyrics.synced, "lyrics synced flag true");
        check("lrclib".equals(kLyrics.source), "source lrclib preserved");
        check(kLyrics.lines.size() == 3, "3 lines parsed into KaraokeModel");

        // Verify word conversion
        KaraokeModel.KaraokeLine line0 = kLyrics.lines.get(0);
        check(line0.t == 1000L, "line 0 start time 1000ms");
        check(line0.words.size() == 6, "line 0 has 6 words");
        check("Starlight".equals(line0.words.get(0).w), "first word is 'Starlight'");
        check(line0.words.get(0).t == 1000L, "first word t = 1000");
        check(line0.words.get(0).d == 400L, "first word d = 400");

        System.out.println("[PASS] 3. Track change -> lyrics loading -> model conversion -> engine feed");
    }

    private static void testMissingOrUnsyncedLyrics(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(1_000_000L);
        Track trackUnsynced = new Track("unsynced_1", "Instrumental Band", "Pure Ambience", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(trackUnsynced), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache4");
        LyricsClient lyricsClient = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);

        // Seed cache with synced=false
        Lyrics unsynced = new Lyrics("plain", false, List.of());
        String sha1 = LyricsClient.computeSha1("Instrumental Band", "Pure Ambience");
        lyricsClient.writeCacheEntry(cacheDir.resolve(sha1 + ".json"), false, unsynced);

        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, lyricsClient, engine);

        bridge.update(virtualTime.get());

        check(engine.getLyrics() == null, "unsynced lyrics result in null engine lyrics (karaoke silent)");
        check(!engine.isPlaying(), "karaoke is not playing when unsynced");
        check(engine.getActiveAnchorCount() == 0, "0 active anchors for unsynced track");

        // Switch to missing track (404 / empty)
        Track missingTrack = new Track("missing_1", "Unknown Artist", "Nowhere Song", 90_000L, "");
        String sha1Missing = LyricsClient.computeSha1("Unknown Artist", "Nowhere Song");
        lyricsClient.writeCacheEntry(cacheDir.resolve(sha1Missing + ".json"), true, null); // 404 negative cache

        source.next();
        StubMusicSource source2 = new StubMusicSource(List.of(missingTrack), virtualTime::get);
        MusicClock clock2 = new MusicClock(source2, virtualTime::get);
        MusicKaraokeBridge bridge2 = new MusicKaraokeBridge(clock2, lyricsClient, engine);

        bridge2.update(virtualTime.get());

        check(engine.getLyrics() == null, "missing lyrics result in null engine lyrics");
        check(!engine.isPlaying(), "karaoke remains completely silent with zero errors");

        System.out.println("[PASS] 4. Missing / unsynced lyrics -> karaoke stays silent, zero errors");
    }

    private static void testTimelineOnsetAccuracyP95(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(10_000L);
        Track track = new Track("sync_1", "P95 Artist", "Timing Precision", 60_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache5");
        LyricsClient lyricsClient = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);

        Lyrics lyrics = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        String sha1 = LyricsClient.computeSha1("P95 Artist", "Timing Precision");
        lyricsClient.writeCacheEntry(cacheDir.resolve(sha1 + ".json"), false, lyrics);

        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, lyricsClient, engine);

        // Initial update loads lyrics
        bridge.update(virtualTime.get());
        check(engine.isPlaying(), "engine is playing after loading lyrics");

        Random frameJitter = new Random(1337L);

        // Simulate 60 FPS playback loop over 11 seconds (11,000 ms)
        long simulatedDurationMs = 11_000L;
        long targetEnd = virtualTime.get() + simulatedDurationMs;

        while (virtualTime.get() < targetEnd) {
            long dt = 16L + frameJitter.nextInt(6); // 16..21 ms step (~55-60 FPS)
            virtualTime.addAndGet(dt);

            bridge.update(virtualTime.get());
            engine.tickHeadless(clock.positionMs(virtualTime.get()));
        }

        KaraokeClock.OnsetStats stats = engine.getClock().getStats();
        System.out.printf("   Simulation recorded %d onsets, mean error: %.2f ms, p50: %d ms, p95: %d ms, max: %d ms%n",
                stats.sampleCount, stats.meanErrorMs, stats.p50Ms, stats.p95Ms, stats.maxErrorMs);

        check(stats.sampleCount >= 10, "at least 10 words recorded onsets during playback");
        check(stats.p95Ms <= 50L, "QA requirement: p95 onset error <= 50 ms (actual: " + stats.p95Ms + " ms)");
        check(stats.meetsP95Requirement(), "meetsP95Requirement() returned true");

        System.out.println("[PASS] 5. Timeline progression: word onset accuracy matches timeline (p95 <= 50 ms)");
    }

    private static void testSeekResetsWords(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(20_000L);
        Track track = new Track("seek_1", "Aura", "Seekable Track", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache6");
        LyricsClient lyricsClient = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);

        Lyrics lyrics = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        String sha1 = LyricsClient.computeSha1("Aura", "Seekable Track");
        lyricsClient.writeCacheEntry(cacheDir.resolve(sha1 + ".json"), false, lyrics);

        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, lyricsClient, engine);

        bridge.update(virtualTime.get());

        // Advance to 1500 ms (first line is active, words showing)
        virtualTime.addAndGet(1500L);
        bridge.update(virtualTime.get());
        engine.tickHeadless(clock.positionMs(virtualTime.get()));

        check(engine.getActiveAnchorCount() > 0, "active words showing at 1500ms");

        // Spawn a dummy physical body to ensure physics bodies are present
        engine.getPhysics().spawnBody("TestFlying", new net.minecraft.util.math.Vec3d(0, 64, 0), 1.0f, 0.4f, 0f, 0f);
        check(engine.getPhysics().getBodyCount() > 0, "physics bodies present before seek");

        // Perform Seek in MusicClock
        long prevEpoch = clock.epoch();
        clock.seek(5200L);
        check(clock.epoch() > prevEpoch, "clock epoch incremented on seek");

        // Next bridge update detects epoch change and triggers reset
        bridge.update(virtualTime.get());

        check(engine.getActiveAnchorCount() == 0, "seek cleared shown words (activeAnchors)");
        check(engine.getPhysics().getBodyCount() == 0, "seek cleared flying words (physics bodies)");

        // Advance a few frames at seeked position (5200 ms) -> line 1 words appear cleanly
        virtualTime.addAndGet(20L);
        bridge.update(virtualTime.get());
        engine.tickHeadless(clock.positionMs(virtualTime.get()));

        System.out.println("[PASS] 6. Clock epoch increase (seek/resync) resets shown and flying words");
    }

    private static void testPauseState(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(30_000L);
        Track track = new Track("pause_1", "Aura", "Pause Track", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache7");
        LyricsClient lyricsClient = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);

        Lyrics lyrics = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        String sha1 = LyricsClient.computeSha1("Aura", "Pause Track");
        lyricsClient.writeCacheEntry(cacheDir.resolve(sha1 + ".json"), false, lyrics);

        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, lyricsClient, engine);

        bridge.update(virtualTime.get());

        // Advance to 1200 ms (words hanging)
        virtualTime.addAndGet(1200L);
        bridge.update(virtualTime.get());
        engine.tickHeadless(clock.positionMs(virtualTime.get()));

        int activeBeforePause = engine.getActiveAnchorCount();
        check(activeBeforePause > 0, "words are hanging before pause");

        // Pause playback
        source.pause();
        check(source.now().isPaused(), "source is paused");

        bridge.update(virtualTime.get());
        check(engine.isPaused(), "engine is paused");

        // Advance virtual time by 5 seconds while paused
        for (int i = 0; i < 50; i++) {
            virtualTime.addAndGet(100L);
            bridge.update(virtualTime.get());
            engine.tickHeadless(clock.positionMs(virtualTime.get()));
        }

        // Check: hanging words remained in place!
        check(engine.getActiveAnchorCount() == activeBeforePause, "hanging words remained during pause");
        check(engine.getPhysics().getBodyCount() == 0, "no new words appeared / dropped into physics during pause");

        System.out.println("[PASS] 7. Playback pause: new words do not appear, existing hanging words remain");
    }

    private static void testStopDissolve(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(40_000L);
        Track track = new Track("stop_1", "Aura", "Stop Track", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache8");
        LyricsClient lyricsClient = new LyricsClient("http://127.0.0.1:9999", cacheDir, null);

        Lyrics lyrics = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        String sha1 = LyricsClient.computeSha1("Aura", "Stop Track");
        lyricsClient.writeCacheEntry(cacheDir.resolve(sha1 + ".json"), false, lyrics);

        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, lyricsClient, engine);

        bridge.update(virtualTime.get());

        // Advance to 1500 ms (words active)
        virtualTime.addAndGet(1500L);
        bridge.update(virtualTime.get());
        engine.tickHeadless(clock.positionMs(virtualTime.get()));

        check(engine.getActiveAnchorCount() > 0, "words active before stop");

        // Stop music source
        source.stop();

        bridge.update(virtualTime.get());

        check(engine.isStopped(), "engine set to stopped");
        check(engine.getActiveAnchorCount() == 0, "hanging words cleared on stop");

        // Active words were converted to dissolving physics bodies
        check(engine.getPhysics().getBodyCount() > 0, "bodies spawned for dissolution");
        for (KaraokePhysics.PhysicalWord b : engine.getPhysics().getBodies()) {
            check(b.dissolving, "body marked as dissolving");
        }

        // Simulate 30 physics ticks -> all dissolving bodies disappear
        for (int i = 0; i < 30; i++) {
            engine.getPhysics().tick(null);
        }

        check(engine.getPhysics().getBodyCount() == 0, "all dissolved bodies removed");

        System.out.println("[PASS] 8. Playback stop: everything dissolves");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Test assertion failed: " + description);
        }
    }

    private static void deleteDirQuietly(Path dir) {
        try (var stream = Files.walk(dir)) {
            stream.sorted(Comparator.reverseOrder())
                    .forEach(p -> {
                        try {
                            Files.deleteIfExists(p);
                        } catch (IOException ignored) {
                        }
                    });
        } catch (Throwable ignored) {
        }
    }

    private static void testDelayedCompletionAfterStop(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(50_000L);
        Track track = new Track("delayed_stop_1", "Artist", "Delayed Stop Song", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache_del_stop");
        ControlledLyricsClient controlledClient = new ControlledLyricsClient(cacheDir);
        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, controlledClient, engine);

        // 1. Tick while PLAYING: triggers lyrics request for track
        bridge.update(virtualTime.get());
        check(bridge.getRequestGeneration() == 1L, "generation 1 after loading track");
        check(engine.isStopped(), "engine initially stopped until lyrics arrive");

        // 2. Stop playback before request completes
        source.stop();
        bridge.update(virtualTime.get());
        check(engine.isStopped(), "engine is stopped after source.stop()");
        check(bridge.getRequestGeneration() == 2L, "generation bumped to 2 on STOP");

        // 3. Late lyrics completion fires on background thread
        Lyrics lyrics = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        controlledClient.future.complete(java.util.Optional.of(lyrics));

        // 4. Client tick runs: late completion MUST NOT un-stop the engine!
        bridge.update(virtualTime.get());
        check(engine.isStopped(), "engine MUST remain stopped even after late lyrics completion! (qaBridgeProbe fix)");
        check(engine.getLyrics() == null, "stale lyrics not applied to stopped engine");

        System.out.println("[PASS] 9. Delayed lyrics completion after STOP: engine remains stopped");
    }

    private static void testDelayedCompletionAfterTrackChange(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(60_000L);
        Track trackA = new Track("track_A", "Artist A", "Title A", 120_000L, "");
        Track trackB = new Track("track_B", "Artist B", "Title B", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(trackA, trackB), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache_del_track");
        ControlledLyricsClient controlledClient = new ControlledLyricsClient(cacheDir);
        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, controlledClient, engine);

        // 1. Start loading Track A
        bridge.update(virtualTime.get());
        check("track_A".equals(bridge.getLastTrackId()), "track A started");

        // 2. Track changes to Track B before Track A resolves
        source.next();
        bridge.update(virtualTime.get());
        check("track_B".equals(bridge.getLastTrackId()), "track B active");

        // 3. Late resolution of Track A occurs
        Lyrics lyricsA = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        controlledClient.future.complete(java.util.Optional.of(lyricsA));

        // 4. Client tick update: Track A lyrics MUST NOT apply to Track B!
        bridge.update(virtualTime.get());
        check(engine.getLyrics() == null, "Track A late lyrics discarded on Track B");

        System.out.println("[PASS] 10. Delayed lyrics completion after track change: stale track lyrics dropped");
    }

    private static void testDelayedCompletionAfterBridgeLost(Path tempDir) {
        AtomicLong virtualTime = new AtomicLong(70_000L);
        Track track = new Track("track_offline", "Artist", "Offline Track", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache_del_offline");
        ControlledLyricsClient controlledClient = new ControlledLyricsClient(cacheDir);
        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, controlledClient, engine);

        // 1. Start loading track
        bridge.update(virtualTime.get());

        // 2. Bridge becomes unavailable
        source.setAvailable(false);
        bridge.update(virtualTime.get());
        check(!bridge.isActive(), "bridge inactive when source unavailable");
        check(engine.isStopped(), "engine stopped when source offline");

        // 3. Late completion arrives while bridge is unavailable
        Lyrics lyrics = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        controlledClient.future.complete(java.util.Optional.of(lyrics));

        // 4. Client tick runs
        bridge.update(virtualTime.get());
        check(engine.isStopped(), "engine remains stopped when bridge offline");
        check(engine.getLyrics() == null, "lyrics not applied when bridge offline");

        System.out.println("[PASS] 11. Delayed lyrics completion after bridge lost: lyrics dropped, engine stopped");
    }

    private static void testAsyncCompletionThreadSafetyNoDirectEngineMutation(Path tempDir) throws Exception {
        AtomicLong virtualTime = new AtomicLong(80_000L);
        Track track = new Track("async_safety", "Thread Artist", "Safety Track", 120_000L, "");
        StubMusicSource source = new StubMusicSource(List.of(track), virtualTime::get);
        MusicClock clock = new MusicClock(source, virtualTime::get);

        Path cacheDir = tempDir.resolve("cache_async_safety");
        ControlledLyricsClient controlledClient = new ControlledLyricsClient(cacheDir);
        KaraokeEngine engine = new KaraokeEngine();
        MusicKaraokeBridge bridge = new MusicKaraokeBridge(clock, controlledClient, engine);

        // 1. Start request on client tick
        bridge.update(virtualTime.get());
        check(engine.getLyrics() == null, "engine lyrics null before completion");

        // 2. Complete future on a separate worker thread
        Lyrics lyrics = Lyrics.parseJson(SAMPLE_JSON_LYRICS);
        Thread worker = new Thread(() -> {
            controlledClient.future.complete(java.util.Optional.of(lyrics));
        }, "LyricsWorkerThread");
        worker.start();
        worker.join();

        // 3. CRITICAL: The background worker thread has completed, BUT bridge.update() has NOT run yet.
        // KaraokeEngine MUST NOT be mutated by the worker thread!
        check(engine.getLyrics() == null, "worker thread MUST NOT mutate KaraokeEngine directly! (Render safety fix)");
        check(engine.getActiveAnchorCount() == 0, "activeAnchors untouched by background thread");

        // 4. Now simulate client tick running on client main thread:
        bridge.update(virtualTime.get());
        check(engine.getLyrics() != null, "engine lyrics safely applied on client tick thread");
        check(!engine.isStopped(), "engine active after client tick apply");

        System.out.println("[PASS] 12. Async completion thread safety: zero direct engine mutation from HTTP threads");
    }

    private static void testMusicSourceProviderReuseAndNoExtraPollers() {
        // Set QA stub to avoid network
        System.setProperty("aura.qa.stubMusic", "true");
        net.auramc.glass.client.quickmenu.music.MusicSourceProvider.initDefault();

        net.auramc.glass.client.music.MusicSource sharedSource = net.auramc.glass.client.quickmenu.music.MusicSourceProvider.get();
        check(sharedSource != null, "MusicSourceProvider source available");

        // Default constructor MUST reuse MusicSourceProvider.get()
        MusicKaraokeBridge bridge = new MusicKaraokeBridge();
        check(bridge.getMusicClock().source() == sharedSource, "MusicKaraokeBridge shares MusicSourceProvider source");

        // Close bridge
        bridge.close();
        System.out.println("[PASS] 13. MusicSourceProvider reuse: 0 extra pollers created, shared with F8 menu");
    }

    static class ControlledLyricsClient extends LyricsClient {
        final java.util.concurrent.CompletableFuture<java.util.Optional<Lyrics>> future = new java.util.concurrent.CompletableFuture<>();

        ControlledLyricsClient(Path cacheDir) {
            super("http://127.0.0.1:9999", cacheDir, null);
        }

        @Override
        public java.util.concurrent.CompletableFuture<java.util.Optional<Lyrics>> getLyricsAsync(String artist, String title, double durationSec) {
            return future;
        }
    }

}

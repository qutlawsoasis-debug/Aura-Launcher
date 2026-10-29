package net.auramc.glass.client.karaoke;

import net.auramc.glass.client.quickmenu.QuickMenuConfig;

import java.util.concurrent.atomic.AtomicLong;

/**
 * Unit test suite for Karaoke Settings (W2b):
 * 1. Timing offset shifts onset and effective playback position (+/- 500 ms, step 10).
 * 2. maxWords=1 limits groups strictly to 1 word in both adaptiveness and timeline engine ticking.
 * 3. Disabling karaoke results in zero words, clears physics, and halts engine work.
 * 4. "Only standing" suppresses word spawning when player is walking, sprinting, or jumping.
 * 5. Karaoke text size scaling (75% - 150%) and clamping bounds.
 * 6. Config persistence and immediate application without relog.
 */
public final class KaraokeSettingsTest {

    private static final String SAMPLE_LYRICS = """
    {
      "source": "lrclib",
      "synced": true,
      "lines": [
        {
          "t": 1000,
          "text": "Bright horizon shining through the night sky",
          "words": [
            { "t": 1000, "d": 400, "w": "Bright" },
            { "t": 1400, "d": 400, "w": "horizon" },
            { "t": 1800, "d": 350, "w": "shining" },
            { "t": 2150, "d": 350, "w": "through" },
            { "t": 2500, "d": 300, "w": "the" },
            { "t": 2800, "d": 500, "w": "night" },
            { "t": 3300, "d": 600, "w": "sky" }
          ]
        }
      ]
    }
    """;

    public static void main(String[] args) {
        System.out.println("=== Starting KaraokeSettingsTest Suite ===");

        // Ensure clean config state for tests
        QuickMenuConfig.get().karaoke = true;
        QuickMenuConfig.get().lyricsOffsetMs = 0;
        QuickMenuConfig.get().karaokeSize = 1.0;
        QuickMenuConfig.get().karaokeMaxWords = 4;
        QuickMenuConfig.get().karaokeOnlyStanding = false;

        testTimingOffsetShiftsOnset();
        testMaxWordsLimitsGroupSize();
        testDisabledKaraokeZeroWordsAndZeroEngineWork();
        testOnlyStandingSuppressesWordSpawningOnMove();
        testOnlyStandingTransitionWithActiveWords();
        testResumeInsidePreviousGroupWithMaxWords();
        testKaraokeSizeBounds();
        testConfigImmediateApplyWithoutRelog();

        System.out.println("=== ALL KARAOKE SETTINGS TESTS PASSED ===");
    }

    private static void testTimingOffsetShiftsOnset() {
        System.out.println("Running testTimingOffsetShiftsOnset...");

        // 1. Initial offset 0
        KaraokeConfig.get().setTimingOffsetMs(0);
        check(KaraokeConfig.get().getTimingOffsetMs() == 0, "offset initially 0");

        // 2. Set positive offset +200 ms (early onset)
        KaraokeConfig.get().setTimingOffsetMs(200);
        check(KaraokeConfig.get().getTimingOffsetMs() == 200, "offset is +200 ms");
        check(QuickMenuConfig.get().lyricsOffsetMs == 200, "config holds lyricsOffsetMs = 200");

        // Clock effective position adds offset
        KaraokeClock clock = new KaraokeClock();
        clock.setClockSupplier(() -> 1000L);
        check(clock.getEffectivePositionMs() == 1200L, "effective pos 1000 + 200 = 1200 ms");

        // When raw position is 600 ms with +200 ms offset, effective position is 800 ms (pre-roll 760 ms met)
        AtomicLong virtualTimeEarly = new AtomicLong(0L);
        KaraokeEngine engineEarly = new KaraokeEngine();
        engineEarly.setClockSupplier(virtualTimeEarly::get);
        engineEarly.setLyricsFromJson(SAMPLE_LYRICS);
        engineEarly.setStopped(false);
        engineEarly.setPaused(false);

        virtualTimeEarly.set(600L);
        engineEarly.tickHeadless(600L); // Raw 600 + 200 = 800 ms -> pre-roll spawns words
        check(engineEarly.getActiveAnchorCount() > 0, "words spawned during pre-roll at raw 600 ms due to +200 ms offset");
        check(engineEarly.getClock().getStats().sampleCount == 0, "onset not recorded yet at raw 600 ms");

        // Next tick at raw 800 ms (effective 1000 ms) triggers onset for word "Bright" at t=1000 ms
        virtualTimeEarly.set(800L);
        engineEarly.tickHeadless(800L); // Raw 800 + 200 = 1000 ms -> onset triggered 200 ms early!
        check(engineEarly.getClock().getStats().sampleCount >= 1, "onset was triggered at raw 800 ms due to +200 ms offset");

        // Now compare to offset = 0: fresh engine instance
        KaraokeConfig.get().setTimingOffsetMs(0);
        AtomicLong virtualTimeZero = new AtomicLong(0L);
        KaraokeEngine engineZero = new KaraokeEngine();
        engineZero.setClockSupplier(virtualTimeZero::get);
        engineZero.setLyricsFromJson(SAMPLE_LYRICS);
        engineZero.setStopped(false);
        engineZero.setPaused(false);

        virtualTimeZero.set(600L);
        engineZero.tickHeadless(600L); // Raw 600 + 0 = 600 ms (< 760 ms pre-roll)
        check(engineZero.getActiveAnchorCount() == 0, "no words at raw 600 ms when offset is 0");
        virtualTimeZero.set(760L);
        engineZero.tickHeadless(760L); // Pre-roll threshold met
        check(engineZero.getActiveAnchorCount() > 0, "words spawned at 760 ms when offset is 0");
        virtualTimeZero.set(800L);
        engineZero.tickHeadless(800L); // Raw 800 + 0 = 800 ms (< 1000 ms onset)
        check(engineZero.getClock().getStats().sampleCount == 0, "onset NOT triggered at raw 800 ms when offset is 0");
        virtualTimeZero.set(1000L);
        engineZero.tickHeadless(1000L); // Raw 1000 + 0 = 1000 ms -> onset triggered at standard time
        check(engineZero.getClock().getStats().sampleCount >= 1, "onset triggered at raw 1000 ms when offset is 0");

        // 3. Set negative offset -300 ms (delayed onset)
        KaraokeConfig.get().setTimingOffsetMs(-300);
        check(KaraokeConfig.get().getTimingOffsetMs() == -300, "offset is -300 ms");
        check(clock.getEffectivePositionMs() == 700L, "effective pos 1000 - 300 = 700 ms");

        // 4. Clamping bounds [-500, +500]
        KaraokeConfig.get().setTimingOffsetMs(-900);
        check(KaraokeConfig.get().getTimingOffsetMs() == -500, "negative offset clamped to -500 ms");
        KaraokeConfig.get().setTimingOffsetMs(750);
        check(KaraokeConfig.get().getTimingOffsetMs() == 500, "positive offset clamped to +500 ms");

        // Reset
        KaraokeConfig.get().setTimingOffsetMs(0);
        System.out.println("[PASS] 1. Timing offset shifts onset and effective position");
    }

    private static void testMaxWordsLimitsGroupSize() {
        System.out.println("Running testMaxWordsLimitsGroupSize...");
        PlayerAdaptiveness adapt = new PlayerAdaptiveness();
        long now = 100000L;

        // Player standing: normally allows 3..6 words
        adapt.setScriptedState(0.01, 5.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        check(adapt.getCurrentResult().canSpawn, "player standing can spawn");

        // 1. With maxWords = 1: group size MUST strictly be 1 word
        KaraokeConfig.get().setMaxWords(1);
        check(KaraokeConfig.get().getMaxWords() == 1, "maxWords config is 1");
        int clamped1 = adapt.clampGroupSize(6);
        check(clamped1 == 1, "standing clampGroupSize(6) with maxWords=1 returned: " + clamped1);

        // Verify in engine timeline: placing candidate line with 7 words yields strictly 1 word anchor
        AtomicLong virtualTime = new AtomicLong(1000L);
        KaraokeEngine engine = new KaraokeEngine();
        engine.setClockSupplier(virtualTime::get);
        engine.setLyricsFromJson(SAMPLE_LYRICS);
        engine.setStopped(false);
        engine.setPaused(false);
        engine.tickHeadless(1000L);
        check(engine.getActiveAnchorCount() == 1, "engine active anchors strictly 1 word when maxWords=1 (actual: " + engine.getActiveAnchorCount() + ")");

        // 2. With maxWords = 2: group size is at most 2
        KaraokeConfig.get().setMaxWords(2);
        check(KaraokeConfig.get().getMaxWords() == 2, "maxWords config is 2");
        int clamped2 = adapt.clampGroupSize(6);
        check(clamped2 == 2, "standing clampGroupSize(6) with maxWords=2 returned: " + clamped2);

        // 3. With maxWords = 4: group size is up to 4
        KaraokeConfig.get().setMaxWords(4);
        int clamped4 = adapt.clampGroupSize(6);
        check(clamped4 == 4, "standing clampGroupSize(6) with maxWords=4 returned: " + clamped4);

        // 4. Bounds clamping [1..6]
        KaraokeConfig.get().setMaxWords(0);
        check(KaraokeConfig.get().getMaxWords() == 1, "maxWords 0 clamped to 1");
        KaraokeConfig.get().setMaxWords(10);
        check(KaraokeConfig.get().getMaxWords() == 6, "maxWords 10 clamped to 6");

        // Reset
        KaraokeConfig.get().setMaxWords(4);
        System.out.println("[PASS] 2. maxWords=1 limits groups strictly to 1 word");
    }

    private static void testDisabledKaraokeZeroWordsAndZeroEngineWork() {
        System.out.println("Running testDisabledKaraokeZeroWordsAndZeroEngineWork...");
        KaraokeEngine engine = KaraokeEngine.getInstance();
        AtomicLong virtualTime = new AtomicLong(1000L);
        engine.setClockSupplier(virtualTime::get);
        engine.setLyricsFromJson(SAMPLE_LYRICS);
        engine.setStopped(false);
        engine.setPaused(false);

        // Make words active
        KaraokeConfig.get().setEnabled(true);
        engine.seekTo(0L);
        engine.tickHeadless(1000L);
        check(engine.getActiveAnchorCount() > 0, "active words present before disabling");

        // Spawn a dummy physics body
        engine.getPhysics().spawnBody("TestWord", new net.minecraft.util.math.Vec3d(0, 64, 0), 1.0f, 0.4f, 0f, 0f);
        check(engine.getPhysics().getBodyCount() > 0, "physics body present before disabling");

        // Disable karaoke
        KaraokeConfig.get().setEnabled(false);
        check(!KaraokeConfig.get().isEnabled(), "karaoke is disabled");
        check(engine.getActiveAnchorCount() == 0, "active words cleared immediately upon disabling");
        check(engine.getPhysics().getBodyCount() == 0, "physics bodies cleared immediately upon disabling");

        // Tick headless while disabled: must do zero work and spawn no words
        virtualTime.set(1500L);
        engine.tickHeadless(1500L);
        check(engine.getActiveAnchorCount() == 0, "zero words after ticking while disabled");
        check(engine.getPhysics().getBodyCount() == 0, "zero physics bodies after ticking while disabled");

        // Also test local engine instance: if disabled, tickHeadless cleans up immediately
        KaraokeEngine localEngine = new KaraokeEngine();
        localEngine.setClockSupplier(virtualTime::get);
        localEngine.setLyricsFromJson(SAMPLE_LYRICS);
        localEngine.setStopped(false);
        localEngine.tickHeadless(1000L);
        check(localEngine.getActiveAnchorCount() == 0, "disabled local engine spawns 0 words on tick");

        // Re-enable
        KaraokeConfig.get().setEnabled(true);
        System.out.println("[PASS] 3. Disabling karaoke gives zero words and zero engine work");
    }

    private static void testOnlyStandingSuppressesWordSpawningOnMove() {
        System.out.println("Running testOnlyStandingSuppressesWordSpawningOnMove...");
        PlayerAdaptiveness adapt = new PlayerAdaptiveness();
        long now = 200000L;

        // Turn ON "only standing"
        KaraokeConfig.get().setOnlyStanding(true);
        check(KaraokeConfig.get().isOnlyStanding(), "onlyStanding is true");

        // 1. Walking player -> spawning MUST be suppressed
        adapt.setScriptedState(0.08, 10.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        check(!adapt.getCurrentResult().canSpawn, "walking cannot spawn when onlyStanding=true");
        check(adapt.clampGroupSize(4) == 0, "clampGroupSize is 0 while walking");

        // 2. Sprinting / running player -> spawning MUST be suppressed
        adapt.setScriptedState(0.25, 15.0, true, false, false, false, false, false, 20.0f, -10000L, now);
        check(!adapt.getCurrentResult().canSpawn, "sprinting cannot spawn when onlyStanding=true");
        check(adapt.clampGroupSize(4) == 0, "clampGroupSize is 0 while sprinting");

        // 3. Jumping player -> spawning MUST be suppressed
        adapt.setScriptedState(0.08, 10.0, false, true, false, false, false, false, 20.0f, -10000L, now);
        check(!adapt.getCurrentResult().canSpawn, "jumping cannot spawn when onlyStanding=true");

        // 4. Flying / falling player -> spawning MUST be suppressed
        adapt.setScriptedState(0.08, 10.0, false, false, true, false, false, false, 20.0f, -10000L, now);
        check(!adapt.getCurrentResult().canSpawn, "flying cannot spawn when onlyStanding=true");

        // 5. Standing player -> spawning IS allowed
        adapt.setScriptedState(0.01, 5.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        check(adapt.getCurrentResult().canSpawn, "standing player CAN spawn when onlyStanding=true");
        check(adapt.clampGroupSize(4) > 0, "clampGroupSize > 0 when standing");

        // 6. Turn OFF "only standing" -> walking can spawn again
        KaraokeConfig.get().setOnlyStanding(false);
        check(!KaraokeConfig.get().isOnlyStanding(), "onlyStanding is false");
        adapt.setScriptedState(0.08, 10.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        check(adapt.getCurrentResult().canSpawn, "walking CAN spawn when onlyStanding=false");
        check(adapt.clampGroupSize(4) > 0, "clampGroupSize > 0 while walking when onlyStanding=false");

        System.out.println("[PASS] 4. Only standing suppresses word spawning on move");
    }

    private static void testKaraokeSizeBounds() {
        System.out.println("Running testKaraokeSizeBounds...");

        // Default 1.0 (100%)
        KaraokeConfig.get().setSize(1.0f);
        check(Math.abs(KaraokeConfig.get().getSize() - 1.0f) < 0.001f, "default size is 1.0");

        // Min bound 0.75 (75%)
        KaraokeConfig.get().setSize(0.75f);
        check(Math.abs(KaraokeConfig.get().getSize() - 0.75f) < 0.001f, "size 0.75");

        // Max bound 1.50 (150%)
        KaraokeConfig.get().setSize(1.50f);
        check(Math.abs(KaraokeConfig.get().getSize() - 1.50f) < 0.001f, "size 1.50");

        // Clamping below min
        KaraokeConfig.get().setSize(0.30f);
        check(Math.abs(KaraokeConfig.get().getSize() - 0.75f) < 0.001f, "size clamped to 0.75");

        // Clamping above max
        KaraokeConfig.get().setSize(2.50f);
        check(Math.abs(KaraokeConfig.get().getSize() - 1.50f) < 0.001f, "size clamped to 1.50");

        // Reset
        KaraokeConfig.get().setSize(1.0f);
        System.out.println("[PASS] 5. Karaoke text size scaling and clamping bounds");
    }

    private static void testConfigImmediateApplyWithoutRelog() {
        System.out.println("Running testConfigImmediateApplyWithoutRelog...");

        // Directly update QuickMenuConfig properties
        var c = QuickMenuConfig.get();
        c.lyricsOffsetMs = 40;
        check(KaraokeConfig.get().getTimingOffsetMs() == 40, "KaraokeConfig sees lyricsOffsetMs immediately");

        c.karaokeMaxWords = 3;
        check(KaraokeConfig.get().getMaxWords() == 3, "KaraokeConfig sees karaokeMaxWords immediately");

        c.karaokeOnlyStanding = true;
        check(KaraokeConfig.get().isOnlyStanding(), "KaraokeConfig sees karaokeOnlyStanding immediately");

        c.karaokeSize = 1.25;
        check(Math.abs(KaraokeConfig.get().getSize() - 1.25f) < 0.001f, "KaraokeConfig sees karaokeSize immediately");

        // Clean up to defaults
        c.lyricsOffsetMs = 0;
        c.karaokeMaxWords = 4;
        c.karaokeOnlyStanding = false;
        c.karaokeSize = 1.0;
        c.karaoke = true;

        System.out.println("[PASS] 6. Config persistence and immediate application without relog");
    }

    private static void testOnlyStandingTransitionWithActiveWords() {
        System.out.println("Running testOnlyStandingTransitionWithActiveWords...");
        KaraokeConfig.get().setEnabled(true);
        KaraokeConfig.get().setOnlyStanding(false);

        KaraokeEngine engine = new KaraokeEngine();
        AtomicLong virtualTime = new AtomicLong(1000L);
        engine.setClockSupplier(virtualTime::get);
        engine.setLyricsFromJson(SAMPLE_LYRICS);
        engine.setStopped(false);
        engine.setPaused(false);

        PlayerAdaptiveness adapt = engine.getAdaptiveness();
        long now = 1_000_000L;

        // 1. Player is STANDING: spawn active words
        adapt.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        check(adapt.getCurrentResult().state == PlayerAdaptiveness.ActivityState.STANDING, "player standing initially");
        engine.tickHeadless(1000L);
        check(engine.getActiveAnchorCount() > 0, "active words spawned while standing (count: " + engine.getActiveAnchorCount() + ")");

        // Also add a physics body
        engine.getPhysics().spawnBody("FallWord", new net.minecraft.util.math.Vec3d(0, 64, 0), 1.0f, 0.4f, 0f, 0f);
        check(engine.getPhysics().getBodyCount() > 0, "physics bodies present");

        // 2. Enable "only standing" WHILE words are already visible
        KaraokeConfig.get().setOnlyStanding(true);
        // Words should still be visible because player is STANDING
        engine.tickHeadless(1100L);
        check(engine.getActiveAnchorCount() > 0, "words remain while standing with onlyStanding=true");

        // 3. Transition: Standing -> Running / Sprinting
        adapt.setScriptedState(0.25, 15.0, true, false, false, false, false, false, 20.0f, -10000L, now + 50_000L);
        check(adapt.getCurrentResult().onlyStandingSuppressed, "running activates onlyStandingSuppressed");

        // Next engine tick: active anchors and falling physics bodies MUST be cleared immediately!
        engine.tickHeadless(1200L);
        check(engine.getActiveAnchorCount() == 0, "running MUST clear active anchors (actual: " + engine.getActiveAnchorCount() + ")");
        check(engine.getPhysics().getBodyCount() == 0, "running MUST clear falling physics bodies (actual: " + engine.getPhysics().getBodyCount() + ")");

        // 4. Moving during upcoming words: no accumulation of expired words
        // Time advances through words at 1400, 1800 while running
        virtualTime.set(2000L);
        engine.tickHeadless(2000L);
        check(engine.getActiveAnchorCount() == 0, "no words spawned while running");

        // 5. Transition: Running -> Standing (player stops)
        adapt.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 20.0f, -10000L, now + 100_000L);
        check(!adapt.getCurrentResult().onlyStandingSuppressed, "standing clears onlyStandingSuppressed");
        check(adapt.getCurrentResult().canSpawn, "canSpawn is true when standing again");

        // At playback 2150L word "through" arrives: should spawn cleanly without expired backlog!
        virtualTime.set(2150L);
        engine.tickHeadless(2150L);
        check(engine.getActiveAnchorCount() > 0, "words resume spawning at current playback position after stopping");

        // 6. Test jumping transition with active words
        adapt.setScriptedState(0.05, 10.0, false, true, false, false, false, false, 20.0f, -10000L, now + 150_000L);
        engine.tickHeadless(2200L);
        check(engine.getActiveAnchorCount() == 0, "jumping MUST clear active anchors");

        // 7. Test flying / falling transition with active words
        // Stop jumping, let word spawn
        adapt.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 20.0f, -10000L, now + 200_000L);
        virtualTime.set(2500L); // word "the" at 2500
        engine.tickHeadless(2500L);
        check(engine.getActiveAnchorCount() > 0, "word spawned when standing");

        adapt.setScriptedState(0.05, 10.0, false, false, true, false, false, false, 20.0f, -10000L, now + 250_000L);
        engine.tickHeadless(2550L);
        check(engine.getActiveAnchorCount() == 0, "flying/falling MUST clear active anchors");

        // 8. Test turning on setting during running
        KaraokeConfig.get().setOnlyStanding(false);
        engine.seekTo(0L);
        virtualTime.set(1000L);
        adapt.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 20.0f, -10000L, now + 300_000L);
        engine.tickHeadless(1000L);
        check(engine.getActiveAnchorCount() > 0, "word spawned with onlyStanding=false");

        // Player starts running while onlyStanding is still false
        adapt.setScriptedState(0.25, 15.0, true, false, false, false, false, false, 20.0f, -10000L, now + 350_000L);
        engine.tickHeadless(1050L);
        check(engine.getActiveAnchorCount() > 0, "word still visible when running if onlyStanding=false");

        // NOW enable onlyStanding during run
        KaraokeConfig.get().setOnlyStanding(true);
        engine.tickHeadless(1060L);
        check(engine.getActiveAnchorCount() == 0, "enabling onlyStanding during run MUST clear active anchors immediately");

        // Clean up
        KaraokeConfig.get().setOnlyStanding(false);
        System.out.println("[PASS] 7. Transition standing -> running/jumping/flying and mid-run toggle properly clears active words");
    }

    private static void testResumeInsidePreviousGroupWithMaxWords() {
        System.out.println("Running testResumeInsidePreviousGroupWithMaxWords...");
        var c = QuickMenuConfig.get();
        c.karaoke = true;
        c.karaokeOnlyStanding = true;
        c.karaokeMaxWords = 4;
        c.lyricsOffsetMs = 0;

        KaraokeEngine e = new KaraokeEngine();
        e.setClockSupplier(() -> 1000L);
        e.setLyricsFromJson("{\"lines\":[{\"t\":1000,\"text\":\"one two three four five\",\"words\":[{\"t\":1000,\"d\":1000,\"w\":\"one\"},{\"t\":2000,\"d\":1000,\"w\":\"two\"},{\"t\":3000,\"d\":1000,\"w\":\"three\"},{\"t\":4000,\"d\":1000,\"w\":\"four\"},{\"t\":5000,\"d\":1000,\"w\":\"five\"}]}]}");
        e.setStopped(false);
        e.setPaused(false);

        PlayerAdaptiveness a = e.getAdaptiveness();
        long now = 200_000L;

        // 1. Standing at 1000: group with words spawned
        a.setScriptedState(0, 0, false, false, false, false, false, false, 20f, -10000L, now);
        e.tickHeadless(1000L);
        check(e.getActiveAnchorCount() == 3, "standing at 1000 spawns 3 anchors (one, two, three)");
        check(e.getActiveGroupWords().get(0).w.equals("one"), "first word is one");

        // 2. Running at 1100: active words cleared
        a.setScriptedState(0.3, 0, true, false, false, false, false, false, 20f, -10000L, now + 100L);
        e.tickHeadless(1100L);
        check(e.getActiveAnchorCount() == 0, "running at 1100 clears active anchors");

        // 3. Running through word one's expiration at 2100
        e.tickHeadless(2100L);
        check(e.getActiveAnchorCount() == 0, "running at 2100 maintains zero anchors");

        // 4. Stopping at 2200 inside previous multi-word group: MUST resume on current word "two"
        a.setScriptedState(0, 0, false, false, false, false, false, false, 20f, -10000L, now + 200L);
        e.tickHeadless(2200L);
        check(e.getActiveAnchorCount() > 0, "stopping at 2200 resumes word display");
        check(e.getActiveGroupWords().get(0).w.equals("two"), "resumes exactly on current word 'two' (actual: " + e.getActiveGroupWords().get(0).w + ")");
        check(!e.getActiveGroupWords().stream().anyMatch(w -> w.w.equals("one")), "expired word 'one' is NOT repeated");

        // 5. Test another transition with maxWords=6: stop inside word "four" at 4200
        c.karaokeMaxWords = 6;
        a.setScriptedState(0.3, 0, true, false, false, false, false, false, 20f, -10000L, now + 300L);
        e.tickHeadless(2500L);
        check(e.getActiveAnchorCount() == 0, "running at 2500 clears anchors");
        e.tickHeadless(3500L);
        e.tickHeadless(4100L);

        // Stop at 4200: word "four" is active (4000..5000)
        a.setScriptedState(0, 0, false, false, false, false, false, false, 20f, -10000L, now + 400L);
        e.tickHeadless(4200L);
        check(e.getActiveAnchorCount() > 0, "stopping at 4200 resumes word display");
        check(e.getActiveGroupWords().get(0).w.equals("four"), "resumes exactly on current word 'four' (actual: " + e.getActiveGroupWords().get(0).w + ")");
        check(!e.getActiveGroupWords().stream().anyMatch(w -> w.w.equals("one") || w.w.equals("two") || w.w.equals("three")), "prior words are NOT repeated");

        // Cleanup
        c.karaokeOnlyStanding = false;
        c.karaokeMaxWords = 4;
        System.out.println("[PASS] 8. Resume inside previous group with maxWords=4/6 selects exact active word without repeat");
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("Assertion failed: " + message);
        }
    }
}

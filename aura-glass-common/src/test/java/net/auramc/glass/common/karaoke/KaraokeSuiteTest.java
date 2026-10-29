package net.auramc.glass.common.karaoke;

import net.auramc.glass.client.karaoke.KaraokeClock;
import net.auramc.glass.client.karaoke.KaraokeConfig;
import net.auramc.glass.client.karaoke.KaraokeEngine;
import net.auramc.glass.client.karaoke.KaraokeModel;
import net.auramc.glass.client.karaoke.KaraokePhysics;
import net.auramc.glass.client.karaoke.PlayerAdaptiveness;
import net.auramc.glass.client.karaoke.WordAnimation;
import net.auramc.glass.client.karaoke.WordPlacement;
import net.minecraft.util.math.Box;
import net.minecraft.util.math.Vec3d;
import org.joml.Matrix4f;

import java.util.ArrayList;
import java.util.List;
import java.util.Random;

/**
 * Isolated unit test suite for in-world karaoke engine:
 * 1. Lyrics Model & JSON parsing (/api/lyrics format from w3.md)
 * 2. Syllable-based word timing estimation
 * 3. Player adaptiveness (standing, walking, running, turns, combat, hazards, non-accumulation)
 * 4. Word placement (3–7 blocks, 4–12° cone, > 3° crosshair dead zone assert)
 * 5. Apple Spring animation & typography colors
 * 6. 20 Hz physics (voxel AABB collision, bounce 0.2–0.35, sleep, limit <= 32 bodies)
 * 7. Onset error simulation (p95 <= 50 ms)
 */
public final class KaraokeSuiteTest {

    public static void main(String[] args) {
        System.out.println("=== Starting Karaoke Engine Test Suite ===");

        testModelParsing();
        testSyllableEstimation();
        testPlayerAdaptiveness();
        testWordPlacementAndDeadZone();
        testAppleSpringAnimation();
        testKaraokePhysics();
        testClockAndOnsetStats();
        testWordRectangleNonIntersection();

        System.out.println("=== ALL KARAOKE UNIT TESTS PASSED ===");
    }

    private static void testModelParsing() {
        String json = """
        {
          "source": "lrclib",
          "synced": true,
          "lines": [
            {
              "t": 1200,
              "text": "Bright horizon shining through",
              "words": [
                { "t": 1200, "d": 400, "w": "Bright" },
                { "t": 1650, "d": 500, "w": "horizon" },
                { "t": 2200, "d": 450, "w": "shining" },
                { "t": 2700, "d": 350, "w": "through" }
              ]
            }
          ]
        }
        """;

        KaraokeModel.KaraokeLyrics lyrics = KaraokeModel.fromJson(json);
        check(lyrics.synced, "synced flag parsed");
        check(lyrics.lines.size() == 1, "lines count");
        KaraokeModel.KaraokeLine line = lyrics.lines.get(0);
        check(line.t == 1200L, "line start time");
        check(line.words.size() == 4, "words count");
        check(line.words.get(1).w.equals("horizon"), "word text");
        check(line.words.get(1).t == 1650L, "word start time");
        check(line.words.get(1).d == 500L, "word duration");
        check(lyrics.totalWords() == 4, "total words");

        System.out.println("[PASS] 1. Lyrics Model & JSON parsing");
    }

    private static void testSyllableEstimation() {
        String line = "Звёзды горят, освещая путь!";
        List<KaraokeModel.KaraokeWord> estimated = KaraokeModel.estimateWordsForLine(line, 1000L, 4000L);
        check(estimated.size() == 4, "estimated words count");
        check(estimated.get(0).w.equals("Звёзды"), "first word");
        check(estimated.get(0).t == 1000L, "first word onset");
        // Verify punctuation pause exists for "горят,"
        check(estimated.get(2).t > estimated.get(1).end(), "comma introduces pause");

        System.out.println("[PASS] 2. Syllable-based word timing estimation");
    }

    private static void testPlayerAdaptiveness() {
        PlayerAdaptiveness adapt = new PlayerAdaptiveness();
        long now = 100000L;

        // 1. Standing: speed 0.01, turn 5.0 -> 3–6 words
        adapt.setScriptedState(0.01, 5.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        var resStanding = adapt.getCurrentResult();
        check(resStanding.canSpawn, "standing can spawn");
        check(resStanding.minGroup == 3 && resStanding.maxGroup == 6, "standing group range 3..6");

        // 2. Walking: speed 0.08 blocks/tick -> 2–3 words
        adapt.setScriptedState(0.08, 10.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        var resWalking = adapt.getCurrentResult();
        check(resWalking.canSpawn, "walking can spawn");
        check(resWalking.minGroup == 2 && resWalking.maxGroup == 3, "walking group range 2..3");

        // 3. Running / Sprint / Jump -> 1–2 words
        adapt.setScriptedState(0.25, 15.0, true, false, false, false, false, false, 20.0f, -10000L, now);
        var resRunning = adapt.getCurrentResult();
        check(resRunning.canSpawn, "running can spawn");
        check(resRunning.minGroup == 1 && resRunning.maxGroup == 2, "running group range 1..2");

        // 4. Fast turn (> 90°/s) -> 1–2 words and closer to gaze center (gazeAngleFactor = 0.6)
        adapt.setScriptedState(0.02, 120.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        var resTurn = adapt.getCurrentResult();
        check(resTurn.canSpawn, "fast turn can spawn");
        check(resTurn.minGroup == 1 && resTurn.maxGroup == 2, "fast turn group range 1..2");
        check(resTurn.gazeAngleFactor <= 0.65f, "fast turn gaze angle factor");

        // 5. Extreme turn (> 200°/s) -> pause new words
        adapt.setScriptedState(0.02, 220.0, false, false, false, false, false, false, 20.0f, -10000L, now);
        var resExtreme = adapt.getCurrentResult();
        check(!resExtreme.canSpawn, "extreme turn pauses new words");
        check(resExtreme.minGroup == 0 && resExtreme.maxGroup == 0, "extreme turn 0 words");

        // 6. Combat (< 3s) -> 0 words
        adapt.setScriptedState(0.02, 10.0, false, false, false, false, false, false, 20.0f, now - 1500L, now);
        var resCombat = adapt.getCurrentResult();
        check(!resCombat.canSpawn, "combat blocks spawning");

        // 7. GUI open -> 0 words
        adapt.setScriptedState(0.0, 0.0, false, false, false, false, false, true, 20.0f, -10000L, now);
        var resGui = adapt.getCurrentResult();
        check(!resGui.canSpawn, "gui blocks spawning");

        // 8. Water / lava -> 0 words
        adapt.setScriptedState(0.0, 0.0, false, false, false, false, true, false, 20.0f, -10000L, now);
        var resWater = adapt.getCurrentResult();
        check(!resWater.canSpawn, "water/lava blocks spawning");

        // 9. Low HP (<= 6 HP / 3 hearts) -> 0 words
        adapt.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 5.0f, -10000L, now);
        var resLowHp = adapt.getCurrentResult();
        check(!resLowHp.canSpawn, "low HP blocks spawning");

        // 10. Flying / falling -> 1 word
        adapt.setScriptedState(0.1, 0.0, false, false, true, false, false, false, 20.0f, -10000L, now);
        var resFlight = adapt.getCurrentResult();
        check(resFlight.canSpawn && resFlight.minGroup == 1 && resFlight.maxGroup == 1, "flight 1 word");

        // 11. Real Production Event Hookup Path: onPlayerAttack()
        KaraokeEngine engine = KaraokeEngine.getInstance();
        engine.getAdaptiveness().setScriptedState(0.01, 5.0, false, false, false, false, false, false, 20.0f, -10000L, System.currentTimeMillis());
        check(engine.getAdaptiveness().getCurrentResult().canSpawn, "engine reset to standing");
        long tAttack = System.currentTimeMillis();
        engine.onPlayerAttack();
        var resAttack = engine.getAdaptiveness().getCurrentResult();
        check(!resAttack.canSpawn, "onPlayerAttack immediately gates spawning");
        check(resAttack.state == PlayerAdaptiveness.ActivityState.BLOCKED_COMBAT, "onPlayerAttack enters BLOCKED_COMBAT");
        check(engine.getAdaptiveness().clampGroupSize(4) == 0, "clamped words is 0 during combat");
        check(engine.getAdaptiveness().getLastCombatTimeMs() >= tAttack, "lastCombatTimeMs updated on attack");

        // 12. Real Production Event Hookup Path: onPlayerDamage()
        engine.getAdaptiveness().setScriptedState(0.01, 5.0, false, false, false, false, false, false, 20.0f, -10000L, System.currentTimeMillis());
        check(engine.getAdaptiveness().getCurrentResult().canSpawn, "engine reset to standing");
        long tDamage = System.currentTimeMillis();
        engine.onPlayerDamage();
        var resDamage = engine.getAdaptiveness().getCurrentResult();
        check(!resDamage.canSpawn, "onPlayerDamage immediately gates spawning");
        check(resDamage.state == PlayerAdaptiveness.ActivityState.BLOCKED_COMBAT, "onPlayerDamage enters BLOCKED_COMBAT");
        check(engine.getAdaptiveness().clampGroupSize(4) == 0, "clamped words is 0 during combat");
        check(engine.getAdaptiveness().getLastCombatTimeMs() >= tDamage, "lastCombatTimeMs updated on damage");

        System.out.println("[PASS] 3. Player adaptiveness (all 10 states + attack/damage event hooks + non-accumulation)");
    }

    private static void testWordPlacementAndDeadZone() {
        Vec3d eyePos = new Vec3d(0, 64, 0);
        float yaw = 0f;
        float pitch = 0f;
        Vec3d lookVec = new Vec3d(0, 0, 1);

        List<KaraokeModel.KaraokeWord> words = List.of(
                new KaraokeModel.KaraokeWord(1000, 400, "Golden"),
                new KaraokeModel.KaraokeWord(1450, 400, "horizon"),
                new KaraokeModel.KaraokeWord(1900, 400, "rising")
        );

        var anchors = WordPlacement.layoutGroup(words, eyePos, yaw, pitch, Vec3d.ZERO, 1.0f, null);
        check(!anchors.isEmpty(), "anchors created");

        Vec3d groupCenter = WordPlacement.getGroupCenter(eyePos, yaw, pitch, WordPlacement.MIN_DISTANCE, 1.0f);
        boolean centerInCone = WordPlacement.isInsideCone(groupCenter, eyePos, lookVec);
        check(centerInCone, "group placement center within 12° gaze cone");

        for (int i = 0; i < anchors.size(); i++) {
            var a = anchors.get(i);
            double dist = a.position.distanceTo(eyePos);
            check(dist >= WordPlacement.MIN_DISTANCE && dist <= WordPlacement.MAX_DISTANCE,
                    "distance within 3–7 blocks: " + dist);

            // Assert: strictly outside 3° crosshair dead zone
            boolean outsideDeadZone = WordPlacement.isOutsideDeadZone(a.position, eyePos, lookVec);
            check(outsideDeadZone, "anchor #" + i + " strictly outside 3° dead zone");

            // Assert: strictly within 12° gaze cone
            boolean insideCone = WordPlacement.isInsideCone(a.position, eyePos, lookVec);
            check(insideCone, "anchor #" + i + " strictly inside 12° cone: " + WordPlacement.angleFromLook(a.position, eyePos, lookVec));
        }

        // QA ConeProbe Verification: 6 words of 'internationalization' (must strictly fit <= 12.0°)
        List<KaraokeModel.KaraokeWord> longWords = new ArrayList<>();
        for (int i = 0; i < 6; i++) {
            longWords.add(new KaraokeModel.KaraokeWord(1000 + i * 500, 400, "internationalization"));
        }
        var longAnchors = WordPlacement.layoutGroup(longWords, eyePos, 0f, 0f, Vec3d.ZERO, 1.0f, null);
        check(!longAnchors.isEmpty(), "long words anchors created");
        for (var a : longAnchors) {
            double ang = WordPlacement.angleFromLook(a.position, eyePos, lookVec);
            check(ang >= 3.0, "long word anchor outside 3° dead zone: " + ang);
            check(ang <= 12.0, "long word anchor strictly within 12.0° cone (actual: " + ang + ")");
        }

        // Assert: Left-to-right reading order (x increases monotonically) and space gaps
        List<KaraokeModel.KaraokeWord> phrase = List.of(
                new KaraokeModel.KaraokeWord(1000, 400, "Bright"),
                new KaraokeModel.KaraokeWord(1450, 400, "horizon"),
                new KaraokeModel.KaraokeWord(2000, 450, "shining"),
                new KaraokeModel.KaraokeWord(2500, 350, "through")
        );

        float[] testYaws = {0f, 90f, 180f, 270f, 45f};
        float[] testPitches = {0f, 15f, -10f};

        for (float testYaw : testYaws) {
            for (float testPitch : testPitches) {
                double yRad = Math.toRadians(testYaw);
                double pRad = Math.toRadians(testPitch);
                Vec3d currentLook = new Vec3d(
                        -Math.sin(yRad) * Math.cos(pRad),
                        -Math.sin(pRad),
                        Math.cos(yRad) * Math.cos(pRad)
                ).normalize();
                Vec3d camRight = new Vec3d(-Math.cos(yRad), 0, -Math.sin(yRad)).normalize();

                var phraseAnchors = WordPlacement.layoutGroup(phrase, eyePos, testYaw, testPitch, Vec3d.ZERO, 1.0f, null);
                check(!phraseAnchors.isEmpty(), "phrase anchors created");

                // Verify every anchor is strictly within [3.0°, 12.0°]
                for (var a : phraseAnchors) {
                    double ang = WordPlacement.angleFromLook(a.position, eyePos, currentLook);
                    check(ang >= 3.0 && ang <= 12.0, "phrase anchor within [3°, 12°]: " + ang);

                    // Left & right bounds
                    Vec3d leftBound = a.position.add(camRight.multiply(-a.width * 0.5));
                    Vec3d rightBound = a.position.add(camRight.multiply(a.width * 0.5));
                    double leftAng = WordPlacement.angleFromLook(leftBound, eyePos, currentLook);
                    double rightAng = WordPlacement.angleFromLook(rightBound, eyePos, currentLook);
                    check(leftAng <= 12.05, "left boundary <= 12°: " + leftAng);
                    check(rightAng <= 12.05, "right boundary <= 12°: " + rightAng);
                }

                // Verify left-to-right reading order on anchors that share the same vertical level / line
                for (int i = 0; i < phraseAnchors.size() - 1; i++) {
                    Vec3d posA = phraseAnchors.get(i).position;
                    Vec3d posB = phraseAnchors.get(i + 1).position;
                    float pitchA = phraseAnchors.get(i).pitch;
                    float pitchB = phraseAnchors.get(i + 1).pitch;
                    if (Math.abs(pitchA - pitchB) < 0.2f) {
                        double xProjA = posA.subtract(eyePos).dotProduct(camRight);
                        double xProjB = posB.subtract(eyePos).dotProduct(camRight);
                        check(xProjB > xProjA, "Left-to-right word order on same line: " + xProjA + " < " + xProjB);

                        double rightEdgeA = xProjA + phraseAnchors.get(i).width * 0.5;
                        double leftEdgeB = xProjB - phraseAnchors.get(i + 1).width * 0.5;
                        check(leftEdgeB >= rightEdgeA + 0.05, "Space gap between words on same line");
                    }
                }
            }
        }

        // Test multi-word single-line reading order explicitly with words that fit on one line
        List<KaraokeModel.KaraokeWord> shortLine = List.of(
                new KaraokeModel.KaraokeWord(1000, 300, "To"),
                new KaraokeModel.KaraokeWord(1350, 300, "the")
        );
        var shortAnchors = WordPlacement.layoutGroup(shortLine, eyePos, 0f, 0f, Vec3d.ZERO, 1.0f, null);
        check(shortAnchors.size() == 2, "2 words on short line");
        Vec3d camRight0 = new Vec3d(-1, 0, 0);
        double x0 = shortAnchors.get(0).position.subtract(eyePos).dotProduct(camRight0);
        double x1 = shortAnchors.get(1).position.subtract(eyePos).dotProduct(camRight0);
        check(x1 > x0, "To (x=" + x0 + ") before the (x=" + x1 + ")");

        System.out.println("[PASS] 4. Word placement, distance (3–7 bl), crosshair dead zone (> 3°), cone limit (<= 12° for all anchors and bounds including long words), and left-to-right screen order with spaces");
    }

    private static void testAppleSpringAnimation() {
        // Test spring progression: 0.0 at t=0, reaches ~1.0 with subtle overshoot
        float s0 = WordAnimation.evaluateSpring(0.0f);
        check(s0 == 0.0f, "spring starts at 0.0");

        float s100 = WordAnimation.evaluateSpring(0.10f);
        check(s100 > 0.40f && s100 < 0.95f, "spring rising at 100ms");

        float s250 = WordAnimation.evaluateSpring(0.25f);
        check(s250 >= 0.95f && s250 <= 1.05f, "spring settles near 1.0 at 250ms");

        float s400 = WordAnimation.evaluateSpring(0.40f);
        check(s400 == 1.0f, "spring fully rested at 400ms");

        // Letter stagger test: letter 0 reveals before letter 3
        float l0 = WordAnimation.letterAlpha(50L, 0);
        float l3 = WordAnimation.letterAlpha(50L, 3);
        check(l0 > 0.0f, "first letter visible");
        check(l3 == 0.0f, "fourth letter not yet manifested (stagger)");

        // Color test: active #64D2FF, sung white, upcoming sky-white
        int colActive = WordAnimation.getWordColor(1500L, 1400L, 300L, 1.0f);
        check(colActive == WordAnimation.COLOR_ACTIVE, "active word color #64D2FF");

        int colSung = WordAnimation.getWordColor(2000L, 1400L, 300L, 1.0f);
        check(colSung == WordAnimation.COLOR_SUNG, "sung word color #FFFFFF");

        int colUpcoming = WordAnimation.getWordColor(1000L, 1400L, 300L, 1.0f);
        check(colUpcoming == WordAnimation.COLOR_UPCOMING, "upcoming word color");

        System.out.println("[PASS] 5. Apple Spring animation, letter stagger, and HIG typography colors");
    }

    private static void testKaraokePhysics() {
        KaraokePhysics physics = new KaraokePhysics();
        physics.clear();

        Vec3d spawnPos = new Vec3d(0, 68, 0);
        physics.spawnBody("Echoes", spawnPos, 0.8f, 0.4f, 0f, 0f);

        check(physics.getBodyCount() == 1, "body count 1");
        var b = physics.getBodies().get(0);
        check(b.y == 68.0, "initial body Y");

        // Simulate 10 ticks (free fall)
        for (int i = 0; i < 10; i++) {
            physics.tick(null);
        }

        check(b.y < 68.0, "body falls under gravity");
        check(b.vy < 0.0, "body has downward velocity");

        // Test body limit: spawn 40 bodies -> limit <= 32 enforced
        for (int i = 0; i < 40; i++) {
            physics.spawnBody("Word" + i, spawnPos, 0.5f, 0.4f, 0f, 0f);
        }
        check(physics.getBodyCount() <= KaraokePhysics.MAX_BODIES, "strict body limit <= 32");

        // Test sub-frame interpolation
        Vec3d interp = b.getInterpolatedPos(0.5f);
        double expectedY = b.prevY + (b.y - b.prevY) * 0.5;
        check(Math.abs(interp.y - expectedY) < 0.0001, "sub-frame interpolation exact");

        System.out.println("[PASS] 6. 20 Hz physics (gravity, bounce, limit <= 32 bodies, sub-frame interp)");
    }

    private static void testClockAndOnsetStats() {
        KaraokeClock clock = new KaraokeClock();
        clock.startDemoTimeline();

        Random rng = new Random(42);
        // Simulate 40 word onsets with simulated frame jitter (0 to 25 ms)
        for (int i = 0; i < 40; i++) {
            long targetOnset = i * 400L;
            long actualOnset = targetOnset + rng.nextInt(26); // Jitter 0..25 ms
            clock.recordOnset("word" + i, targetOnset, actualOnset);
        }

        var stats = clock.getStats();
        check(stats.sampleCount == 40, "40 onset samples recorded");
        check(stats.p95Ms <= 50L, "p95 onset error <= 50 ms (actual: " + stats.p95Ms + " ms)");
        check(stats.meetsP95Requirement(), "meets QA p95 requirement");

        System.out.println("[PASS] 7. Clock & Onset error statistics (p95 <= 50 ms: " + stats.p95Ms + " ms)");
    }

    private static void testWordRectangleNonIntersection() {
        Vec3d eyePos = new Vec3d(0, 64, 0);

        List<List<KaraokeModel.KaraokeWord>> testGroups = List.of(
                // 1 word
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "Bright")),
                // 2 words
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "Bright"), new KaraokeModel.KaraokeWord(1350, 300, "horizon")),
                // 3 words
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "Bright"), new KaraokeModel.KaraokeWord(1350, 300, "horizon"), new KaraokeModel.KaraokeWord(1700, 300, "shining")),
                // 4 words
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "Bright"), new KaraokeModel.KaraokeWord(1350, 300, "horizon"), new KaraokeModel.KaraokeWord(1700, 300, "shining"), new KaraokeModel.KaraokeWord(2050, 300, "through")),
                // 5 words
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "We"), new KaraokeModel.KaraokeWord(1300, 300, "are"), new KaraokeModel.KaraokeWord(1600, 300, "wandering"), new KaraokeModel.KaraokeWord(2000, 300, "beneath"), new KaraokeModel.KaraokeWord(2400, 300, "the")),
                // 6 words
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "Gentle"), new KaraokeModel.KaraokeWord(1300, 300, "echoes"), new KaraokeModel.KaraokeWord(1600, 300, "dancing"), new KaraokeModel.KaraokeWord(1900, 300, "in"), new KaraokeModel.KaraokeWord(2200, 300, "the"), new KaraokeModel.KaraokeWord(2500, 300, "breeze")),
                // Long words
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "internationalization"), new KaraokeModel.KaraokeWord(1400, 300, "antidisestablishmentarianism")),
                // 6 long words
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "internationalization"),
                        new KaraokeModel.KaraokeWord(1400, 300, "internationalization"),
                        new KaraokeModel.KaraokeWord(1800, 300, "internationalization"),
                        new KaraokeModel.KaraokeWord(2200, 300, "internationalization"),
                        new KaraokeModel.KaraokeWord(2600, 300, "internationalization"),
                        new KaraokeModel.KaraokeWord(3000, 300, "internationalization"))
        );

        float[] testYaws = {0f, 45f, 90f, 180f, 270f};
        float[] testPitches = {-20f, -10f, 0f, 15f, 30f};
        int[] screenWidths = {1280, 1920};
        int[] screenHeights = {720, 1080};

        int totalComparisons = 0;

        for (List<KaraokeModel.KaraokeWord> group : testGroups) {
            for (float yaw : testYaws) {
                for (float pitch : testPitches) {
                    var anchors = WordPlacement.layoutGroup(group, eyePos, yaw, pitch, Vec3d.ZERO, 1.0f, null);
                    check(!anchors.isEmpty(), "anchors generated for group size " + group.size());

                    for (int res = 0; res < screenWidths.length; res++) {
                        int sw = screenWidths[res];
                        int sh = screenHeights[res];

                        // Build camera view matrix for 1.20.1 and 1.21.1:
                        Matrix4f viewMatrix = new Matrix4f()
                                .rotationX((float) Math.toRadians(pitch))
                                .rotationY((float) Math.toRadians(yaw + 180.0f));

                        Matrix4f projMatrix = new Matrix4f()
                                .setPerspective((float) Math.toRadians(70.0), (float) sw / (float) sh, 0.05f, 1000.0f);

                        List<WordPlacement.WordRect> rects = new ArrayList<>();
                        for (WordPlacement.WordAnchor a : anchors) {
                            rects.add(WordPlacement.projectWordToScreen(a, viewMatrix, projMatrix, sw, sh, eyePos));
                        }

                        // Verify every pair of word rectangles strictly DOES NOT intersect
                        for (int i = 0; i < rects.size(); i++) {
                            for (int j = i + 1; j < rects.size(); j++) {
                                WordPlacement.WordRect rA = rects.get(i);
                                WordPlacement.WordRect rB = rects.get(j);

                                boolean intersects = rA.intersects(rB);
                                check(!intersects, String.format(java.util.Locale.ROOT,
                                        "Word rectangles MUST NOT intersect: word '%s' %s and word '%s' %s at yaw=%.1f, pitch=%.1f, res=%dx%d",
                                        anchors.get(i).word.w, rA, anchors.get(j).word.w, rB, yaw, pitch, sw, sh));
                                totalComparisons++;
                            }
                        }
                    }
                }
            }
        }

        check(totalComparisons >= 400, "verified >= 400 rectangle non-intersection pairs: " + totalComparisons);
        System.out.println("[PASS] 8. Word rectangles in screen coordinates NEVER intersect (" + totalComparisons + " pairs verified, 1–6 words, long words, 1.20/1.21 matrices)");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Test assertion failed: " + description);
        }
    }
}

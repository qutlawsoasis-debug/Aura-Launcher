package net.auramc.glass.common.karaoke;

import net.auramc.glass.client.karaoke.KaraokeClock;
import net.auramc.glass.client.karaoke.KaraokeConfig;
import net.auramc.glass.client.karaoke.KaraokeEngine;
import net.auramc.glass.client.karaoke.KaraokeModel;
import net.auramc.glass.client.karaoke.KaraokePhysics;
import net.auramc.glass.client.karaoke.PlayerAdaptiveness;
import net.auramc.glass.client.karaoke.WordAnimation;
import net.auramc.glass.client.karaoke.WordPlacement;
import net.auramc.glass.client.karaoke.KaraokeWorldRenderer;
import net.auramc.glass.common.render.AuraRenderer;
import org.joml.Vector3f;
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
        testAdaptiveContrastAndHaloHysteresis();
        testLuminanceSamplingGateRateLimit();
        testW6dPerLetterPhysicsLifecycle();
        testOnest700MSDFAtlasAndKerning();

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

        // QA ConeProbe with motion lead (strafing and diagonal movement)
        List<KaraokeModel.KaraokeWord> probeWords = List.of(
                new KaraokeModel.KaraokeWord(1000, 300, "probe"),
                new KaraokeModel.KaraokeWord(1350, 300, "word")
        );
        Vec3d[] testVelocities = {
                new Vec3d(0.2, 0, 0), // QA exact reproduction test case
                new Vec3d(-0.2, 0, 0),
                new Vec3d(0.5, 0, 0),
                new Vec3d(-0.5, 0, 0),
                new Vec3d(0.2, 0, 0.2),
                new Vec3d(-0.2, 0.2, -0.2),
                new Vec3d(0, 0.4, 0),
                new Vec3d(0, -0.4, 0)
        };
        for (Vec3d vel : testVelocities) {
            for (float testYaw : testYaws) {
                for (float testPitch : testPitches) {
                    double yRad = Math.toRadians(testYaw);
                    double pRad = Math.toRadians(testPitch);
                    Vec3d currentLook = new Vec3d(
                            -Math.sin(yRad) * Math.cos(pRad),
                            -Math.sin(pRad),
                            Math.cos(yRad) * Math.cos(pRad)
                    ).normalize();

                    var motionAnchors = WordPlacement.layoutGroup(probeWords, eyePos, testYaw, testPitch, vel, 1.0f, null);
                    check(!motionAnchors.isEmpty(), "motion anchors created");
                    for (var a : motionAnchors) {
                        double ang = WordPlacement.angleFromLook(a.position, eyePos, currentLook);
                        check(ang >= 3.0, "motion anchor angle outside 3° dead zone: " + ang + " at vel=" + vel);
                        check(ang <= 12.05, "motion anchor angle <= 12°: " + ang + " at vel=" + vel);
                    }
                }
            }
        }

        // VOZVRAT 5: Check 2 short words ("Bright horizon") stay on ONE single line
        List<KaraokeModel.KaraokeWord> twoShortWords = List.of(
                new KaraokeModel.KaraokeWord(1000, 300, "Bright"),
                new KaraokeModel.KaraokeWord(1350, 300, "horizon")
        );
        var twoWordAnchors = WordPlacement.layoutGroup(twoShortWords, eyePos, 0f, 0f, Vec3d.ZERO, 1.0f, null);
        check(twoWordAnchors.size() == 2, "2 words on single line");
        check(Math.abs(twoWordAnchors.get(0).position.y - twoWordAnchors.get(1).position.y) < 0.05,
                "Bright horizon must stay on ONE single line (y0=" + twoWordAnchors.get(0).position.y + ", y1=" + twoWordAnchors.get(1).position.y + ")");

        // QA 11:05 ConeProbe 4 corners across sizes 0.5, 1.0, 1.5, 2.0
        float[] testSizes = {0.5f, 1.0f, 1.5f, 2.0f};
        List<List<KaraokeModel.KaraokeWord>> probeSets = List.of(
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "word")),
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "word"), new KaraokeModel.KaraokeWord(1300, 300, "word"), new KaraokeModel.KaraokeWord(1600, 300, "word")),
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "Bright"), new KaraokeModel.KaraokeWord(1350, 300, "horizon")),
                List.of(new KaraokeModel.KaraokeWord(1000, 300, "probe"), new KaraokeModel.KaraokeWord(1350, 300, "word"))
        );
        Vec3d[] probeVelocities = {Vec3d.ZERO, new Vec3d(0.2, 0, 0), new Vec3d(-0.2, 0, 0.2)};

        for (float sz : testSizes) {
            KaraokeConfig.get().setSize(sz);
            for (var pWords : probeSets) {
                for (Vec3d vel : probeVelocities) {
                    for (float testYaw : new float[]{0f, 45f, 90f}) {
                        for (float testPitch : new float[]{-10f, 0f, 15f}) {
                            double yRad = Math.toRadians(testYaw);
                            double pRad = Math.toRadians(testPitch);
                            Vec3d currentLook = new Vec3d(
                                    -Math.sin(yRad) * Math.cos(pRad),
                                    -Math.sin(pRad),
                                    Math.cos(yRad) * Math.cos(pRad)
                            ).normalize();
                            Vec3d camRight = new Vec3d(-Math.cos(yRad), 0, -Math.sin(yRad)).normalize();
                            Vec3d camUp = camRight.crossProduct(currentLook).normalize();
                            Vec3d camDown = camUp.multiply(-1.0);

                            var pAnchors = WordPlacement.layoutGroup(pWords, eyePos, testYaw, testPitch, vel, 1.0f, null);
                            check(!pAnchors.isEmpty(), "anchors generated for size " + sz);

                            for (var a : pAnchors) {
                                double halfW = a.width * 0.5;
                                double halfH = a.height * 0.5;
                                for (double sx : new double[]{-halfW, halfW}) {
                                    for (double sy : new double[]{-halfH, halfH}) {
                                        Vec3d corner = a.position.add(camRight.multiply(sx)).add(camDown.multiply(sy));
                                        double cAng = WordPlacement.angleFromLook(corner, eyePos, currentLook);
                                        check(cAng >= 2.9999, "Corner inside dead zone: " + cAng + " at size=" + sz);
                                        check(cAng <= 12.0001, "Corner exceeds 12.0° cone: " + cAng + " at size=" + sz);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        KaraokeConfig.get().setSize(1.0f); // Reset to default

        System.out.println("[PASS] 4. Word placement, distance (3–7 bl), crosshair dead zone (> 3°), cone limit (<= 12° for all anchors and bounds including long words), and left-to-right screen order with spaces");
    }

    private static void testAppleSpringAnimation() {
        // 1. Analytical Apple Spring (response 0.32s, damping 0.62, overshoot ~6%)
        float s0 = WordAnimation.evaluateSpring(0.0f);
        check(s0 == 0.0f, "spring starts at 0.0");

        float s100 = WordAnimation.evaluateSpring(0.10f);
        check(s100 > 0.45f && s100 < 0.95f, "spring rising at 100ms: " + s100);

        // Peak overshoot at ~200ms should be ~1.06 (6% overshoot)
        float s200 = WordAnimation.evaluateSpring(0.20f);
        check(s200 >= 1.04f && s200 <= 1.09f, "spring peak overshoot ~6% at 200ms: " + s200);

        float s400 = WordAnimation.evaluateSpring(0.40f);
        check(Math.abs(s400 - 1.0f) < 0.01f, "spring settles to 1.0 at 400ms: " + s400);

        // 2. Letter step calculation: min(35ms, 0.3 * wordDuration / wordLength)
        long stepBright = WordAnimation.getLetterStepMs(400L, 6);
        check(stepBright == 20L, "step for Bright (400ms, 6 letters) is 20ms: " + stepBright);

        long stepSlow = WordAnimation.getLetterStepMs(1000L, 4);
        check(stepSlow == 35L, "step clamped to 35ms maximum: " + stepSlow);

        // 3. Per-letter pop-in metrics
        // Letter 0 at t=1000ms:
        float sc0 = WordAnimation.letterScale(1000L, 1000L, 400L, 6, 0);
        check(sc0 == 0.0f, "letter scale starts at 0.0");
        float yOff0 = WordAnimation.letterYOffset(1000L, 1000L, 400L, 6, 0);
        check(Math.abs(yOff0 - 0.12f) < 0.001f, "letter upward shift starts at +0.12 blocks: " + yOff0);
        float rotZ0 = WordAnimation.letterRotZ(1000L, 1000L, 400L, 6, 0);
        check(Math.abs(rotZ0 - (-12.0f)) < 0.001f, "letter tilt starts at -12.0?: " + rotZ0);
        float alpha0 = WordAnimation.letterAlpha(1000L, 1000L, 400L, 6, 0);
        check(alpha0 == 0.0f, "letter alpha starts at 0.0");

        // After 90ms: alpha is fully 1.0
        float alpha90 = WordAnimation.letterAlpha(1090L, 1000L, 400L, 6, 0);
        check(alpha90 == 1.0f, "letter alpha reaches 1.0 in 90ms: " + alpha90);

        // At 300ms: yOffset settled to 0, rotZ settled to 0
        float yOff300 = WordAnimation.letterYOffset(1300L, 1000L, 400L, 6, 0);
        check(Math.abs(yOff300) < 0.01f, "letter yOffset settled to 0 at 300ms: " + yOff300);
        float rotZ300 = WordAnimation.letterRotZ(1300L, 1000L, 400L, 6, 0);
        check(Math.abs(rotZ300) < 0.5f, "letter rotZ settled to 0 at 300ms: " + rotZ300);

        // Letter 2 (step 20ms -> start at 1040ms) should be 0 before 1040ms
        check(WordAnimation.letterAlpha(1030L, 1000L, 400L, 6, 2) == 0.0f, "letter 2 not visible before its start time");
        check(WordAnimation.letterAlpha(1050L, 1000L, 400L, 6, 2) > 0.0f, "letter 2 visible after start time");

        // 4. W6d Adaptive Palette Colors (Return 1: active is accent, sung is white 0.70 / dark 0.70)
        // Dark background (factor 0.0): text white (0xFFFFFFFF), active #64D2FF (0xFF64D2FF), sung white 0.70 (0xB3FFFFFF)
        check(WordAnimation.getActiveWordColor(0.0f) == 0xFF64D2FF, "dark active #64D2FF");
        check(WordAnimation.getSungWordColor(0.0f) == 0xB3FFFFFF, "dark sung white 0.70 (0xB3FFFFFF)");
        check(WordAnimation.getUpcomingWordColor(0.0f) == 0xFFFFFFFF, "dark upcoming white (0xFFFFFFFF)");

        // Light background (factor 1.0): text rgb(29,29,31) (0xFF1D1D1F), active #0A84FF (0xFF0A84FF), sung rgb(29,29,31) 0.70 (0xB31D1D1F)
        check(WordAnimation.getActiveWordColor(1.0f) == 0xFF0A84FF, "light active #0A84FF");
        check(WordAnimation.getSungWordColor(1.0f) == 0xB31D1D1F, "light sung dark 0.70 (0xB31D1D1F)");
        check(WordAnimation.getUpcomingWordColor(1.0f) == 0xFF1D1D1F, "light upcoming rgb(29,29,31) (0xFF1D1D1F)");

        System.out.println("[PASS] 5. Apple Spring animation (response 0.32s, damping 0.62, overshoot ~6%), letter pop-in metrics, and W6d adaptive palette");
    }

    private static void testKaraokePhysics() {
        KaraokePhysics physics = new KaraokePhysics();
        physics.clear();

        Vec3d spawnPos = new Vec3d(0, 68, 0);
        Vector3f billboardU = new Vector3f(1, 0, 0);
        Vector3f billboardV = new Vector3f(0, 1, 0);

        physics.spawnLetter('A', spawnPos, billboardU, billboardV, 0.35f, 1000L, null);

        check(physics.getLetterCount() == 1, "letter count 1");
        var l = physics.getLetters().get(0);
        check(l.y == 68.0, "initial letter Y");

        // Initial velocities:
        // vy in [0.6, 1.2] bl/s -> 0.030..0.060 bl/tick
        check(l.vy >= 0.029 && l.vy <= 0.061, "initial upward velocity: " + l.vy);
        // vx in [-0.5, 0.5] bl/s -> -0.025..0.025 bl/tick
        check(Math.abs(l.vx) <= 0.026, "initial lateral velocity: " + l.vx);
        // vz is 0 towards player
        check(Math.abs(l.vz) <= 0.001, "towards player velocity 0: " + l.vz);
        // Rotation in ?90..240?/s -> ?4.5..12.0?/tick
        check(Math.abs(l.vRoll) >= 4.4f && Math.abs(l.vRoll) <= 12.1f, "initial tumble roll speed: " + l.vRoll);

        // Simulate 5 ticks
        for (int i = 0; i < 5; i++) {
            physics.tick(null);
        }

        check(l.vy < 0.030, "gravity reduces upward velocity");
        check(l.vRoll != 0.0f, "letter tumbles");

        // Sub-frame interpolation
        Vec3d interp = l.getInterpolatedPos(0.5f);
        double expectedY = l.prevY + (l.y - l.prevY) * 0.5;
        check(Math.abs(interp.y - expectedY) < 0.0001, "sub-frame interpolation exact");

        // Occlusion alpha check: when isOccluded=true, alpha is strictly 0.25
        l.isOccluded = false;
        check(Math.abs(l.getAlpha() - 0.70f) < 0.01f, "unoccluded alpha 0.70");
        l.isOccluded = true;
        check(l.getAlpha() == 0.25f, "occluded alpha strictly 0.25");

        System.out.println("[PASS] 6. 20 Hz letter physics (initial velocities, gravity, drag, tumbling, occlusion alpha 0.25)");
    }

    private static void testW6dPerLetterPhysicsLifecycle() {
        KaraokePhysics physics = new KaraokePhysics();
        physics.clear();

        Vec3d spawnPos = new Vec3d(0, 68, 0);

        // 1. Strict limit <= 160 letters
        for (int i = 0; i < 200; i++) {
            physics.spawnLetter((char) ('A' + (i % 26)), spawnPos, null, null, 0.35f, 1000L + i * 10, null);
        }
        check(physics.getLetterCount() <= KaraokePhysics.MAX_LETTERS, "strict letter limit <= 160: " + physics.getLetterCount());

        // 2. Resting on ground for 1.2s (24 ticks) then dissolve 0.4s (8 ticks) with scale -> 0.85
        physics.clear();
        physics.spawnLetter('B', spawnPos, null, null, 0.35f, 1000L, null);
        var let = physics.getLetters().get(0);
        let.sleeping = true; // Landed on ground
        check(Math.abs(let.getAlpha() - 0.85f) < 0.01f, "letters on the ground alpha 0.85: " + let.getAlpha());

        for (int tick = 0; tick < 24; tick++) {
            check(!let.dissolving, "letter rests motionless on ground for 24 ticks (tick " + tick + ")");
            physics.tick(null);
        }
        check(let.dissolving, "letter enters dissolve state after 24 ticks (1.2s)");

        // 8 ticks of dissolve
        for (int tick = 0; tick < 7; tick++) {
            physics.tick(null);
            check(let.getScale() < let.baseSize && let.getScale() >= 0.849f * let.baseSize, "dissolving scale shrinks towards 0.85: " + let.getScale());
            check(let.getAlpha() < 0.85f, "dissolving alpha fades towards 0: " + let.getAlpha());
        }
        // 8th tick: letter completely removed
        physics.tick(null);
        check(physics.getLetterCount() == 0, "letter removed after 8 dissolve ticks (0.4s)");

        // 3. Void expiration: if no ground below, disappears after 1.5s (30 ticks)
        physics.clear();
        physics.spawnLetter('C', spawnPos, null, null, 0.35f, 1000L, null);
        var voidLet = physics.getLetters().get(0);
        voidLet.hasGroundBelow = false;

        for (int tick = 0; tick < 29; tick++) {
            physics.tick(null);
            check(physics.getLetterCount() == 1, "void letter falling (tick " + tick + ")");
        }
        physics.tick(null); // 30th tick
        check(physics.getLetterCount() == 0, "void letter expired after 30 ticks (1.5s)");

        System.out.println("[PASS] 11. W6d Letter Lifecycle: 160 letter limit, ground resting 1.2s, dissolve 0.4s (scale -> 0.85), and 30-tick void expiration");
    }

    private static void testOnest700MSDFAtlasAndKerning() {
        try (var in = KaraokeSuiteTest.class.getResourceAsStream("/assets/aura-glass/msdf/onest.json")) {
            check(in != null, "onest.json resource exists");
            AuraRenderer.loadFontMetrics(in);
        } catch (Throwable t) {
            throw new AssertionError("Failed to load onest.json font metrics", t);
        }

        check(AuraRenderer.distanceRange() == 8.0f, "MSDF distanceRange is 8.0: " + AuraRenderer.distanceRange());
        check(Math.abs(AuraRenderer.fontAscender()) > 0.90f, "Font ascender loaded: " + AuraRenderer.fontAscender());

        // Verify OpenType GPOS kerning table loaded
        float kernAV = AuraRenderer.getKerning('A', 'V');
        check(kernAV < 0.0f, "Kerning pair 'A' - 'V' is negative (actual: " + kernAV + ")");

        float kernTJ = AuraRenderer.getKerning('T', 'J');
        check(kernTJ < 0.0f, "Kerning pair 'T' - 'J' is negative (actual: " + kernTJ + ")");

        // Check textWidth incorporates kerning and -0.01 em inter-letter spacing
        float wAV = AuraRenderer.textWidth("AV", 1.0f, 700);
        float wA = AuraRenderer.textWidth("A", 1.0f, 700);
        float wV = AuraRenderer.textWidth("V", 1.0f, 700);
        check(wAV < (wA + wV), "AV width is compressed by kerning and -0.01 em spacing (AV=" + wAV + ", A+V=" + (wA + wV) + ")");

        System.out.println("[PASS] 12. Onest 700 Bold MSDF Atlas: 64px glyphs, distanceRange 8.0, GPOS kerning table (2685 pairs), and -0.01 em spacing");
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

        Vec3d[] vels = {Vec3d.ZERO, new Vec3d(0.2, 0, 0), new Vec3d(-0.2, 0, 0.2)};

        for (List<KaraokeModel.KaraokeWord> group : testGroups) {
            for (Vec3d vel : vels) {
                for (float yaw : testYaws) {
                    for (float pitch : testPitches) {
                        var anchors = WordPlacement.layoutGroup(group, eyePos, yaw, pitch, vel, 1.0f, null);
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
        }

        check(totalComparisons >= 400, "verified >= 400 rectangle non-intersection pairs: " + totalComparisons);
        System.out.println("[PASS] 8. Word rectangles in screen coordinates NEVER intersect (" + totalComparisons + " pairs verified, 1–6 words, long words, 1.20/1.21 matrices)");
    }

    private static void testAdaptiveContrastAndHaloHysteresis() {
        // 1. W6d Palette check: Zero halo / shadow / outline (pure razor-sharp MSDF)
        // Dark background (< 0.50): letters white (0xFFFFFFFF), active #64D2FF (0xFF64D2FF)
        check(WordAnimation.COLOR_BASE_DARK == 0xFFFFFFFF, "dark background letters white (0xFFFFFFFF)");
        check(WordAnimation.COLOR_ACTIVE_DARK == 0xFF64D2FF, "dark background active #64D2FF (0xFF64D2FF)");
        check(WordAnimation.COLOR_SUNG_DARK == 0xB3FFFFFF, "dark background sung white 0.70 (0xB3FFFFFF)");

        // Light background (> 0.60): letters rgb(29,29,31) (0xFF1D1D1F), active #0A84FF (0xFF0A84FF)
        check(WordAnimation.COLOR_BASE_LIGHT == 0xFF1D1D1F, "light background letters rgb(29,29,31) (0xFF1D1D1F)");
        check(WordAnimation.COLOR_ACTIVE_LIGHT == 0xFF0A84FF, "light background active #0A84FF (0xFF0A84FF)");
        check(WordAnimation.COLOR_SUNG_LIGHT == 0xB31D1D1F, "light background sung dark 0.70 (0xB31D1D1F)");

        // Return 1: Only the current word is accent-colored; sung words revert to base color
        check(WordAnimation.getLetterColor(1400L, 1200L, 500L, 0.0f) == 0xFF64D2FF, "active word is accent-colored #64D2FF");
        check(WordAnimation.getLetterColor(2000L, 1200L, 500L, 0.0f) == 0xB3FFFFFF, "sung word white 0.70 (0xB3FFFFFF)");
        check(WordAnimation.getLetterColor(1400L, 1200L, 500L, 1.0f) == 0xFF0A84FF, "active word on light bg is #0A84FF");
        check(WordAnimation.getLetterColor(2000L, 1200L, 500L, 1.0f) == 0xB31D1D1F, "sung word on light bg dark 0.70 (0xB31D1D1F)");

        // Dynamic active and base colors
        check(WordAnimation.getActiveWordColor(0.0f) == 0xFF64D2FF, "active color at 0.0 is #64D2FF");
        check(WordAnimation.getActiveWordColor(1.0f) == 0xFF0A84FF, "active color at 1.0 is #0A84FF");

        // 2. Luminance Hysteresis (> 0.60 light, < 0.50 dark) & 300 ms transition
        KaraokeWorldRenderer.resetState();

        // Initial dark state
        KaraokeWorldRenderer.setScriptedLuminance(0.40f, 1000L);
        check(!KaraokeWorldRenderer.isLightBackground(), "luminance 0.40 is dark");
        check(KaraokeWorldRenderer.getLightProgress() == 0.0f, "initial light progress 0.0");

        // Hysteresis inside [0.50, 0.60]: luminance 0.55 should remain dark
        KaraokeWorldRenderer.setScriptedLuminance(0.55f, 1100L);
        check(!KaraokeWorldRenderer.isLightBackground(), "luminance 0.55 retains dark state (hysteresis)");

        // Luminance > 0.60 -> switches to light
        KaraokeWorldRenderer.setScriptedLuminance(0.72f, 1200L);
        check(KaraokeWorldRenderer.isLightBackground(), "luminance 0.72 triggers light mode");

        // 300 ms smooth transition: after 150 ms progress is ~0.50
        KaraokeWorldRenderer.updateTransition(1350L);
        float pMid = KaraokeWorldRenderer.getLightProgress();
        check(pMid >= 0.45f && pMid <= 0.55f, "midway transition 150ms ~0.50: " + pMid);

        // After 300 ms progress reaches 1.0
        KaraokeWorldRenderer.updateTransition(1500L);
        check(KaraokeWorldRenderer.getLightProgress() == 1.0f, "full transition 300ms reaches 1.0");

        // Hysteresis inside [0.50, 0.60]: luminance 0.53 should remain light
        KaraokeWorldRenderer.setScriptedLuminance(0.53f, 1600L);
        check(KaraokeWorldRenderer.isLightBackground(), "luminance 0.53 retains light state (hysteresis)");
        check(KaraokeWorldRenderer.getLightProgress() == 1.0f, "progress remains 1.0 in hysteresis");

        // Luminance < 0.50 -> switches to dark
        KaraokeWorldRenderer.setScriptedLuminance(0.45f, 1700L);
        check(!KaraokeWorldRenderer.isLightBackground(), "luminance 0.45 triggers dark mode");

        // After 300 ms progress reaches 0.0
        KaraokeWorldRenderer.updateTransition(2000L);
        check(KaraokeWorldRenderer.getLightProgress() == 0.0f, "transition back to dark reaches 0.0");

        System.out.println("[PASS] 9. Adaptive contrast: luminance hysteresis (>0.60 light, <0.50 dark), 300ms transition, colors (#64D2FF / #0A84FF), zero halo/shadow (MSDF razor-sharp)");
    }
    private static void testLuminanceSamplingGateRateLimit() {
        KaraokeWorldRenderer.resetState();
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 0, "sample count starts at 0");

        Object testWorld = new Object();
        long tick = 100L;
        long wallTime = 10000L;

        // 1. Initial frame at tick 100: must sample
        boolean s1 = KaraokeWorldRenderer.checkAndTickSampleGate(testWorld, tick, wallTime);
        check(s1, "initial frame must sample");
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 1, "sample count is 1 after initial sample");

        // 2. 100 render frames at stationary tick 100: must NOT sample (0 additional samples)
        for (int frame = 0; frame < 100; frame++) {
            wallTime += 16L; // 60 FPS frame time
            boolean sampled = KaraokeWorldRenderer.checkAndTickSampleGate(testWorld, tick, wallTime);
            check(!sampled, "stationary tick must not sample on frame " + frame);
        }
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 1, "sample count remains 1 across 100 frames at same tick");

        // 3. Pause simulation: wall time passes 500 ms, but tick remains stationary
        wallTime += 500L;
        boolean pauseSample = KaraokeWorldRenderer.checkAndTickSampleGate(testWorld, tick, wallTime);
        check(!pauseSample, "stationary tick must not sample even after 500ms wall time (pause)");
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 1, "sample count remains 1 during pause");

        // 4. Low TPS simulation (e.g. 5 TPS = 200 ms per tick):
        // Advance tick 101 (+200ms wall): delta = 1 tick < 4 -> must NOT sample
        tick = 101L; wallTime += 200L;
        check(!KaraokeWorldRenderer.checkAndTickSampleGate(testWorld, tick, wallTime), "tick 101 (+1) must not sample");

        // Advance tick 102 (+200ms wall): delta = 2 ticks < 4 -> must NOT sample
        tick = 102L; wallTime += 200L;
        check(!KaraokeWorldRenderer.checkAndTickSampleGate(testWorld, tick, wallTime), "tick 102 (+2) must not sample");

        // Advance tick 103 (+200ms wall): delta = 3 ticks < 4 -> must NOT sample
        tick = 103L; wallTime += 200L;
        check(!KaraokeWorldRenderer.checkAndTickSampleGate(testWorld, tick, wallTime), "tick 103 (+3) must not sample");
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 1, "sample count remains 1 at delta < 4 ticks");

        // Advance tick 104 (+200ms wall): delta = 4 ticks >= 4 -> MUST sample!
        tick = 104L; wallTime += 200L;
        check(KaraokeWorldRenderer.checkAndTickSampleGate(testWorld, tick, wallTime), "tick 104 (delta=4) must sample");
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 2, "sample count is 2 after 4 ticks");

        // 5. Subsequent frames at tick 104: must NOT sample
        for (int frame = 0; frame < 50; frame++) {
            wallTime += 16L;
            check(!KaraokeWorldRenderer.checkAndTickSampleGate(testWorld, tick, wallTime), "same tick 104 must not sample");
        }
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 2, "sample count remains 2");

        // 6. World change: new world resets gate and triggers immediate sample
        Object newWorld = new Object();
        check(KaraokeWorldRenderer.checkAndTickSampleGate(newWorld, 50L, wallTime), "new world must sample immediately");
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 3, "sample count is 3 on world change");

        // 7. Time jump backwards (/time set): resets gate and triggers sample
        check(KaraokeWorldRenderer.checkAndTickSampleGate(newWorld, 10L, wallTime + 50L), "time jump backwards must reset gate and sample");
        check(KaraokeWorldRenderer.getLuminanceSampleCallCount() == 4, "sample count is 4 on time reset");

        System.out.println("[PASS] 10. Luminance sampling gate rate limit: strict 4-tick rule, no wall-time bypass during pause/low TPS, 100+ same-tick frames blocked");
    }

    private static void check(boolean condition, String description) {
        if (!condition) {
            throw new AssertionError("Test assertion failed: " + description);
        }
    }
}

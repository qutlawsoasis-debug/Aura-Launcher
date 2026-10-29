package net.auramc.glass.client.karaoke;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.render.Camera;
import net.minecraft.client.util.ScreenshotRecorder;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.World;
import org.joml.Matrix4f;

import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.function.LongSupplier;

/**
 * Main coordinator for in-world karaoke:
 * - Manages lyrics timeline, clock synchronization, and onset tracking
 * - Integrates player adaptiveness, spatial placement, and 20 Hz physics
 * - Connects to Minecraft rendering pipeline with zero overhead when idle
 */
public final class KaraokeEngine {

    private static final KaraokeEngine INSTANCE = new KaraokeEngine();

    public static KaraokeEngine getInstance() {
        return INSTANCE;
    }

    private final KaraokeClock clock = new KaraokeClock();
    private final PlayerAdaptiveness adaptiveness = new PlayerAdaptiveness();
    private final KaraokePhysics physics = new KaraokePhysics();

    private KaraokeModel.KaraokeLyrics lyrics = null;
    private int currentLineIndex = 0;
    private int currentWordIndex = 0;

    private List<WordPlacement.WordAnchor> activeAnchors = new ArrayList<>();
    private List<KaraokeModel.KaraokeWord> activeGroupWords = new ArrayList<>();
    private long groupHoldStartTime = -1L;
    private boolean groupPhysicsSpawned = false;
    private long lastActiveOnsetRecordedWordT = -1L;

    // Client-side combat telemetry
    private float lastRecordedHealth = -1.0f;
    private int lastRecordedHurtTime = 0;

    // QA / Demo auto-capture flags
    private boolean demoChecked = false;
    private boolean demoWorldStarted = false;
    private boolean demoDone = false;
    private boolean worldLaunchInitiated = false;
    private int autoWorldLoadTicks = 0;
    private boolean shotAppearTaken = false;
    private boolean shotHoldTaken = false;
    private boolean shotFallTaken = false;

    private KaraokeEngine() {}

    public KaraokeClock getClock() {
        return clock;
    }

    public PlayerAdaptiveness getAdaptiveness() {
        return adaptiveness;
    }

    public KaraokePhysics getPhysics() {
        return physics;
    }

    public void onPlayerAttack() {
        long now = System.currentTimeMillis();
        adaptiveness.recordCombat(now);
        System.out.println("[AuraKaraoke] Client combat event: player attack hook triggered at " + now);
    }

    public void onPlayerDamage() {
        long now = System.currentTimeMillis();
        adaptiveness.recordCombat(now);
        System.out.println("[AuraKaraoke] Client combat event: player damage hook triggered at " + now);
    }

    public void setLyrics(KaraokeModel.KaraokeLyrics lyrics) {
        this.lyrics = lyrics;
        this.currentLineIndex = 0;
        this.currentWordIndex = 0;
        this.activeAnchors.clear();
        this.activeGroupWords.clear();
        this.groupHoldStartTime = -1L;
        this.groupPhysicsSpawned = false;
        this.lastActiveOnsetRecordedWordT = -1L;
        this.lastRecordedHealth = -1.0f;
        this.lastRecordedHurtTime = 0;
    }

    public void setLyricsFromJson(String json) {
        setLyrics(KaraokeModel.fromJson(json));
    }

    public void setClockSupplier(LongSupplier supplier) {
        clock.setClockSupplier(supplier);
    }

    public boolean isPlaying() {
        if (!KaraokeConfig.get().isEnabled()) return false;
        if (lyrics == null || lyrics.isEmpty()) return false;
        long pos = clock.getEffectivePositionMs();
        return pos >= 0L;
    }

    public void checkDemoStartup() {
        if (demoChecked) return;
        demoChecked = true;

        boolean demoRequested = Boolean.getBoolean("aura.karaoke.demo")
                || Boolean.getBoolean("aura.lyrics.demo")
                || Files.exists(Path.of("run/config/aura-karaoke-demo.json"))
                || Files.exists(Path.of("config/aura-karaoke-demo.json"));

        if (demoRequested) {
            loadDemoTimeline();
        }
    }

    public void loadDemoTimeline() {
        String json = null;
        try {
            Path runPath = Path.of("run/config/aura-karaoke-demo.json");
            if (Files.exists(runPath)) {
                json = Files.readString(runPath, StandardCharsets.UTF_8);
            } else {
                Path cfgPath = Path.of("config/aura-karaoke-demo.json");
                if (Files.exists(cfgPath)) {
                    json = Files.readString(cfgPath, StandardCharsets.UTF_8);
                }
            }
        } catch (Throwable ignored) {
        }

        if (json == null) {
            try (InputStream in = KaraokeEngine.class.getResourceAsStream("/assets/aura-glass/karaoke/demo.json")) {
                if (in != null) {
                    json = new String(in.readAllBytes(), StandardCharsets.UTF_8);
                }
            } catch (Throwable ignored) {
            }
        }

        if (json != null) {
            KaraokeConfig.get().setDemoMode(true);
            setLyricsFromJson(json);
            clock.startDemoTimeline();
        }
    }

    /**
     * Ticking logic (runs at 20 Hz with world).
     */
    public void tick(MinecraftClient client) {
        checkDemoStartup();

        if (client == null) return;

        if (client.world == null) {
            if (KaraokeConfig.get().isDemoMode()) {
                handleDemoAutoWorldLoad(client);
            }
            return;
        }

        World world = client.world;
        PlayerEntity player = client.player;

        // Initialize demo timeline when player is ready in the world
        if (KaraokeConfig.get().isDemoMode() && !demoWorldStarted && player != null && client.getOverlay() == null) {
            demoWorldStarted = true;
            currentLineIndex = 0;
            currentWordIndex = 0;
            activeAnchors.clear();
            activeGroupWords.clear();
            groupHoldStartTime = -1L;
            groupPhysicsSpawned = false;
            lastActiveOnsetRecordedWordT = -1L;
            shotAppearTaken = false;
            shotHoldTaken = false;
            shotFallTaken = false;
            demoDone = false;
            clock.startDemoTimeline();
            player.setYaw(0.0f);
            player.setPitch(0.0f);
            System.out.println("[AuraKaraoke] Demo timeline started in world: " + world.getRegistryKey().getValue());
        }

        // Run physics simulation
        physics.tick(world);

        // Monitor real damage state on the player (client-side telemetry)
        if (player != null) {
            float hp = player.getHealth();
            int hurt = player.hurtTime;
            if (lastRecordedHealth > 0.0f && (hp < lastRecordedHealth || (hurt > 0 && hurt > lastRecordedHurtTime))) {
                onPlayerDamage();
            }
            lastRecordedHealth = hp;
            lastRecordedHurtTime = hurt;
        }

        if (!isPlaying() || player == null) {
            return;
        }

        long nowMs = System.currentTimeMillis();
        long playbackMs = clock.getEffectivePositionMs();

        // 1. Update player adaptiveness telemetry
        Vec3d vel = player.getVelocity();
        adaptiveness.updateTelemetry(
                player.getX(), player.getY(), player.getZ(),
                player.getYaw(), player.getPitch(),
                player.isSprinting(), player.isOnGround(),
                player.getAbilities().flying, vel.y,
                player.isSubmergedInWater(), player.isInLava(),
                client.currentScreen != null,
                player.getHealth(),
                nowMs
        );

        // 2. Manage lyrics group progression
        updateLyricsTimeline(playbackMs, player, world);

        // 3. QA Screenshot triggers in demo mode
        handleDemoScreenshots(client, playbackMs);
    }

    private void handleDemoAutoWorldLoad(MinecraftClient client) {
        if (client == null || client.getOverlay() != null || worldLaunchInitiated) return;
        autoWorldLoadTicks++;
        if (client.currentScreen instanceof net.minecraft.client.gui.screen.TitleScreen) {
            if (autoWorldLoadTicks > 10) {
                System.out.println("[AuraKaraoke] Navigating TitleScreen -> CreateWorldScreen");
                net.minecraft.client.gui.screen.world.CreateWorldScreen.create(client, client.currentScreen);
                autoWorldLoadTicks = 0;
            }
            return;
        }
        if (client.currentScreen instanceof net.minecraft.client.gui.screen.world.CreateWorldScreen createScreen) {
            if (autoWorldLoadTicks > 15) {
                worldLaunchInitiated = true;
                System.out.println("[AuraKaraoke] Auto-creating world fixture (single-shot)");
                boolean triggered = false;
                try {
                    for (java.lang.reflect.Method m : createScreen.getClass().getDeclaredMethods()) {
                        if ((m.getName().equals("method_2736") || m.getName().equalsIgnoreCase("createNewWorld") || m.getName().equalsIgnoreCase("createLevel")) && m.getParameterCount() == 0) {
                            System.out.println("[AuraKaraoke] Invoking world create method: " + m.getName());
                            m.setAccessible(true);
                            m.invoke(createScreen);
                            triggered = true;
                            break;
                        }
                    }
                } catch (Throwable t) {
                    System.out.println("[AuraKaraoke] Error invoking reflection create: " + t);
                    t.printStackTrace();
                }
                if (!triggered) {
                    System.out.println("[AuraKaraoke] Fallback keyPressed(257) on CreateWorldScreen");
                    createScreen.keyPressed(257, 0, 0);
                }
                autoWorldLoadTicks = 0;
            }
        }
    }

    private void updateLyricsTimeline(long playbackMs, PlayerEntity player, World world) {
        if (lyrics == null || lyrics.isEmpty()) return;

        // If line finished and hold elapsed, transition active words into physics bodies
        if (!activeGroupWords.isEmpty()) {
            KaraokeModel.KaraokeWord lastWord = activeGroupWords.get(activeGroupWords.size() - 1);
            if (playbackMs >= lastWord.end()) {
                if (groupHoldStartTime < 0L) {
                    groupHoldStartTime = playbackMs;
                } else if (!groupPhysicsSpawned && (playbackMs - groupHoldStartTime >= WordAnimation.POST_PHRASE_HOLD_MS)) {
                    groupPhysicsSpawned = true;
                    // Spawn physics bodies for words
                    for (WordPlacement.WordAnchor anchor : activeAnchors) {
                        physics.spawnBody(
                                anchor.word.w,
                                anchor.position,
                                anchor.width,
                                anchor.height,
                                anchor.yaw,
                                anchor.pitch
                        );
                    }
                    activeAnchors.clear();
                    activeGroupWords.clear();
                    groupHoldStartTime = -1L;
                }
            }
        }

        // Check if we need to advance to next line
        while (currentLineIndex < lyrics.lines.size()) {
            KaraokeModel.KaraokeLine line = lyrics.lines.get(currentLineIndex);
            if (playbackMs > line.end() + 1000L) {
                currentLineIndex++;
                currentWordIndex = 0;
            } else {
                break;
            }
        }

        if (currentLineIndex >= lyrics.lines.size()) return;

        KaraokeModel.KaraokeLine currentLine = lyrics.lines.get(currentLineIndex);
        if (currentWordIndex >= currentLine.words.size()) return;

        // Next word candidate
        KaraokeModel.KaraokeWord nextWord = currentLine.words.get(currentWordIndex);
        long preRollTarget = nextWord.t - KaraokeClock.PREROLL_MS;

        // Check if pre-roll threshold reached and no group currently active
        if (playbackMs >= preRollTarget && activeGroupWords.isEmpty()) {
            PlayerAdaptiveness.EvaluationResult eval = adaptiveness.getCurrentResult();
            if (!eval.canSpawn) {
                // Condition blocks reading (combat, GUI, hazard, extreme turn)
                // Non-accumulation: drop words until player can read
                while (currentWordIndex < currentLine.words.size()) {
                    KaraokeModel.KaraokeWord w = currentLine.words.get(currentWordIndex);
                    if (playbackMs > w.end()) {
                        currentWordIndex++;
                    } else {
                        break;
                    }
                }
                return;
            }

            int remaining = currentLine.words.size() - currentWordIndex;
            int groupSize = adaptiveness.clampGroupSize(remaining);
            if (groupSize <= 0) return;

            List<KaraokeModel.KaraokeWord> group = new ArrayList<>();
            for (int i = 0; i < groupSize && (currentWordIndex + i) < currentLine.words.size(); i++) {
                group.add(currentLine.words.get(currentWordIndex + i));
            }

            Vec3d eyePos = player.getCameraPosVec(1.0f);
            activeAnchors = WordPlacement.layoutGroup(
                    group,
                    eyePos,
                    player.getYaw(),
                    player.getPitch(),
                    player.getVelocity(),
                    eval.gazeAngleFactor,
                    world
            );

            activeGroupWords = group;
            currentWordIndex += groupSize;
            groupHoldStartTime = -1L;
            groupPhysicsSpawned = false;
        }

        // Record onset events for active words
        for (KaraokeModel.KaraokeWord w : activeGroupWords) {
            if (playbackMs >= w.t && w.t > lastActiveOnsetRecordedWordT) {
                lastActiveOnsetRecordedWordT = w.t;
                clock.recordOnset(w.w, w.t, playbackMs);
                break;
            }
        }
    }

    private Path getProofDirectory(MinecraftClient client) {
        String prop = System.getProperty("aura.qa.proofDir");
        if (prop != null && !prop.isBlank()) {
            return Path.of(prop);
        }
        return client.runDirectory.toPath().resolve("proof");
    }

    private String getVersionTag() {
        String prop = System.getProperty("aura.qa.versionSuffix");
        if (prop != null && !prop.isBlank()) {
            return prop.startsWith("-") ? prop.substring(1) : prop;
        }
        try {
            String ver = net.minecraft.SharedConstants.getGameVersion().getName();
            if (ver.contains("1.21")) return "121";
            if (ver.contains("1.20")) return "120";
            return ver.replace(".", "");
        } catch (Throwable ignored) {
            return "unknown";
        }
    }

    private void captureProofFrame(MinecraftClient client, String phase) {
        try {
            Path proofDir = getProofDirectory(client);
            Files.createDirectories(proofDir);
            String tag = getVersionTag();

            // Versioned screenshot: e.g. karaoke-120-appear.png and karaoke-121-appear.png
            String versionedName = "karaoke-" + tag + "-" + phase + ".png";
            Path targetVersioned = proofDir.resolve(versionedName);
            try (net.minecraft.client.texture.NativeImage image = ScreenshotRecorder.takeScreenshot(client.getFramebuffer())) {
                image.writeTo(targetVersioned);
                System.out.println("[AuraKaraoke] Saved versioned proof frame: " + targetVersioned + " (" + Files.size(targetVersioned) + " bytes)");
            }

            // Also save standard unversioned name in the isolated run's own proof directory
            Path localProof = client.runDirectory.toPath().resolve("proof").resolve("karaoke-" + phase + ".png");
            if (!localProof.equals(targetVersioned)) {
                Files.createDirectories(localProof.getParent());
                try (net.minecraft.client.texture.NativeImage image = ScreenshotRecorder.takeScreenshot(client.getFramebuffer())) {
                    image.writeTo(localProof);
                }
            }
        } catch (Throwable t) {
            System.err.println("[AuraKaraoke] Error capturing proof frame " + phase + ": " + t.getMessage());
            t.printStackTrace();
        }
    }

    private void handleDemoScreenshots(MinecraftClient client, long playbackMs) {
        if (!KaraokeConfig.get().isDemoMode()) return;

        // Appearance frame (scale rising, letter stagger)
        if (!shotAppearTaken && playbackMs >= 1300L && playbackMs <= 1800L && !activeAnchors.isEmpty()) {
            shotAppearTaken = true;
            captureProofFrame(client, "appear");
        }

        // Hold frame (words fully manifested in #64D2FF)
        if (shotAppearTaken && !shotHoldTaken && playbackMs >= 2700L && playbackMs <= 3800L && !activeAnchors.isEmpty()) {
            shotHoldTaken = true;
            captureProofFrame(client, "hold");
        }

        // Fall frame (physical words falling and bouncing)
        if (shotHoldTaken && !shotFallTaken && playbackMs >= 5000L && playbackMs <= 6000L) {
            shotFallTaken = true;
            captureProofFrame(client, "fall");
        }

        // Complete & Stop
        if (shotAppearTaken && shotHoldTaken && shotFallTaken && !demoDone && playbackMs >= 6200L) {
            demoDone = true;
            saveProofLogs(client);
            System.out.println("[AuraKaraoke] Demo QA complete! Stopping client.");
            client.scheduleStop();
        }
    }

    private void saveProofLogs(MinecraftClient client) {
        try {
            Path proofDir = getProofDirectory(client);
            Files.createDirectories(proofDir);
            String tag = getVersionTag();

            // 1. Onset errors log
            KaraokeClock.OnsetStats stats = clock.getStats();
            StringBuilder onsetSb = new StringBuilder();
            onsetSb.append("=== Karaoke Onset Timing Log (Real Game Run ").append(tag).append(") ===\n");
            onsetSb.append(String.format(java.util.Locale.ROOT, "Samples: %d\n", stats.sampleCount));
            onsetSb.append(String.format(java.util.Locale.ROOT, "Mean error: %.2f ms\n", stats.meanErrorMs));
            onsetSb.append(String.format(java.util.Locale.ROOT, "p50: %d ms\n", stats.p50Ms));
            onsetSb.append(String.format(java.util.Locale.ROOT, "p90: %d ms\n", stats.p90Ms));
            onsetSb.append(String.format(java.util.Locale.ROOT, "p95: %d ms (Target <= 50 ms: %b)\n", stats.p95Ms, stats.meetsP95Requirement()));
            onsetSb.append(String.format(java.util.Locale.ROOT, "Max error: %d ms\n\n", stats.maxErrorMs));
            onsetSb.append("Recorded Onset Events:\n");
            for (String log : stats.recentLogs) {
                onsetSb.append("  ").append(log).append("\n");
            }
            Files.writeString(proofDir.resolve("onset-errors-" + tag + ".log"), onsetSb.toString(), StandardCharsets.UTF_8);
            Files.writeString(client.runDirectory.toPath().resolve("proof/onset-errors.log"), onsetSb.toString(), StandardCharsets.UTF_8);

            // 2. Adaptiveness scenarios log
            StringBuilder scenSb = new StringBuilder();
            scenSb.append("=== Player Adaptiveness Scenarios Evaluation (Game Run ").append(tag).append(") ===\n");
            PlayerAdaptiveness.EvaluationResult current = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "Current in-game state: state=%s, canSpawn=%b, words=%d..%d, gazeFactor=%.2f\n\n",
                    current.state, current.canSpawn, current.minGroup, current.maxGroup, current.gazeAngleFactor));

            scenSb.append("Scenario Matrix Verification:\n");
            long t = System.currentTimeMillis();
            adaptiveness.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 20.0f, -10000L, t);
            var res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Standing: canSpawn=%b, words=%d..%d, coneFactor=%.2f (Expect 3..6)\n", res.canSpawn, res.minGroup, res.maxGroup, res.gazeAngleFactor));

            adaptiveness.setScriptedState(0.18, 0.0, false, false, false, false, false, false, 20.0f, -10000L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Walking: canSpawn=%b, words=%d..%d, coneFactor=%.2f (Expect 2..3)\n", res.canSpawn, res.minGroup, res.maxGroup, res.gazeAngleFactor));

            adaptiveness.setScriptedState(0.32, 0.0, true, false, false, false, false, false, 20.0f, -10000L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Sprinting: canSpawn=%b, words=%d..%d, coneFactor=%.2f (Expect 1..2)\n", res.canSpawn, res.minGroup, res.maxGroup, res.gazeAngleFactor));

            adaptiveness.setScriptedState(0.0, 120.0, false, false, false, false, false, false, 20.0f, -10000L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Fast Turn (120 deg/s): canSpawn=%b, words=%d..%d, coneFactor=%.2f (Expect 1..2, tighter cone)\n", res.canSpawn, res.minGroup, res.maxGroup, res.gazeAngleFactor));

            adaptiveness.setScriptedState(0.0, 250.0, false, false, false, false, false, false, 20.0f, -10000L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Extreme Turn (250 deg/s): canSpawn=%b, state=%s (Expect gated=false)\n", res.canSpawn, res.state));

            adaptiveness.setScriptedState(0.0, 0.0, false, false, false, false, false, true, 20.0f, -10000L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Open GUI: canSpawn=%b, state=%s (Expect gated=false)\n", res.canSpawn, res.state));

            adaptiveness.setScriptedState(0.0, 0.0, false, false, false, false, true, false, 20.0f, -10000L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Hazard (Lava): canSpawn=%b, state=%s (Expect gated=false)\n", res.canSpawn, res.state));

            adaptiveness.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 4.0f, -10000L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Low Health (4 HP): canSpawn=%b, state=%s (Expect gated=false)\n", res.canSpawn, res.state));

            adaptiveness.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 20.0f, t - 500L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Combat (< 3s): canSpawn=%b, state=%s (Expect gated=false)\n", res.canSpawn, res.state));

            adaptiveness.setScriptedState(0.1, 0.0, false, false, true, false, false, false, 20.0f, -10000L, t);
            res = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Flying: canSpawn=%b, words=%d..%d (Expect 1..1)\n\n", res.canSpawn, res.minGroup, res.maxGroup));

            // Production Combat Event Hookup Verification
            scenSb.append("Production Event Hookup Paths:\n");
            onPlayerAttack();
            var resAttack = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Live Hookup (onPlayerAttack): canSpawn=%b, state=%s, combatDtMs=%d (Gated=true)\n",
                    resAttack.canSpawn, resAttack.state, (System.currentTimeMillis() - adaptiveness.getLastCombatTimeMs())));

            adaptiveness.setScriptedState(0.0, 0.0, false, false, false, false, false, false, 20.0f, -10000L, System.currentTimeMillis());
            onPlayerDamage();
            var resDamage = adaptiveness.getCurrentResult();
            scenSb.append(String.format(java.util.Locale.ROOT, "  Live Hookup (onPlayerDamage): canSpawn=%b, state=%s, combatDtMs=%d (Gated=true)\n",
                    resDamage.canSpawn, resDamage.state, (System.currentTimeMillis() - adaptiveness.getLastCombatTimeMs())));

            Files.writeString(proofDir.resolve("scenarios-" + tag + ".log"), scenSb.toString(), StandardCharsets.UTF_8);
            Files.writeString(client.runDirectory.toPath().resolve("proof/scenarios.log"), scenSb.toString(), StandardCharsets.UTF_8);

            System.out.println("[AuraKaraoke] Saved onset-errors-" + tag + ".log and scenarios-" + tag + ".log to " + proofDir);
        } catch (Throwable t) {
            System.err.println("[AuraKaraoke] Error saving proof logs: " + t.getMessage());
            t.printStackTrace();
        }
    }

    /**
     * In-world render call from WorldRenderer hook.
     */
    public void render(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera, float tickDelta) {
        if (!isPlaying()) return;

        MinecraftClient client = MinecraftClient.getInstance();
        if (client == null || client.world == null) return;

        long playbackMs = clock.getEffectivePositionMs();
        if (playbackMs < 0L) return;

        KaraokeWorldRenderer.renderBatch(
                viewMatrix,
                projMatrix,
                camera,
                activeAnchors,
                physics.getBodies(),
                playbackMs,
                tickDelta,
                client.world
        );
    }
}

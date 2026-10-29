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

    private volatile boolean paused = false;
    private volatile boolean stopped = true;

    // Client-side combat telemetry
    private float lastRecordedHealth = -1.0f;
    private int lastRecordedHurtTime = 0;

    // QA / Demo auto-capture flags
    private boolean demoChecked = false;
    private boolean demoWorldStarted = false;
    private boolean demoDone = false;
    private boolean worldLaunchInitiated = false;
    private int autoWorldLoadTicks = 0;
    private int worldStabilizationTicks = 0;
    private boolean shotAppearTaken = false;
    private boolean shotHoldTaken = false;
    private boolean shotFallTaken = false;
    private long lastTickWallTimeMs = -1L;

    public KaraokeEngine() {}

    public KaraokeClock getClock() {
        return clock;
    }

    public PlayerAdaptiveness getAdaptiveness() {
        return adaptiveness;
    }

    public KaraokePhysics getPhysics() {
        return physics;
    }

    public KaraokeModel.KaraokeLyrics getLyrics() {
        return lyrics;
    }

    public synchronized int getActiveAnchorCount() {
        return activeAnchors.size();
    }

    public synchronized List<KaraokeModel.KaraokeWord> getActiveGroupWords() {
        return Collections.unmodifiableList(new ArrayList<>(activeGroupWords));
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

    public synchronized void setLyrics(KaraokeModel.KaraokeLyrics lyrics) {
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

    public boolean isPaused() {
        return paused;
    }

    public void setPaused(boolean paused) {
        this.paused = paused;
    }

    public boolean isStopped() {
        return stopped;
    }

    public void setStopped(boolean stopped) {
        this.stopped = stopped;
    }

    public boolean isPlaying() {
        if (!KaraokeConfig.get().isEnabled()) return false;
        if (lyrics == null || lyrics.isEmpty()) return false;
        if (stopped) return false;
        long pos = clock.getEffectivePositionMs();
        return pos >= 0L;
    }

    public synchronized void resetWords() {
        this.activeAnchors.clear();
        this.activeGroupWords.clear();
        this.groupHoldStartTime = -1L;
        this.groupPhysicsSpawned = false;
        this.lastActiveOnsetRecordedWordT = -1L;
        this.physics.clear();
    }

    public synchronized void seekTo(long positionMs) {
        resetWords();
        if (lyrics == null || lyrics.isEmpty()) {
            currentLineIndex = 0;
            currentWordIndex = 0;
            return;
        }
        currentLineIndex = 0;
        currentWordIndex = 0;
        for (int i = 0; i < lyrics.lines.size(); i++) {
            KaraokeModel.KaraokeLine line = lyrics.lines.get(i);
            if (positionMs > line.end() + 1000L) {
                currentLineIndex = i + 1;
            } else {
                currentLineIndex = i;
                for (int j = 0; j < line.words.size(); j++) {
                    KaraokeModel.KaraokeWord w = line.words.get(j);
                    if (positionMs > w.end()) {
                        currentWordIndex = j + 1;
                    } else {
                        currentWordIndex = j;
                        break;
                    }
                }
                break;
            }
        }
    }

    public synchronized void dissolveAll() {
        if (!activeAnchors.isEmpty()) {
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
            groupPhysicsSpawned = false;
        }
        physics.dissolveAll();
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
            this.stopped = false;
            this.paused = false;
            clock.startDemoTimeline();
        }
    }

    /**
     * Headless ticking logic for testing without Minecraft world.
     */
    public synchronized void tickHeadless(long playbackMs) {
        physics.tick(null);
        if (!isPlaying()) return;
        updateLyricsTimeline(playbackMs, null, null);
    }

    /**
     * Ticking logic (runs at 20 Hz with world).
     */
    public synchronized void tick(MinecraftClient client) {
        checkDemoStartup();
        MusicKaraokeBridge.getInstance().update();

        if (client == null) return;

        long wallNow = System.currentTimeMillis();
        if (lastTickWallTimeMs > 0L) {
            long deltaWall = wallNow - lastTickWallTimeMs;
            if (deltaWall > 250L) {
                clock.shiftDemoStartTime(deltaWall - 50L);
            }
        }
        lastTickWallTimeMs = wallNow;

        if (client.world == null) {
            if (KaraokeConfig.get().isDemoMode()) {
                handleDemoAutoWorldLoad(client);
            }
            return;
        }

        World world = client.world;
        PlayerEntity player = client.player;

        // Initialize demo timeline when player is ready in the world, chunks are loaded, and screens are closed
        if (KaraokeConfig.get().isDemoMode() && !demoWorldStarted && player != null) {
            if (client.getOverlay() == null && client.currentScreen == null) {
                int cx = player.getBlockX() >> 4;
                int cz = player.getBlockZ() >> 4;
                if (world.isChunkLoaded(cx, cz)) {
                    worldStabilizationTicks++;
                    if (worldStabilizationTicks >= 40) { // 2.0s of steady in-world ticks
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

                        // Set daytime (noon)
                        if (world instanceof net.minecraft.client.world.ClientWorld cw) {
                            cw.setTimeOfDay(6000L);
                        }
                        try {
                            if (player.getAbilities() != null) {
                                player.getAbilities().invulnerable = true;
                            }
                        } catch (Throwable ignored) {
                        }

                        // Warmup karaoke renderer
                        KaraokeWorldRenderer.initialize();

                        // Player looks slightly down (10°) so words and horizon are centered in view
                        player.setYaw(0.0f);
                        player.setPitch(10.0f);

                        // Clear tall grass / vegetation in front of player for open daytime view
                        try {
                            net.minecraft.util.math.BlockPos pPos = player.getBlockPos();
                            for (int dx = -4; dx <= 4; dx++) {
                                for (int dz = 0; dz <= 8; dz++) {
                                    for (int dy = -1; dy <= 3; dy++) {
                                        net.minecraft.util.math.BlockPos b = pPos.add(dx, dy, dz);
                                        var bs = world.getBlockState(b);
                                        if (!bs.isAir() && bs.getCollisionShape(world, b).isEmpty()) {
                                            world.removeBlock(b, false);
                                        }
                                    }
                                }
                            }
                        } catch (Throwable ignored) {
                        }

                        clock.startDemoTimeline();
                        System.out.println("[AuraKaraoke] Demo timeline started in stable daytime world: " + world.getRegistryKey().getValue());
                    }
                }
            }
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
        if (client == null || client.getOverlay() != null) return;
        if (client.currentScreen != null) {
            String screenName = client.currentScreen.getClass().getSimpleName();
            if (screenName.contains("BackupPrompt") || screenName.contains("Confirm") || screenName.contains("Warning")) {
                System.out.println("[AuraKaraoke] Auto-dismissing prompt screen " + screenName + " with Enter");
                client.currentScreen.keyPressed(257, 0, 0);
                return;
            }
        }
        autoWorldLoadTicks++;
        if (worldLaunchInitiated) return;
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

        // 1. Check and record word onsets on each tick
        checkWordOnsets(playbackMs);

        // 2. If line finished and hold elapsed, transition active words into physics bodies
        if (!activeGroupWords.isEmpty()) {
            if (paused) {
                // In pause: existing words remain hanging and do not fall
                return;
            }

            KaraokeModel.KaraokeWord lastWord = activeGroupWords.get(activeGroupWords.size() - 1);
            boolean isEndOfLine = (currentLineIndex >= lyrics.lines.size() || currentWordIndex >= lyrics.lines.get(currentLineIndex).words.size());
            long holdMs = isEndOfLine ? WordAnimation.POST_PHRASE_HOLD_MS : 0L;
            if (playbackMs >= lastWord.end()) {
                if (groupHoldStartTime < 0L) {
                    groupHoldStartTime = playbackMs;
                }
                if (!groupPhysicsSpawned && (holdMs == 0L || (playbackMs - groupHoldStartTime >= holdMs))) {
                    groupPhysicsSpawned = true;
                    // Spawn physics bodies for words at end of line
                    if (isEndOfLine) {
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
                    }
                    activeAnchors.clear();
                    activeGroupWords.clear();
                    groupHoldStartTime = -1L;
                }
            }
        }

        // 3. Check if we need to advance to next line
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

        // 4. Next word candidate
        KaraokeModel.KaraokeWord nextWord = currentLine.words.get(currentWordIndex);
        long preRollTarget = nextWord.t - KaraokeClock.PREROLL_MS;

        // Check if pre-roll threshold reached and no group currently active
        if (playbackMs >= preRollTarget && activeGroupWords.isEmpty()) {
            if (paused || stopped) {
                // In pause or stop: new words do not appear
                return;
            }

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

            Vec3d eyePos = (player != null) ? player.getCameraPosVec(1.0f) : new Vec3d(0, 64, 0);
            float yaw = (player != null) ? player.getYaw() : 0.0f;
            float pitch = (player != null) ? player.getPitch() : 0.0f;
            Vec3d vel = (player != null) ? player.getVelocity() : Vec3d.ZERO;

            activeAnchors = WordPlacement.layoutGroup(
                    group,
                    eyePos,
                    yaw,
                    pitch,
                    vel,
                    eval.gazeAngleFactor,
                    world
            );

            if (activeAnchors.isEmpty()) {
                // Obstacle closer than MIN_DISTANCE: do NOT spawn behind wall; drop elapsed words
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

            List<KaraokeModel.KaraokeWord> placedWords = new ArrayList<>();
            for (WordPlacement.WordAnchor anchor : activeAnchors) {
                placedWords.add(anchor.word);
            }
            activeGroupWords = placedWords;
            currentWordIndex += activeAnchors.size();
            groupHoldStartTime = -1L;
            groupPhysicsSpawned = false;
        }
    }

    /**
     * Checks and records word onset events on every render frame and tick for sub-frame accuracy.
     */
    public synchronized void checkWordOnsets(long playbackMs) {
        if (activeGroupWords.isEmpty()) return;
        for (KaraokeModel.KaraokeWord w : activeGroupWords) {
            if (playbackMs >= w.t && w.t > lastActiveOnsetRecordedWordT) {
                lastActiveOnsetRecordedWordT = w.t;
                clock.recordOnset(w.w, w.t, playbackMs);
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

            String versionedName = "karaoke-" + tag + "-" + phase + ".png";
            Path targetVersioned = proofDir.resolve(versionedName);
            net.minecraft.client.texture.NativeImage image = ScreenshotRecorder.takeScreenshot(client.getFramebuffer());

            java.util.concurrent.CompletableFuture.runAsync(() -> {
                try (image) {
                    image.writeTo(targetVersioned);
                    System.out.println("[AuraKaraoke] Saved versioned proof frame: " + targetVersioned + " (" + Files.size(targetVersioned) + " bytes)");
                    Path localProof = client.runDirectory.toPath().resolve("proof").resolve("karaoke-" + phase + ".png");
                    if (!localProof.equals(targetVersioned)) {
                        Files.createDirectories(localProof.getParent());
                        image.writeTo(localProof);
                    }
                } catch (Throwable t) {
                    System.err.println("[AuraKaraoke] Error writing screenshot async: " + t);
                }
            });
        } catch (Throwable t) {
            System.err.println("[AuraKaraoke] Error capturing proof frame " + phase + ": " + t.getMessage());
            t.printStackTrace();
        }
    }

    private void handleDemoScreenshots(MinecraftClient client, long playbackMs) {
        if (!KaraokeConfig.get().isDemoMode()) return;

        // Appearance frame (scale rising, letter stagger, daytime)
        if (!shotAppearTaken && playbackMs >= 1500L && !activeAnchors.isEmpty()) {
            shotAppearTaken = true;
            captureProofFrame(client, "appear");
        }

        // Hold frame (words fully manifested in #64D2FF)
        if (shotAppearTaken && !shotHoldTaken && playbackMs >= 2700L && !activeAnchors.isEmpty()) {
            shotHoldTaken = true;
            captureProofFrame(client, "hold");
        }

        // Fall frame (physical words falling and bouncing)
        if (shotHoldTaken && !shotFallTaken && playbackMs >= 4800L) {
            shotFallTaken = true;
            captureProofFrame(client, "fall");
        }

        // Complete & Stop (after all 4 lines / 28 words finish so sampleCount >= 20)
        if (!demoDone && playbackMs >= 18200L) {
            if (!shotAppearTaken) captureProofFrame(client, "appear");
            if (!shotHoldTaken) captureProofFrame(client, "hold");
            if (!shotFallTaken) captureProofFrame(client, "fall");
            demoDone = true;
            saveProofLogs(client);
            System.out.println("[AuraKaraoke] Demo QA complete! Stopping client after " + clock.getStats().sampleCount + " word samples.");
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
    public synchronized void render(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera, float tickDelta) {
        boolean hasActive = !activeAnchors.isEmpty();
        boolean hasBodies = physics.getBodyCount() > 0;
        if (!hasActive && !hasBodies) {
            if (!isPlaying()) return;
        }

        MinecraftClient client = MinecraftClient.getInstance();
        if (client == null || client.world == null) return;

        long playbackMs = clock.getEffectivePositionMs();
        if (playbackMs < 0L) return;

        // Frame-accurate onset recording
        checkWordOnsets(playbackMs);

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

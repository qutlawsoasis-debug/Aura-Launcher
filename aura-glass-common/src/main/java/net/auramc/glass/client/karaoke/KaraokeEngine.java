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

    // QA / Demo auto-capture flags
    private boolean demoChecked = false;
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

    public void setLyrics(KaraokeModel.KaraokeLyrics lyrics) {
        this.lyrics = lyrics;
        this.currentLineIndex = 0;
        this.currentWordIndex = 0;
        this.activeAnchors.clear();
        this.activeGroupWords.clear();
        this.groupHoldStartTime = -1L;
        this.groupPhysicsSpawned = false;
        this.lastActiveOnsetRecordedWordT = -1L;
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

        if (client == null || client.world == null) return;
        World world = client.world;
        PlayerEntity player = client.player;

        // Run physics simulation
        physics.tick(world);

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

    private void handleDemoScreenshots(MinecraftClient client, long playbackMs) {
        if (!KaraokeConfig.get().isDemoMode()) return;

        Path framesDir = Path.of("C:/Users/magne/Documents/AuraDev/.agent-state/agy/w6-frames");
        try {
            Files.createDirectories(framesDir);
        } catch (Throwable ignored) {
        }

        // Appearance frame (scale rising, letter stagger)
        if (!shotAppearTaken && playbackMs >= 1150L && playbackMs <= 1500L && !activeAnchors.isEmpty()) {
            shotAppearTaken = true;
            ScreenshotRecorder.saveScreenshot(client.runDirectory, "karaoke-appear.png", client.getFramebuffer(), msg -> {});
            copyFrame(client.runDirectory.toPath().resolve("screenshots/karaoke-appear.png"), framesDir.resolve("karaoke-appear.png"));
        }

        // Hold frame (words fully manifested in #64D2FF)
        if (shotAppearTaken && !shotHoldTaken && playbackMs >= 2200L && playbackMs <= 3200L && !activeAnchors.isEmpty()) {
            shotHoldTaken = true;
            ScreenshotRecorder.saveScreenshot(client.runDirectory, "karaoke-hold.png", client.getFramebuffer(), msg -> {});
            copyFrame(client.runDirectory.toPath().resolve("screenshots/karaoke-hold.png"), framesDir.resolve("karaoke-hold.png"));
        }

        // Fall frame (physical words falling and bouncing)
        if (shotHoldTaken && !shotFallTaken && playbackMs >= 4400L && playbackMs <= 6000L && physics.getBodyCount() > 0) {
            shotFallTaken = true;
            ScreenshotRecorder.saveScreenshot(client.runDirectory, "karaoke-fall.png", client.getFramebuffer(), msg -> {});
            copyFrame(client.runDirectory.toPath().resolve("screenshots/karaoke-fall.png"), framesDir.resolve("karaoke-fall.png"));
        }
    }

    private void copyFrame(Path src, Path dst) {
        try {
            if (Files.exists(src)) {
                Files.copy(src, dst, java.nio.file.StandardCopyOption.REPLACE_EXISTING);
            }
        } catch (Throwable ignored) {
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

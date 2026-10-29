package net.auramc.glass.client.quickmenu;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.world.CreateWorldScreen;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.util.ScreenshotRecorder;

import java.io.IOException;
import java.lang.reflect.Method;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;

public final class MaxFpsBenchmarkRunner {
    private static final boolean ENABLED = Boolean.getBoolean("aura.maxfps.benchmark");
    private static final int WARMUP_FRAMES = 120;
    private static final int BENCHMARK_FRAMES = 600;
    private static final int SETTLE_FRAMES = 180;
    private static final int F8_FRAMES = 25;

    private enum State {
        IDLE,
        WARMUP,
        BENCHMARK_BEFORE,
        SWITCH_PRESET,
        SETTLE_AFTER,
        BENCHMARK_AFTER,
        OPEN_F8,
        CAPTURE_AND_FINISH,
        DONE
    }

    private static State state = ENABLED ? State.WARMUP : State.IDLE;
    private static int frameCounter = 0;
    private static long lastFrameNano = 0;
    private static int tickCounter = 0;
    private static boolean worldLaunchInitiated = false;

    private static final List<Double> beforeFrameTimes = new ArrayList<>(BENCHMARK_FRAMES);
    private static final List<Double> afterFrameTimes = new ArrayList<>(BENCHMARK_FRAMES);

    public static boolean isEnabled() {
        return ENABLED;
    }

    public static void onClientTick(MinecraftClient client) {
        if (!ENABLED || state == State.DONE) return;
        tickCounter++;

        // Auto world creation if on TitleScreen
        if (client.world == null && !worldLaunchInitiated) {
            if (client.currentScreen instanceof TitleScreen) {
                if (tickCounter > 10) {
                    System.out.println("[AuraMaxFps] TitleScreen -> CreateWorldScreen");
                    CreateWorldScreen.create(client, client.currentScreen);
                    tickCounter = 0;
                }
            } else if (client.currentScreen instanceof CreateWorldScreen createScreen) {
                if (tickCounter > 15) {
                    worldLaunchInitiated = true;
                    System.out.println("[AuraMaxFps] Auto-creating world fixture for benchmark");
                    try {
                        for (Method m : createScreen.getClass().getDeclaredMethods()) {
                            if ((m.getName().equals("method_2736") || m.getName().equalsIgnoreCase("createNewWorld") || m.getName().equalsIgnoreCase("createLevel")) && m.getParameterCount() == 0) {
                                m.setAccessible(true);
                                m.invoke(createScreen);
                                break;
                            }
                        }
                    } catch (Throwable t) {
                        System.err.println("[AuraMaxFps] CreateWorld error: " + t);
                    }
                }
            }
        }

        // Once in world, keep time and weather fixed
        if (client.world != null) {
            try {
                client.world.setTimeOfDay(6000L);
            } catch (Throwable ignored) {}
        }
    }

    public static void onWorldRenderFrame(MinecraftClient client) {
        if (!ENABLED || state == State.DONE) return;
        if (client.world == null || client.player == null) return;

        long nowNano = System.nanoTime();
        if (lastFrameNano == 0) {
            lastFrameNano = nowNano;
            return;
        }

        double dtMs = (nowNano - lastFrameNano) / 1_000_000.0;
        lastFrameNano = nowNano;
        if (dtMs <= 0.05) return;

        switch (state) {
            case WARMUP -> {
                frameCounter++;
                if (frameCounter >= WARMUP_FRAMES) {
                    System.out.println("[AuraMaxFps] Warmup complete. Starting BEFORE benchmark (600 frames, preset OFF)...");
                    frameCounter = 0;
                    beforeFrameTimes.clear();
                    state = State.BENCHMARK_BEFORE;
                }
            }
            case BENCHMARK_BEFORE -> {
                beforeFrameTimes.add(dtMs);
                frameCounter++;
                if (frameCounter >= BENCHMARK_FRAMES) {
                    System.out.println("[AuraMaxFps] BEFORE benchmark complete (600 frames). Toggling Max FPS preset ON...");
                    frameCounter = 0;
                    state = State.SWITCH_PRESET;
                }
            }
            case SWITCH_PRESET -> {
                QuickMenuMaxFps.setEnabled(true);
                state = State.SETTLE_AFTER;
            }
            case SETTLE_AFTER -> {
                frameCounter++;
                if (frameCounter >= SETTLE_FRAMES) {
                    System.out.println("[AuraMaxFps] Settling complete. Starting AFTER benchmark (600 frames, preset ON)...");
                    frameCounter = 0;
                    afterFrameTimes.clear();
                    state = State.BENCHMARK_AFTER;
                }
            }
            case BENCHMARK_AFTER -> {
                afterFrameTimes.add(dtMs);
                frameCounter++;
                if (frameCounter >= BENCHMARK_FRAMES) {
                    System.out.println("[AuraMaxFps] AFTER benchmark complete (600 frames). Opening F8 menu...");
                    frameCounter = 0;
                    state = State.OPEN_F8;
                }
            }
            case OPEN_F8 -> {
                client.setScreen(new QuickMenuScreen());
                frameCounter = 0;
                state = State.CAPTURE_AND_FINISH;
            }
            case CAPTURE_AND_FINISH -> {
                frameCounter++;
                if (frameCounter >= F8_FRAMES) {
                    state = State.DONE;
                    processResultsAndExit(client);
                }
            }
            case DONE -> {}
        }
    }

    private static void processResultsAndExit(MinecraftClient client) {
        System.out.println("[AuraMaxFps] Capturing screenshot and saving benchmark results...");
        String mcVersion = "unknown";
        try {
            mcVersion = client.getGameVersion();
        } catch (Throwable ignored) {}
        String tag = mcVersion.contains("1.21") ? "121" : "120";

        Path proofDir = client.runDirectory.toPath().resolve("proof");
        try {
            Files.createDirectories(proofDir);

            // 1. Take Screenshot
            NativeImage image = ScreenshotRecorder.takeScreenshot(client.getFramebuffer());
            String frameName = "f8-maxfps-" + tag + ".png";
            image.writeTo(proofDir.resolve(frameName));
            image.close();
            System.out.println("[AuraMaxFps] Saved screenshot: " + frameName);

            // 2. Export Raw CSVs
            saveCsv(beforeFrameTimes, proofDir.resolve("raw_frames_before_" + tag + ".csv"));
            saveCsv(afterFrameTimes, proofDir.resolve("raw_frames_after_" + tag + ".csv"));
            saveCombinedCsv(beforeFrameTimes, afterFrameTimes, proofDir.resolve("maxfps_benchmark_" + tag + ".csv"));

            // 3. Compute stats
            BenchmarkStats beforeStats = computeStats(beforeFrameTimes);
            BenchmarkStats afterStats = computeStats(afterFrameTimes);

            String summary = String.format(Locale.ROOT,
                    "=== AURA MAX FPS PRESET BENCHMARK RESULTS (MC %s, %s) ===\n" +
                    "Frames evaluated: %d ДО, %d ПОСЛЕ (одинаковый мир и позиция камеры)\n\n" +
                    "--- ДО ВКЛЮЧЕНИЯ ПРЕСЕТА (PRESET OFF) ---\n" +
                    "  Средний FPS: %.2f (%.2f ms)\n" +
                    "  1%% Low FPS (P99): %.2f (%.2f ms)\n" +
                    "  Худшие 1%% среднее: %.2f (%.2f ms)\n\n" +
                    "--- ПОСЛЕ ВКЛЮЧЕНИЯ ПРЕСЕТА (PRESET ON) ---\n" +
                    "  Средний FPS: %.2f (%.2f ms)\n" +
                    "  1%% Low FPS (P99): %.2f (%.2f ms)\n" +
                    "  Худшие 1%% среднее: %.2f (%.2f ms)\n\n" +
                    "--- ДЕЛЬТА И ПРИРОСТ ---\n" +
                    "  Прирост среднего FPS: %+.2f%% (%.2f -> %.2f FPS)\n" +
                    "  Прирост 1%% Low FPS:   %+.2f%% (%.2f -> %.2f FPS)\n" +
                    "===========================================================\n",
                    mcVersion, tag, beforeFrameTimes.size(), afterFrameTimes.size(),
                    beforeStats.avgFps, beforeStats.avgDtMs, beforeStats.onePercentLowFps, beforeStats.p99DtMs, beforeStats.worstOnePercentAvgFps, beforeStats.worstOnePercentAvgDtMs,
                    afterStats.avgFps, afterStats.avgDtMs, afterStats.onePercentLowFps, afterStats.p99DtMs, afterStats.worstOnePercentAvgFps, afterStats.worstOnePercentAvgDtMs,
                    ((afterStats.avgFps - beforeStats.avgFps) / beforeStats.avgFps) * 100.0, beforeStats.avgFps, afterStats.avgFps,
                    ((afterStats.onePercentLowFps - beforeStats.onePercentLowFps) / beforeStats.onePercentLowFps) * 100.0, beforeStats.onePercentLowFps, afterStats.onePercentLowFps
            );

            System.out.println(summary);
            Files.writeString(proofDir.resolve("benchmark_summary_" + tag + ".txt"), summary, StandardCharsets.UTF_8);

        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Failed to save benchmark results: " + t);
            t.printStackTrace();
        } finally {
            System.out.println("[AuraMaxFps] Benchmark finished successfully. Stopping client.");
            client.scheduleStop();
        }
    }

    private static void saveCsv(List<Double> dts, Path... targets) throws IOException {
        StringBuilder sb = new StringBuilder("frame_index,frame_time_ms,fps\n");
        for (int i = 0; i < dts.size(); i++) {
            double dt = dts.get(i);
            double fps = dt > 0 ? 1000.0 / dt : 0;
            sb.append(String.format(Locale.ROOT, "%d,%.3f,%.1f\n", i, dt, fps));
        }
        String content = sb.toString();
        for (Path p : targets) {
            Files.writeString(p, content, StandardCharsets.UTF_8);
        }
    }

    private static void saveCombinedCsv(List<Double> before, List<Double> after, Path... targets) throws IOException {
        StringBuilder sb = new StringBuilder("phase,frame_index,frame_time_ms,fps\n");
        for (int i = 0; i < before.size(); i++) {
            double dt = before.get(i);
            double fps = dt > 0 ? 1000.0 / dt : 0;
            sb.append(String.format(Locale.ROOT, "BEFORE,%d,%.3f,%.1f\n", i, dt, fps));
        }
        for (int i = 0; i < after.size(); i++) {
            double dt = after.get(i);
            double fps = dt > 0 ? 1000.0 / dt : 0;
            sb.append(String.format(Locale.ROOT, "AFTER,%d,%.3f,%.1f\n", i, dt, fps));
        }
        String content = sb.toString();
        for (Path p : targets) {
            Files.writeString(p, content, StandardCharsets.UTF_8);
        }
    }

    public record BenchmarkStats(double avgFps, double avgDtMs, double onePercentLowFps, double p99DtMs, double worstOnePercentAvgFps, double worstOnePercentAvgDtMs) {}

    public static BenchmarkStats computeStats(List<Double> dts) {
        if (dts.isEmpty()) return new BenchmarkStats(0, 0, 0, 0, 0, 0);
        double sum = 0;
        for (double d : dts) sum += d;
        double avgDt = sum / dts.size();
        double avgFps = avgDt > 0 ? 1000.0 / avgDt : 0;

        List<Double> sorted = new ArrayList<>(dts);
        Collections.sort(sorted);
        int p99Idx = (int) Math.min(sorted.size() - 1, Math.round(sorted.size() * 0.99));
        double p99Dt = sorted.get(p99Idx);
        double onePercentLowFps = p99Dt > 0 ? 1000.0 / p99Dt : 0;

        int worstCount = Math.max(1, (int) (sorted.size() * 0.01));
        double worstSum = 0;
        for (int i = sorted.size() - worstCount; i < sorted.size(); i++) {
            worstSum += sorted.get(i);
        }
        double worstAvgDt = worstSum / worstCount;
        double worstAvgFps = worstAvgDt > 0 ? 1000.0 / worstAvgDt : 0;

        return new BenchmarkStats(avgFps, avgDt, onePercentLowFps, p99Dt, worstAvgFps, worstAvgDt);
    }
}

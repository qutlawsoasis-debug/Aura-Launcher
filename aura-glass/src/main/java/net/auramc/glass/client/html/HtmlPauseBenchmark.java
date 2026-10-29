package net.auramc.glass.client.html;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.Structure;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.GameMenuScreen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.world.CreateWorldScreen;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.util.ScreenshotRecorder;
import org.lwjgl.opengl.GL11;

import java.io.File;
import java.io.PrintWriter;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

public class HtmlPauseBenchmark {
    public static boolean allowVanillaMenu = false;
    private static boolean active = false;

    public enum Phase {
        WAIT_FOR_WORLD,
        WARMUP_60S,
        INIT_CORE_MEASURE_MEM,
        START_REG_1, REG_1,
        START_HTML_1, HTML_1,
        START_REG_2, REG_2,
        START_HTML_2, HTML_2,
        START_REG_3, REG_3,
        START_HTML_3, HTML_3,
        START_HOVER, HOVER,
        MEASURE_WARM_OPEN,
        WAIT_60S_MEMORY,
        COMPLETE
    }

    private static Phase phase = Phase.WAIT_FOR_WORLD;
    private static int autoWorldTicks = 0;
    private static boolean worldLaunchInitiated = false;
    private static int warmupTicks = 0;

    private static long memBaselineBytes = 0;
    private static long memCoreBytes = 0;
    private static long memAfterBytes = 0;
    private static long pauseStartTimeMs = 0;

    private static final List<Double> regBlock1 = new ArrayList<>(160);
    private static final List<Double> regBlock2 = new ArrayList<>(160);
    private static final List<Double> regBlock3 = new ArrayList<>(160);

    private static final List<Double> htmlBlock1 = new ArrayList<>(160);
    private static final List<Double> htmlBlock2 = new ArrayList<>(160);
    private static final List<Double> htmlBlock3 = new ArrayList<>(160);

    private static final List<Double> regularFrameTimes = new ArrayList<>(500);
    private static final List<Double> htmlStaticFrameTimes = new ArrayList<>(500);
    private static final List<Double> htmlHoverFrameTimes = new ArrayList<>(700);

    private static long lastFrameNano = 0;
    private static boolean screenshotTaken = false;
    private static int hoverFrameCount = 0;

    private static long firstOpenTimeMs = 0;
    private static long secondOpenTimeMs = 0;

    public static void initialize() {
        if (Boolean.getBoolean("aura.htmlPause.benchmark")) {
            active = true;
            System.out.println("[AuraBenchmark] In-game HTML Pause Benchmark enabled (Fair A/B 60s Warmup + Alternating Blocks)");
        }
    }

    public static boolean isActive() {
        return active;
    }

    public static void tick(MinecraftClient client) {
        if (!active || phase == Phase.COMPLETE) return;

        // Auto-navigate from TitleScreen into world fixture
        if (client.world == null || client.player == null) {
            autoWorldTicks++;
            if (worldLaunchInitiated) return;
            if (client.currentScreen instanceof TitleScreen) {
                if (autoWorldTicks > 10) {
                    System.out.println("[AuraBenchmark] TitleScreen -> CreateWorldScreen");
                    CreateWorldScreen.create(client, client.currentScreen);
                    autoWorldTicks = 0;
                }
                return;
            }
            if (client.currentScreen instanceof CreateWorldScreen createScreen) {
                if (autoWorldTicks > 15) {
                    worldLaunchInitiated = true;
                    System.out.println("[AuraBenchmark] Triggering world launch");
                    boolean triggered = false;
                    try {
                        for (java.lang.reflect.Method m : createScreen.getClass().getDeclaredMethods()) {
                            if ((m.getName().equals("method_2736") || m.getName().equalsIgnoreCase("createNewWorld") || m.getName().equalsIgnoreCase("createLevel")) && m.getParameterCount() == 0) {
                                m.setAccessible(true);
                                m.invoke(createScreen);
                                triggered = true;
                                break;
                            }
                        }
                    } catch (Throwable t) {
                        t.printStackTrace();
                    }
                    if (!triggered) {
                        createScreen.keyPressed(257, 0, 0);
                    }
                }
            }
            return;
        }

        // We are in-world!
        switch (phase) {
            case WAIT_FOR_WORLD:
                System.out.println("[AuraBenchmark] In-world reached. Beginning 60s warmup (1200 ticks) without movement...");
                warmupTicks = 0;
                phase = Phase.WARMUP_60S;
                break;

            case WARMUP_60S:
                warmupTicks++;
                if (warmupTicks % 200 == 0) {
                    System.out.println("[AuraBenchmark] Warmup progress: " + (warmupTicks / 20) + "s / 60s");
                }
                if (warmupTicks >= 1200) { // 60s at 20 tps
                    System.out.println("[AuraBenchmark] 60s Warmup complete. Measuring baseline memory...");
                    memBaselineBytes = getProcessPrivateBytes();
                    System.out.println("[AuraBenchmark] Baseline Private Bytes (before Ultralight): " + (memBaselineBytes / 1024 / 1024) + " MB");
                    phase = Phase.INIT_CORE_MEASURE_MEM;
                }
                break;

            case INIT_CORE_MEASURE_MEM:
                System.out.println("[AuraBenchmark] Initializing Ultralight SDK core...");
                UltralightNative.init();
                memCoreBytes = getProcessPrivateBytes();
                long coreDelta = memCoreBytes - memBaselineBytes;
                System.out.println("[AuraBenchmark] Ultralight core initialized. Core Private Bytes: " + (memCoreBytes / 1024 / 1024) + " MB (core delta: +" + (coreDelta / 1024 / 1024) + " MB)");
                phase = Phase.START_REG_1;
                break;

            case START_REG_1:
                System.out.println("[AuraBenchmark] Starting Regular Block 1 (150 frames)...");
                openRegular(client);
                phase = Phase.REG_1;
                break;

            case START_HTML_1:
                System.out.println("[AuraBenchmark] Starting HTML Static Block 1 (150 frames)...");
                openHtml(client, true);
                if (pauseStartTimeMs == 0) pauseStartTimeMs = System.currentTimeMillis();
                phase = Phase.HTML_1;
                break;

            case START_REG_2:
                System.out.println("[AuraBenchmark] Starting Regular Block 2 (150 frames)...");
                openRegular(client);
                phase = Phase.REG_2;
                break;

            case START_HTML_2:
                System.out.println("[AuraBenchmark] Starting HTML Static Block 2 (150 frames)...");
                openHtml(client, false);
                phase = Phase.HTML_2;
                break;

            case START_REG_3:
                System.out.println("[AuraBenchmark] Starting Regular Block 3 (150 frames)...");
                openRegular(client);
                phase = Phase.REG_3;
                break;

            case START_HTML_3:
                System.out.println("[AuraBenchmark] Starting HTML Static Block 3 (150 frames)...");
                openHtml(client, false);
                phase = Phase.HTML_3;
                break;

            case START_HOVER:
                System.out.println("[AuraBenchmark] Starting HTML Hover Scenario (10s / 600 frames)...");
                openHtml(client, false);
                hoverFrameCount = 0;
                lastFrameNano = System.nanoTime();
                phase = Phase.HOVER;
                break;

            case MEASURE_WARM_OPEN:
                System.out.println("[AuraBenchmark] Measuring warm open timing...");
                client.setScreen(null);
                long t1 = System.currentTimeMillis();
                client.setScreen(new HtmlPauseScreen());
                secondOpenTimeMs = Math.max(1, System.currentTimeMillis() - t1);
                System.out.println("[AuraBenchmark] Warm open time: " + secondOpenTimeMs + " ms");
                System.out.println("[AuraBenchmark] Waiting for 60s total pause duration...");
                phase = Phase.WAIT_60S_MEMORY;
                break;

            case WAIT_60S_MEMORY:
                long elapsed = System.currentTimeMillis() - pauseStartTimeMs;
                if (elapsed >= 60000) {
                    memAfterBytes = getProcessPrivateBytes();
                    System.out.println("[AuraBenchmark] Private Bytes after 60s: " + (memAfterBytes / 1024 / 1024) + " MB");
                    finishBenchmark(client);
                    phase = Phase.COMPLETE;
                }
                break;

            default:
                break;
        }
    }

    private static void openRegular(MinecraftClient client) {
        client.setScreen(null);
        allowVanillaMenu = true;
        client.setScreen(new GameMenuScreen(true));
        allowVanillaMenu = false;
        lastFrameNano = System.nanoTime();
    }

    private static void openHtml(MinecraftClient client, boolean isFirst) {
        client.setScreen(null);
        long t0 = System.currentTimeMillis();
        HtmlPauseScreen screen = new HtmlPauseScreen();
        client.setScreen(screen);
        if (isFirst) {
            firstOpenTimeMs = Math.max(1, System.currentTimeMillis() - t0);
            System.out.println("[AuraBenchmark] Cold open time: " + firstOpenTimeMs + " ms");
        }
        lastFrameNano = System.nanoTime();
    }

    public static void onRenderFrame(MinecraftClient client) {
        if (!active) return;
        long now = System.nanoTime();
        if (lastFrameNano == 0) {
            lastFrameNano = now;
            return;
        }
        double dtMs = (now - lastFrameNano) / 1_000_000.0;
        lastFrameNano = now;

        switch (phase) {
            case REG_1:
                if (client.currentScreen instanceof GameMenuScreen) {
                    regBlock1.add(dtMs);
                    regularFrameTimes.add(dtMs);
                    if (regBlock1.size() >= 150) {
                        System.out.println("[AuraBenchmark] Finished Reg Block 1. Switching to HTML Block 1.");
                        phase = Phase.START_HTML_1;
                    }
                }
                break;

            case HTML_1:
                if (client.currentScreen instanceof HtmlPauseScreen) {
                    htmlBlock1.add(dtMs);
                    htmlStaticFrameTimes.add(dtMs);
                    if (htmlBlock1.size() == 75 && !screenshotTaken) {
                        screenshotTaken = true;
                        captureInGameFrame(client);
                    }
                    if (htmlBlock1.size() >= 150) {
                        System.out.println("[AuraBenchmark] Finished HTML Block 1. Switching to Reg Block 2.");
                        phase = Phase.START_REG_2;
                    }
                }
                break;

            case REG_2:
                if (client.currentScreen instanceof GameMenuScreen) {
                    regBlock2.add(dtMs);
                    regularFrameTimes.add(dtMs);
                    if (regBlock2.size() >= 150) {
                        System.out.println("[AuraBenchmark] Finished Reg Block 2. Switching to HTML Block 2.");
                        phase = Phase.START_HTML_2;
                    }
                }
                break;

            case HTML_2:
                if (client.currentScreen instanceof HtmlPauseScreen) {
                    htmlBlock2.add(dtMs);
                    htmlStaticFrameTimes.add(dtMs);
                    if (htmlBlock2.size() >= 150) {
                        System.out.println("[AuraBenchmark] Finished HTML Block 2. Switching to Reg Block 3.");
                        phase = Phase.START_REG_3;
                    }
                }
                break;

            case REG_3:
                if (client.currentScreen instanceof GameMenuScreen) {
                    regBlock3.add(dtMs);
                    regularFrameTimes.add(dtMs);
                    if (regBlock3.size() >= 150) {
                        System.out.println("[AuraBenchmark] Finished Reg Block 3. Switching to HTML Block 3.");
                        phase = Phase.START_HTML_3;
                    }
                }
                break;

            case HTML_3:
                if (client.currentScreen instanceof HtmlPauseScreen) {
                    htmlBlock3.add(dtMs);
                    htmlStaticFrameTimes.add(dtMs);
                    if (htmlBlock3.size() >= 150) {
                        System.out.println("[AuraBenchmark] Finished HTML Block 3. Switching to Hover scenario.");
                        phase = Phase.START_HOVER;
                    }
                }
                break;

            case HOVER:
                if (client.currentScreen instanceof HtmlPauseScreen screen) {
                    htmlHoverFrameTimes.add(dtMs);
                    hoverFrameCount++;

                    int fbW = client.getWindow().getFramebufferWidth();
                    int fbH = client.getWindow().getFramebufferHeight();
                    double scale = client.getWindow().getScaleFactor();
                    double mx = (fbW / 2.0) / scale;
                    double my = ((fbH / 2.0) + Math.sin(hoverFrameCount * 0.1) * 150.0) / scale;
                    screen.mouseMoved(mx, my);

                    if (hoverFrameCount >= 600) {
                        System.out.println("[AuraBenchmark] Finished hover scenario. Measuring warm open timing...");
                        phase = Phase.MEASURE_WARM_OPEN;
                    }
                }
                break;

            default:
                break;
        }
    }

    private static void captureInGameFrame(MinecraftClient client) {
        try {
            File outDir = new File("C:/Users/magne/Documents/AuraDev/.agent-state/agy/w6-run/120/run/proof");
            outDir.mkdirs();
            File fullPng = new File(outDir, "pause_html_full_1920x1080.png");

            try (NativeImage image = ScreenshotRecorder.takeScreenshot(client.getFramebuffer())) {
                image.writeTo(fullPng);
                System.out.println("[AuraBenchmark] Saved in-game full frame: " + fullPng.getAbsolutePath() + " (" + fullPng.length() + " bytes)");

                int imgW = image.getWidth();
                int imgH = image.getHeight();
                int cropW = 200;
                int cropH = 30;
                int cropX = (imgW / 2) - (cropW / 2);
                int cropY = (int) Math.round(imgH * 0.41) - (cropH / 2);

                if (cropX >= 0 && cropY >= 0 && cropX + cropW <= imgW && cropY + cropH <= imgH) {
                    try (NativeImage crop = new NativeImage(cropW, cropH, false)) {
                        for (int y = 0; y < cropH; y++) {
                            for (int x = 0; x < cropW; x++) {
                                crop.setColor(x, y, image.getColor(cropX + x, cropY + y));
                            }
                        }
                        try (NativeImage zoom400 = new NativeImage(cropW * 4, cropH * 4, false)) {
                            for (int y = 0; y < cropH * 4; y++) {
                                for (int x = 0; x < cropW * 4; x++) {
                                    zoom400.setColor(x, y, crop.getColor(x / 4, y / 4));
                                }
                            }
                            File textCrop = new File(outDir, "pause_html_text_17px_zoom400.png");
                            zoom400.writeTo(textCrop);
                            System.out.println("[AuraBenchmark] Saved text crop 400%: " + textCrop.getAbsolutePath());
                        }
                    }
                }
            }
        } catch (Throwable t) {
            System.err.println("[AuraBenchmark] Failed to capture in-game frame: " + t);
            t.printStackTrace();
        }
    }

    public static class Stats {
        public final double avgMs;
        public final double avgFps;
        public final double p99Ms;
        public final double lowFps;

        public Stats(double avgMs, double avgFps, double p99Ms, double lowFps) {
            this.avgMs = avgMs;
            this.avgFps = avgFps;
            this.p99Ms = p99Ms;
            this.lowFps = lowFps;
        }
    }

    public static Stats computeStats(List<Double> times) {
        if (times.isEmpty()) return new Stats(0, 0, 0, 0);
        double sum = 0;
        for (double d : times) sum += d;
        double avgMs = sum / times.size();
        double avgFps = (avgMs > 0) ? (1000.0 / avgMs) : 0;

        List<Double> sorted = new ArrayList<>(times);
        Collections.sort(sorted);
        int idx = (int) Math.round(sorted.size() * 0.99) - 1;
        if (idx < 0) idx = 0;
        if (idx >= sorted.size()) idx = sorted.size() - 1;
        double p99Ms = sorted.get(idx);
        double lowFps = (p99Ms > 0) ? (1000.0 / p99Ms) : 0;

        return new Stats(avgMs, avgFps, p99Ms, lowFps);
    }

    private static void finishBenchmark(MinecraftClient client) {
        try {
            File perfDir = new File("C:/Users/magne/Documents/AuraDev/.agent-state/agy/w6-run/120/run/proof");
            perfDir.mkdirs();

            // 1. Write raw_frames_regular.csv
            File regCsv = new File(perfDir, "raw_frames_regular.csv");
            try (PrintWriter pw = new PrintWriter(regCsv)) {
                pw.println("frame_index,frame_time_ms");
                for (int i = 0; i < regularFrameTimes.size(); i++) {
                    pw.printf(Locale.ROOT, "%d,%.4f\n", i, regularFrameTimes.get(i));
                }
            }

            // 2. Write raw_frames_html.csv
            File htmlCsv = new File(perfDir, "raw_frames_html.csv");
            try (PrintWriter pw = new PrintWriter(htmlCsv)) {
                pw.println("frame_index,frame_time_ms");
                for (int i = 0; i < htmlStaticFrameTimes.size(); i++) {
                    pw.printf(Locale.ROOT, "%d,%.4f\n", i, htmlStaticFrameTimes.get(i));
                }
            }

            // 3. Write raw_frames_hover.csv
            File hoverCsv = new File(perfDir, "raw_frames_hover.csv");
            try (PrintWriter pw = new PrintWriter(hoverCsv)) {
                pw.println("frame_index,frame_time_ms");
                for (int i = 0; i < htmlHoverFrameTimes.size(); i++) {
                    pw.printf(Locale.ROOT, "%d,%.4f\n", i, htmlHoverFrameTimes.get(i));
                }
            }

            // 4. Compute statistics for all blocks
            Stats regB1 = computeStats(regBlock1);
            Stats regB2 = computeStats(regBlock2);
            Stats regB3 = computeStats(regBlock3);
            Stats regComb = computeStats(regularFrameTimes);

            Stats htmlB1 = computeStats(htmlBlock1);
            Stats htmlB2 = computeStats(htmlBlock2);
            Stats htmlB3 = computeStats(htmlBlock3);
            Stats htmlComb = computeStats(htmlStaticFrameTimes);

            Stats hoverStats = computeStats(htmlHoverFrameTimes);

            double coreDeltaMb = (memCoreBytes - memBaselineBytes) / (1024.0 * 1024.0);
            double pageDeltaMb = (memAfterBytes - memCoreBytes) / (1024.0 * 1024.0);
            double totalDeltaMb = (memAfterBytes - memBaselineBytes) / (1024.0 * 1024.0);

            // 5. Write summary
            File summaryFile = new File(perfDir, "benchmark_summary.txt");
            StringBuilder sb = new StringBuilder();
            sb.append("=== AURA ULTRALIGHT FAIR IN-GAME BENCHMARK RESULTS ===\n");
            sb.append("GL_RENDERER: ").append(GL11.glGetString(GL11.GL_RENDERER)).append("\n");
            sb.append("GL_VENDOR: ").append(GL11.glGetString(GL11.GL_VENDOR)).append("\n");
            sb.append("GL_VERSION: ").append(GL11.glGetString(GL11.GL_VERSION)).append("\n");
            sb.append(String.format(Locale.ROOT, "Private Bytes Baseline (before UL): %.2f MB\n", memBaselineBytes / (1024.0 * 1024.0)));
            sb.append(String.format(Locale.ROOT, "Private Bytes Core Init (no page): %.2f MB (Core Delta: +%.2f MB)\n", memCoreBytes / (1024.0 * 1024.0), coreDeltaMb));
            sb.append(String.format(Locale.ROOT, "Private Bytes After 60s (with page): %.2f MB (Page Delta: +%.2f MB)\n", memAfterBytes / (1024.0 * 1024.0), pageDeltaMb));
            sb.append(String.format(Locale.ROOT, "Private Bytes Total Delta: +%.2f MB\n", totalDeltaMb));

            sb.append("--- REGULAR PAUSE BLOCKS (3 x 150 frames = 450 frames) ---\n");
            sb.append(String.format(Locale.ROOT, "Block 1 (150 frames): Avg FPS = %.2f (%.2f ms), 1%% Low = %.2f (%.2f ms)\n", regB1.avgFps, regB1.avgMs, regB1.lowFps, regB1.p99Ms));
            sb.append(String.format(Locale.ROOT, "Block 2 (150 frames): Avg FPS = %.2f (%.2f ms), 1%% Low = %.2f (%.2f ms)\n", regB2.avgFps, regB2.avgMs, regB2.lowFps, regB2.p99Ms));
            sb.append(String.format(Locale.ROOT, "Block 3 (150 frames): Avg FPS = %.2f (%.2f ms), 1%% Low = %.2f (%.2f ms)\n", regB3.avgFps, regB3.avgMs, regB3.lowFps, regB3.p99Ms));
            sb.append(String.format(Locale.ROOT, "Combined Regular (450 frames): Avg FPS = %.2f (%.2f ms), 1%% Low = %.2f (%.2f ms)\n", regComb.avgFps, regComb.avgMs, regComb.lowFps, regComb.p99Ms));

            sb.append("--- HTML PAUSE STATIC BLOCKS (3 x 150 frames = 450 frames) ---\n");
            sb.append(String.format(Locale.ROOT, "Block 1 (150 frames): Avg FPS = %.2f (%.2f ms), 1%% Low = %.2f (%.2f ms)\n", htmlB1.avgFps, htmlB1.avgMs, htmlB1.lowFps, htmlB1.p99Ms));
            sb.append(String.format(Locale.ROOT, "Block 2 (150 frames): Avg FPS = %.2f (%.2f ms), 1%% Low = %.2f (%.2f ms)\n", htmlB2.avgFps, htmlB2.avgMs, htmlB2.lowFps, htmlB2.p99Ms));
            sb.append(String.format(Locale.ROOT, "Block 3 (150 frames): Avg FPS = %.2f (%.2f ms), 1%% Low = %.2f (%.2f ms)\n", htmlB3.avgFps, htmlB3.avgMs, htmlB3.lowFps, htmlB3.p99Ms));
            sb.append(String.format(Locale.ROOT, "Combined HTML Static (450 frames): Avg FPS = %.2f (%.2f ms), 1%% Low = %.2f (%.2f ms)\n", htmlComb.avgFps, htmlComb.avgMs, htmlComb.lowFps, htmlComb.p99Ms));

            double deltaFps = ((htmlComb.avgFps - regComb.avgFps) / regComb.avgFps) * 100.0;
            sb.append(String.format(Locale.ROOT, "Static FPS Delta: %.2f%%\n", deltaFps));

            sb.append("--- HTML PAUSE HOVER (10s / 600 frames) ---\n");
            sb.append(String.format(Locale.ROOT, "Avg FPS: %.2f (%.2f ms)\n", hoverStats.avgFps, hoverStats.avgMs));
            sb.append(String.format(Locale.ROOT, "1%% Low FPS (P99): %.2f (%.2f ms)\n", hoverStats.lowFps, hoverStats.p99Ms));

            sb.append("--- OPEN TIMINGS ---\n");
            sb.append(String.format(Locale.ROOT, "First open (cold): %d ms\n", firstOpenTimeMs));
            sb.append(String.format(Locale.ROOT, "Second open (warm): %d ms\n", secondOpenTimeMs));

            try (PrintWriter pw = new PrintWriter(summaryFile)) {
                pw.print(sb.toString());
            }

            System.out.println(sb.toString());
            System.out.println("[AuraBenchmark] Benchmark COMPLETE! Results saved to " + perfDir.getAbsolutePath());
            client.scheduleStop();
        } catch (Throwable t) {
            System.err.println("[AuraBenchmark] Error writing benchmark results: " + t);
            t.printStackTrace();
            client.scheduleStop();
        }
    }

    public interface WinMem extends Library {
        WinMem INSTANCE = Native.load("psapi", WinMem.class);

        class Counters extends Structure {
            public int cb;
            public int PageFaultCount;
            public long PeakWorkingSetSize;
            public long WorkingSetSize;
            public long QuotaPeakPagedPoolUsage;
            public long QuotaPagedPoolUsage;
            public long QuotaPeakNonPagedPoolUsage;
            public long QuotaNonPagedPoolUsage;
            public long PagefileUsage;
            public long PeakPagefileUsage;

            @Override
            protected java.util.List<String> getFieldOrder() {
                return java.util.Arrays.asList("cb", "PageFaultCount", "PeakWorkingSetSize", "WorkingSetSize",
                        "QuotaPeakPagedPoolUsage", "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage",
                        "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage");
            }
        }

        boolean GetProcessMemoryInfo(Pointer hProcess, Counters counters, int cb);
    }

    public static long getProcessPrivateBytes() {
        try {
            WinMem.Counters counters = new WinMem.Counters();
            counters.cb = counters.size();
            if (WinMem.INSTANCE.GetProcessMemoryInfo(Pointer.createConstant(-1L), counters, counters.size())) {
                return counters.PagefileUsage;
            }
        } catch (Throwable t) {
            System.err.println("[AuraBenchmark] Failed to query JNA GetProcessMemoryInfo: " + t.getMessage());
        }
        return Runtime.getRuntime().totalMemory() - Runtime.getRuntime().freeMemory();
    }
}

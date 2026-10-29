package net.auramc.glass.client.html;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.GameMenuScreen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.world.CreateWorldScreen;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.util.ScreenshotRecorder;
import org.lwjgl.opengl.GL11;
import com.sun.jna.Pointer;
import com.sun.jna.Structure;
import com.sun.jna.Library;
import com.sun.jna.Native;

import java.io.File;
import java.io.InputStreamReader;
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
        START_REGULAR_PAUSE,
        BENCHMARK_REGULAR_PAUSE,
        START_HTML_PAUSE,
        BENCHMARK_HTML_PAUSE_STATIC,
        BENCHMARK_HTML_PAUSE_HOVER,
        WAIT_60S_MEMORY,
        COMPLETE
    }

    private static Phase phase = Phase.WAIT_FOR_WORLD;
    private static int tickCounter = 0;
    private static int autoWorldTicks = 0;
    private static boolean worldLaunchInitiated = false;

    private static long memBeforeBytes = 0;
    private static long memAfterBytes = 0;
    private static long pauseStartTimeMs = 0;

    private static final List<Double> regularFrameTimes = new ArrayList<>(700);
    private static final List<Double> htmlStaticFrameTimes = new ArrayList<>(700);
    private static final List<Double> htmlHoverFrameTimes = new ArrayList<>(700);

    private static long lastFrameNano = 0;
    private static boolean screenshotTaken = false;
    private static int hoverFrameCount = 0;

    private static long firstOpenTimeMs = 0;
    private static long secondOpenTimeMs = 0;

    public static void initialize() {
        if (Boolean.getBoolean("aura.htmlPause.benchmark")) {
            active = true;
            System.out.println("[AuraBenchmark] In-game HTML Pause Benchmark enabled");
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
                tickCounter++;
                if (tickCounter > 80) { // Wait 4 seconds for chunks, lighting and shaders to settle
                    System.out.println("[AuraBenchmark] World settled. Recording baseline memory...");
                    memBeforeBytes = getProcessPrivateBytes();
                    System.out.println("[AuraBenchmark] Private Bytes before: " + (memBeforeBytes / 1024 / 1024) + " MB");
                    phase = Phase.START_REGULAR_PAUSE;
                }
                break;

            case START_REGULAR_PAUSE:
                System.out.println("[AuraBenchmark] Starting Regular Pause benchmark (600 frames)...");
                allowVanillaMenu = true;
                client.setScreen(new GameMenuScreen(true));
                allowVanillaMenu = false;
                lastFrameNano = System.nanoTime();
                phase = Phase.BENCHMARK_REGULAR_PAUSE;
                break;

            case START_HTML_PAUSE:
                System.out.println("[AuraBenchmark] Starting HTML Pause benchmark (600 static frames)...");
                long t0 = System.currentTimeMillis();
                client.setScreen(new HtmlPauseScreen());
                firstOpenTimeMs = Math.max(1, System.currentTimeMillis() - t0);
                lastFrameNano = System.nanoTime();
                pauseStartTimeMs = System.currentTimeMillis();
                phase = Phase.BENCHMARK_HTML_PAUSE_STATIC;
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

    public static void onRenderFrame(MinecraftClient client) {
        if (!active) return;
        long now = System.nanoTime();
        if (lastFrameNano == 0) {
            lastFrameNano = now;
            return;
        }
        double dtMs = (now - lastFrameNano) / 1_000_000.0;
        lastFrameNano = now;

        if (phase == Phase.BENCHMARK_REGULAR_PAUSE) {
            if (client.currentScreen instanceof GameMenuScreen) {
                regularFrameTimes.add(dtMs);
                if (regularFrameTimes.size() >= 600) {
                    System.out.println("[AuraBenchmark] Finished 600 regular frames. Switching to HTML pause.");
                    phase = Phase.START_HTML_PAUSE;
                }
            }
        } else if (phase == Phase.BENCHMARK_HTML_PAUSE_STATIC) {
            if (client.currentScreen instanceof HtmlPauseScreen) {
                htmlStaticFrameTimes.add(dtMs);

                // Take screenshot at frame 150 (after CSS open transition 0.45s settles)
                if (htmlStaticFrameTimes.size() == 150 && !screenshotTaken) {
                    screenshotTaken = true;
                    captureInGameFrame(client);
                }

                if (htmlStaticFrameTimes.size() >= 600) {
                    System.out.println("[AuraBenchmark] Finished 600 static HTML frames. Starting hover scenario (10s)...");
                    phase = Phase.BENCHMARK_HTML_PAUSE_HOVER;
                    hoverFrameCount = 0;
                }
            }
        } else if (phase == Phase.BENCHMARK_HTML_PAUSE_HOVER) {
            if (client.currentScreen instanceof HtmlPauseScreen screen) {
                htmlHoverFrameTimes.add(dtMs);
                hoverFrameCount++;

                // Simulate continuous mouse movement across the buttons
                int fbW = client.getWindow().getFramebufferWidth();
                int fbH = client.getWindow().getFramebufferHeight();
                double scale = client.getWindow().getScaleFactor();
                double mx = (fbW / 2.0) / scale;
                double my = ((fbH / 2.0) + Math.sin(hoverFrameCount * 0.1) * 150.0) / scale;
                screen.mouseMoved(mx, my);

                if (hoverFrameCount >= 600) { // ~10s at 60 FPS
                    System.out.println("[AuraBenchmark] Finished hover scenario. Measuring warm open timing...");
                    client.setScreen(null);
                    long t1 = System.currentTimeMillis();
                    client.setScreen(new HtmlPauseScreen());
                    secondOpenTimeMs = Math.max(1, System.currentTimeMillis() - t1);

                    System.out.println("[AuraBenchmark] Waiting for 60s total pause duration...");
                    phase = Phase.WAIT_60S_MEMORY;
                }
            }
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

                // Crop 17px primary button text "Вернуться к игре"
                // Screen width 1920, center 960. Panel 440px wide -> x=740..1180.
                // Button "Вернуться к игре" is primary button: height 54px, y ~ 420..474.
                // Text is centered: x ~ 860..1060, y ~ 432..462.
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
                        // Scale 400% Nearest Neighbor: 200x30 -> 800x120
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

            // 4. Compute statistics
            Stats regStats = computeStats(regularFrameTimes);
            Stats htmlStats = computeStats(htmlStaticFrameTimes);
            Stats hoverStats = computeStats(htmlHoverFrameTimes);

            // 5. Write summary
            File summaryFile = new File(perfDir, "benchmark_summary.txt");
            StringBuilder sb = new StringBuilder();
            sb.append("=== AURA ULTRALIGHT IN-GAME BENCHMARK RESULTS ===\n");
            sb.append("GL_RENDERER: ").append(GL11.glGetString(GL11.GL_RENDERER)).append("\n");
            sb.append("GL_VENDOR: ").append(GL11.glGetString(GL11.GL_VENDOR)).append("\n");
            sb.append("GL_VERSION: ").append(GL11.glGetString(GL11.GL_VERSION)).append("\n");
            sb.append(String.format(Locale.ROOT, "Private Bytes Before: %.2f MB\n", memBeforeBytes / (1024.0 * 1024.0)));
            sb.append(String.format(Locale.ROOT, "Private Bytes After 60s: %.2f MB\n", memAfterBytes / (1024.0 * 1024.0)));
            sb.append(String.format(Locale.ROOT, "Private Bytes Delta: +%.2f MB\n", (memAfterBytes - memBeforeBytes) / (1024.0 * 1024.0)));
            sb.append("--- REGULAR PAUSE (600 frames) ---\n");
            sb.append(String.format(Locale.ROOT, "Avg FPS: %.2f (%.2f ms)\n", regStats.avgFps, regStats.avgMs));
            sb.append(String.format(Locale.ROOT, "1%% Low FPS (P99): %.2f (%.2f ms)\n", regStats.lowFps, regStats.p99Ms));
            sb.append("--- HTML PAUSE STATIC (600 frames) ---\n");
            sb.append(String.format(Locale.ROOT, "Avg FPS: %.2f (%.2f ms)\n", htmlStats.avgFps, htmlStats.avgMs));
            sb.append(String.format(Locale.ROOT, "1%% Low FPS (P99): %.2f (%.2f ms)\n", htmlStats.lowFps, htmlStats.p99Ms));
            double deltaFps = ((htmlStats.avgFps - regStats.avgFps) / regStats.avgFps) * 100.0;
            sb.append(String.format(Locale.ROOT, "Static FPS Delta: %.2f%%\n", deltaFps));
            sb.append("--- HTML PAUSE HOVER (10s) ---\n");
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

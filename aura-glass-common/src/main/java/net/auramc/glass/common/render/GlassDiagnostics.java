package net.auramc.glass.common.render;

import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import static org.lwjgl.opengl.GL33C.*;

public final class GlassDiagnostics {
    private static final Set<String> REASONS = new HashSet<>();
    private static final GpuGlassTimers TIMERS = new GpuGlassTimers();
    private static boolean overlay, panel, captured;
    private static int glassElements, fallbackElements, fps, samples, framebufferW, framebufferH, windowW, windowH;
    private static double guiScale, frameMs;
    private static int sampledFbo = -1, sampledW, sampledH;
    private static long lastFrame;
    private static String vendor = "not sampled", renderer = "not sampled", glVersion = "not sampled";
    private static String screenName = "none", minecraftVersion = "unknown", iris = "not sampled";
    private static String status = "NOT_INIT", reason = "not initialized";
    private static String rendererError;
    private static String lastFallbackReason;

    private GlassDiagnostics() {}
    public static void toggleOverlay() { overlay = !overlay; }
    public static void togglePanel() { panel = !panel; }
    public static boolean overlayEnabled() { return overlay; }
    public static boolean panelEnabled() { return panel; }
    public static GpuGlassTimers timers() { return TIMERS; }

    public static void beginFrame(Object screen, String version) {
        captured = false;
        glassElements = fallbackElements = 0;
        lastFallbackReason = null;
        screenName = screen == null ? "in game" : screen.getClass().getSimpleName();
        minecraftVersion = version;
        TIMERS.beginFrame(overlay || panel);
        if (!overlay && !panel) { lastFrame = 0; return; }
        long now = System.nanoTime();
        if (lastFrame != 0) {
            double dt = (now - lastFrame) / 1_000_000.0;
            frameMs = frameMs == 0 ? dt : frameMs * .9 + dt * .1;
        }
        lastFrame = now;
        if (vendor.equals("not sampled")) {
            vendor = String.valueOf(glGetString(GL_VENDOR));
            renderer = String.valueOf(glGetString(GL_RENDERER));
            glVersion = String.valueOf(glGetString(GL_VERSION));
        }
    }

    public static void context(int mainFbo, int framebufferWidth, int framebufferHeight,
                               int windowWidth, int windowHeight, double scale, int currentFps,
                               boolean irisLoaded, String shaderpackState) {
        if (!overlay && !panel) return;
        framebufferW = framebufferWidth; framebufferH = framebufferHeight;
        windowW = windowWidth; windowH = windowHeight; guiScale = scale; fps = currentFps;
        iris = "installed=" + irisLoaded + ", shaderpack=" + shaderpackState;
        if (sampledFbo != mainFbo || sampledW != framebufferWidth || sampledH != framebufferHeight) {
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, mainFbo);
            samples = glGetInteger(GL_SAMPLES);
            sampledFbo = mainFbo; sampledW = framebufferWidth; sampledH = framebufferHeight;
        }
    }

    public static void endFrame() {
        BlurService blur = BlurService.get();
        if (rendererError != null) {
            status = "FAILED";
            reason = rendererError;
        } else if (blur.failedForScreen()) {
            reason = blur.failureReason();
            status = reason.contains("MSAA") || reason.contains("multisampled") ? "FALLBACK" : "FAILED";
        } else if (fallbackElements > 0) {
            status = "FALLBACK";
            reason = lastFallbackReason;
        } else if (captured || blur.isInitialized()) {
            status = "READY";
            reason = glassElements == 0 ? "no glass draw requested on this frame" : "none";
        } else {
            status = "NOT_INIT";
            reason = "no glass capture requested";
        }
    }

    public static void rendererFailure(String message) {
        rendererError = message;
        log(message);
    }
    public static void fallback(String message) { fallbackElements++; lastFallbackReason = message; log(message); }
    public static void glassDrawn() { glassElements++; }
    public static void captureDone() { captured = true; }
    public static void log(String message) {
        if (message != null && REASONS.add(message)) System.err.println("[AuraGlass] " + message);
    }
    public static void drainErrors(String operation) {
        for (int i = 0; i < 16; i++) {
            int error = glGetError();
            if (error == GL_NO_ERROR) break;
            log(operation + " GL error 0x" + Integer.toHexString(error) + " (" + error + ")");
        }
    }

    public static List<String> lines() {
        List<String> lines = new ArrayList<>();
        lines.add("AuraGlass | MC " + minecraftVersion + " | " + screenName);
        lines.add("GL vendor: " + vendor);
        lines.add("GL renderer: " + renderer);
        lines.add("GL version: " + glVersion);
        lines.add(String.format(Locale.ROOT, "Main FBO samples=%d | framebuffer=%dx%d | window=%dx%d | GUI=%.2f",
                samples, framebufferW, framebufferH, windowW, windowH, guiScale));
        lines.add("Iris: " + iris);
        lines.add("BlurService: " + status + " | failedForScreen=" + BlurService.get().failedForScreen());
        String flat = reason == null ? "none" : reason.replace('\n', ' ').replace('\r', ' ');
        for (int offset = 0; offset < flat.length(); offset += 100) lines.add((offset == 0 ? "Reason: " : "        ") + flat.substring(offset, Math.min(flat.length(), offset + 100)));
        lines.add("Quality: " + GlassRenderer.effectiveQuality() + " | glass draw calls=" + GlassRenderer.drawCalls());
        lines.add("Last frame: capture=" + captured + " | blur-path=" + glassElements + " | fallback=" + fallbackElements);
        if (TIMERS.unavailableReason() != null) lines.add(TIMERS.unavailableReason());
        lines.add("GPU delayed >=3f: capture=" + time(GpuGlassTimers.CAPTURE) + " blur=" + time(GpuGlassTimers.BLUR) + " glass=" + time(GpuGlassTimers.GLASS));
        lines.add(String.format(Locale.ROOT, "FPS=%d | frame time=%.3f ms | F6 panel=%s", fps, frameMs, panel));
        return lines;
    }

    private static String time(int channel) {
        if (!captured && channel != GpuGlassTimers.GLASS) return "0.000ms (not used)";
        if (channel == GpuGlassTimers.GLASS && glassElements + fallbackElements == 0) return "0.000ms (not used)";
        double value = TIMERS.milliseconds(channel);
        return value < 0 ? "pending" : String.format(Locale.ROOT, "%.3fms (age %df)", value, TIMERS.measuredAge(channel));
    }

    public static void shutdown() { TIMERS.close(); }
}

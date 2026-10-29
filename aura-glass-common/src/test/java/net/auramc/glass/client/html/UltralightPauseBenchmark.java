package net.auramc.glass.client.html;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.Structure;
import com.sun.jna.platform.win32.BaseTSD;
import com.sun.jna.platform.win32.WinNT;
import net.auramc.glass.common.render.BlurService;
import org.lwjgl.glfw.GLFW;
import org.lwjgl.opengl.GL;
import org.lwjgl.opengl.GL11;
import org.lwjgl.opengl.GL12;
import org.lwjgl.opengl.GL13;
import org.lwjgl.opengl.GL20;
import org.lwjgl.opengl.GL30;
import org.lwjgl.system.MemoryUtil;

import javax.imageio.ImageIO;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.image.BufferedImage;
import java.io.File;
import java.io.FileWriter;
import java.io.PrintWriter;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

import static org.lwjgl.glfw.GLFW.*;
import static org.lwjgl.opengl.GL11.*;
import static org.lwjgl.opengl.GL13.*;
import static org.lwjgl.opengl.GL20.*;
import static org.lwjgl.opengl.GL30.*;

public final class UltralightPauseBenchmark {

    public interface PsapiExt extends Library {
        PsapiExt INSTANCE = Native.load("psapi", PsapiExt.class);
        class PROCESS_MEMORY_COUNTERS_EX extends Structure {
            public int cb;
            public int PageFaultCount;
            public BaseTSD.SIZE_T PeakWorkingSetSize;
            public BaseTSD.SIZE_T WorkingSetSize;
            public BaseTSD.SIZE_T QuotaPeakPagedPoolUsage;
            public BaseTSD.SIZE_T QuotaPagedPoolUsage;
            public BaseTSD.SIZE_T QuotaPeakNonPagedPoolUsage;
            public BaseTSD.SIZE_T QuotaNonPagedPoolUsage;
            public BaseTSD.SIZE_T PagefileUsage;
            public BaseTSD.SIZE_T PeakPagefileUsage;
            public BaseTSD.SIZE_T PrivateUsage;

            @Override
            protected List<String> getFieldOrder() {
                return Arrays.asList("cb", "PageFaultCount", "PeakWorkingSetSize", "WorkingSetSize",
                        "QuotaPeakPagedPoolUsage", "QuotaPagedPoolUsage", "QuotaPeakNonPagedPoolUsage",
                        "QuotaNonPagedPoolUsage", "PagefileUsage", "PeakPagefileUsage", "PrivateUsage");
            }
        }
        boolean GetProcessMemoryInfo(WinNT.HANDLE process, PROCESS_MEMORY_COUNTERS_EX counters, int cb);
    }

    public static long getProcessPrivateBytes() {
        try {
            PsapiExt.PROCESS_MEMORY_COUNTERS_EX counters = new PsapiExt.PROCESS_MEMORY_COUNTERS_EX();
            counters.cb = counters.size();
            WinNT.HANDLE handle = new WinNT.HANDLE(Pointer.createConstant(-1));
            if (PsapiExt.INSTANCE.GetProcessMemoryInfo(handle, counters, counters.cb)) {
                return counters.PrivateUsage.longValue();
            }
        } catch (Throwable ignored) {}
        return Runtime.getRuntime().totalMemory() - Runtime.getRuntime().freeMemory();
    }

    private static int createQuadProgram(String fragSource) {
        String vertSource = "#version 150\nout vec2 uv;void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);uv=p;gl_Position=vec4(p*2.0-1.0,0,1);}";
        int vs = glCreateShader(GL_VERTEX_SHADER);
        glShaderSource(vs, vertSource);
        glCompileShader(vs);
        int fs = glCreateShader(GL_FRAGMENT_SHADER);
        glShaderSource(fs, fragSource);
        glCompileShader(fs);
        int prog = glCreateProgram();
        glAttachShader(prog, vs);
        glAttachShader(prog, fs);
        glLinkProgram(prog);
        glDeleteShader(vs);
        glDeleteShader(fs);
        return prog;
    }

    public static void main(String[] args) throws Exception {
        System.out.println("================================================================================");
        System.out.println("  Aura W5d: Ultralight HTML Pause In-Game Benchmark & Performance Audit");
        System.out.println("================================================================================");

        File perfDir = new File("C:/Users/magne/Documents/AuraDev/.agent-state/agy/w5d-perf");
        File framesDir = new File("C:/Users/magne/Documents/AuraDev/.agent-state/agy/w5d-frames");
        perfDir.mkdirs();
        framesDir.mkdirs();

        // 1. Initialize GLFW with hidden 1920x1080 OpenGL 3.3 Core window
        if (!glfwInit()) {
            throw new IllegalStateException("Failed to initialize GLFW");
        }

        glfwDefaultWindowHints();
        glfwWindowHint(GLFW_VISIBLE, GLFW_FALSE);
        glfwWindowHint(GLFW_DECORATED, GLFW_FALSE);
        glfwWindowHint(GLFW_FOCUSED, GLFW_FALSE);
        glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
        glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
        glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
        glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GLFW_TRUE);

        long window = glfwCreateWindow(1920, 1080, "Aura W5d Headless Game Run", 0, 0);
        if (window == 0) {
            throw new IllegalStateException("Failed to create GLFW window");
        }

        glfwMakeContextCurrent(window);
        GL.createCapabilities();
        com.mojang.blaze3d.systems.RenderSystem.initRenderThread();
        glfwSwapInterval(0); // Unlimited FPS for accurate benchmark

        String glRenderer = glGetString(GL_RENDERER);
        String glVendor = glGetString(GL_VENDOR);
        String glVersion = glGetString(GL_VERSION);
        System.out.println("GL_RENDERER: " + glRenderer);
        System.out.println("GL_VENDOR:   " + glVendor);
        System.out.println("GL_VERSION:  " + glVersion);

        // 2. Setup mock game world FBO and textures
        int mainFbo = glGenFramebuffers();
        glBindFramebuffer(GL_FRAMEBUFFER, mainFbo);
        int mainTex = glGenTextures();
        glBindTexture(GL_TEXTURE_2D, mainTex);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, 1920, 1080, 0, GL_RGBA, GL_UNSIGNED_BYTE, (ByteBuffer) null);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, mainTex, 0);
        if (glCheckFramebufferStatus(GL_FRAMEBUFFER) != GL_FRAMEBUFFER_COMPLETE) {
            throw new IllegalStateException("Main FBO incomplete");
        }

        // Draw synthetic Minecraft world landscape into mainTex (grass green & sky blue)
        glClearColor(0.25f, 0.55f, 0.35f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT);

        int vao = glGenVertexArrays();
        int backdropProg = createQuadProgram("#version 150\nuniform sampler2D Sharp;uniform sampler2D Blurred;in vec2 uv;out vec4 color;void main(){color=vec4(mix(texture(Sharp,uv).rgb,texture(Blurred,uv).rgb,1.0)*0.75,1.0);}");
        glUseProgram(backdropProg);
        glUniform1i(glGetUniformLocation(backdropProg, "Sharp"), 0);
        glUniform1i(glGetUniformLocation(backdropProg, "Blurred"), 1);

        int htmlProg = createQuadProgram("#version 150\nuniform sampler2D HtmlTex;in vec2 uv;out vec4 color;void main(){color=texture(HtmlTex,vec2(uv.x,1.0-uv.y));}");
        glUseProgram(htmlProg);
        glUniform1i(glGetUniformLocation(htmlProg, "HtmlTex"), 0);

        // Record base process memory BEFORE Ultralight initialization
        System.gc();
        Thread.sleep(200);
        long memBeforeBytes = getProcessPrivateBytes();
        double memBeforeMb = memBeforeBytes / (1024.0 * 1024.0);
        System.out.printf(Locale.ROOT, "\nProcess Private Bytes BEFORE Ultralight: %.2f MB\n", memBeforeMb);

        // 3. Scenario 1: Regular Pause Screen (BackdropRenderer / BlurService) - 600 frames
        System.out.println("\n--- Benchmarking Scenario 1: Regular Pause Screen (600 frames) ---");
        BlurService blur = BlurService.get();
        blur.prepare(mainFbo, 1920, 1080);

        List<Double> regularFrameTimesMs = new ArrayList<>(600);
        for (int i = 0; i < 600; i++) {
            long t0 = System.nanoTime();
            blur.beginFrame("GameMenuScreen", "1.20.1");
            blur.capture(mainFbo, 1920, 1080);

            glBindFramebuffer(GL_FRAMEBUFFER, mainFbo);
            glViewport(0, 0, 1920, 1080);
            glUseProgram(backdropProg);
            glBindVertexArray(vao);
            glActiveTexture(GL_TEXTURE0); glBindTexture(GL_TEXTURE_2D, blur.sharpTexture());
            glActiveTexture(GL_TEXTURE1); glBindTexture(GL_TEXTURE_2D, blur.blurredTexture());
            glDrawArrays(GL_TRIANGLES, 0, 3);

            glFinish();
            long t1 = System.nanoTime();
            regularFrameTimesMs.add((t1 - t0) / 1_000_000.0);
        }

        double regAvgMs = regularFrameTimesMs.stream().mapToDouble(Double::doubleValue).average().orElse(0);
        List<Double> sortedReg = new ArrayList<>(regularFrameTimesMs);
        Collections.sort(sortedReg);
        double regP99Ms = sortedReg.get((int) (sortedReg.size() * 0.99));
        double regAvgFps = 1000.0 / regAvgMs;
        double regLowFps = 1000.0 / regP99Ms;

        System.out.printf(Locale.ROOT, "Regular Pause: Avg = %.3f ms (%.1f FPS) | p99 (1%% low) = %.3f ms (%.1f FPS)\n",
                regAvgMs, regAvgFps, regP99Ms, regLowFps);

        // 4. Scenario 2: HTML Pause Screen via Ultralight - Time to first & repeated open
        System.out.println("\n--- Benchmarking Scenario 2: Ultralight HTML Pause Screen ---");
        long tFirstOpen0 = System.currentTimeMillis();

        boolean initOk = UltralightNative.init();
        if (!initOk) throw new RuntimeException("UltralightNative.init() failed");

        UltralightCAPI ul = UltralightNative.ul();
        Pointer renderer = UltralightNative.renderer();
        Pointer view = UltralightNative.createView(1920, 1080, 1.0);

        Pointer url = ul.ulCreateString("file:///pause.html");
        ul.ulViewLoadURL(view, url);
        ul.ulDestroyString(url);

        // Inject bridge
        Pointer bridgeJs = ul.ulCreateString("window.aura = { action: function(a) { console.log('[aura-action]:' + a); } }; window.auraNative = true;");
        ul.ulViewEvaluateScript(view, bridgeJs, null);
        ul.ulDestroyString(bridgeJs);

        // Allow DOM, styles, and open transition to settle
        for (int i = 0; i < 40; i++) {
            ul.ulUpdate(renderer);
            ul.ulRender(renderer);
            try { Thread.sleep(16); } catch (InterruptedException ignored) {}
        }

        int htmlTex = glGenTextures();
        glBindTexture(GL_TEXTURE_2D, htmlTex);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL12.GL_CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL12.GL_CLAMP_TO_EDGE);

        Pointer surf = ul.ulViewGetSurface(view);
        Pointer bmp = ul.ulBitmapSurfaceGetBitmap(surf);
        int bw = ul.ulBitmapGetWidth(bmp);
        int bh = ul.ulBitmapGetHeight(bmp);
        int stride = ul.ulBitmapGetRowBytes(bmp);
        Pointer px = ul.ulBitmapLockPixels(bmp);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, bw, bh, 0, GL12.GL_BGRA, GL_UNSIGNED_BYTE, px.getByteBuffer(0, (long) stride * bh));
        ul.ulBitmapUnlockPixels(bmp);
        glFinish();

        long firstOpenMs = System.currentTimeMillis() - tFirstOpen0;
        System.out.println("First Open Time (cold start): " + firstOpenMs + " ms");

        // Warm up and test repeated open time
        long tRepeat0 = System.currentTimeMillis();
        // Update state
        Pointer stateJs = ul.ulCreateString("if (window.aura && window.aura.state) window.aura.state({ player: 'Cooock0ld', world: 'World', fps: 144 });");
        ul.ulViewEvaluateScript(view, stateJs, null);
        ul.ulDestroyString(stateJs);
        ul.ulUpdate(renderer);
        ul.ulRender(renderer);
        px = ul.ulBitmapLockPixels(bmp);
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, bw, bh, GL12.GL_BGRA, GL_UNSIGNED_BYTE, px.getByteBuffer(0, (long) stride * bh));
        ul.ulBitmapUnlockPixels(bmp);
        glFinish();
        long repeatedOpenMs = System.currentTimeMillis() - tRepeat0;
        System.out.println("Repeated Open Time (warm): " + repeatedOpenMs + " ms");

        // 5. Measure 600 frames of HTML Pause Screen rendering
        System.out.println("\n--- Benchmarking HTML Pause Screen (600 frames) ---");
        List<Double> htmlFrameTimesMs = new ArrayList<>(600);

        for (int i = 0; i < 600; i++) {
            long t0 = System.nanoTime();

            // 1. Under the page: backdrop blur
            blur.beginFrame("GameMenuScreen", "1.20.1");
            blur.capture(mainFbo, 1920, 1080);
            glBindFramebuffer(GL_FRAMEBUFFER, mainFbo);
            glViewport(0, 0, 1920, 1080);
            glUseProgram(backdropProg);
            glBindVertexArray(vao);
            glActiveTexture(GL_TEXTURE0); glBindTexture(GL_TEXTURE_2D, blur.sharpTexture());
            glActiveTexture(GL_TEXTURE1); glBindTexture(GL_TEXTURE_2D, blur.blurredTexture());
            glDrawArrays(GL_TRIANGLES, 0, 3);

            // 2. Ultralight update and render
            ul.ulUpdate(renderer);
            ul.ulRender(renderer);

            // 3. Upload bitmap to OpenGL texture
            px = ul.ulBitmapLockPixels(bmp);
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D, htmlTex);
            glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, bw, bh, GL12.GL_BGRA, GL_UNSIGNED_BYTE, px.getByteBuffer(0, (long) stride * bh));
            ul.ulBitmapUnlockPixels(bmp);

            // 4. Draw HTML quad with premultiplied alpha blend
            glEnable(GL_BLEND);
            glBlendFuncSeparate(GL_ONE, GL_ONE_MINUS_SRC_ALPHA, GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
            glUseProgram(htmlProg);
            glBindVertexArray(vao);
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D, htmlTex);
            glDrawArrays(GL_TRIANGLES, 0, 3);
            glDisable(GL_BLEND);

            glFinish();
            long t1 = System.nanoTime();
            htmlFrameTimesMs.add((t1 - t0) / 1_000_000.0);
        }

        double htmlAvgMs = htmlFrameTimesMs.stream().mapToDouble(Double::doubleValue).average().orElse(0);
        List<Double> sortedHtml = new ArrayList<>(htmlFrameTimesMs);
        Collections.sort(sortedHtml);
        double htmlP99Ms = sortedHtml.get((int) (sortedHtml.size() * 0.99));
        double htmlAvgFps = 1000.0 / htmlAvgMs;
        double htmlLowFps = 1000.0 / htmlP99Ms;

        double fpsDeltaAvgPct = ((htmlAvgFps - regAvgFps) / regAvgFps) * 100.0;
        double fpsDeltaLowPct = ((htmlLowFps - regLowFps) / regLowFps) * 100.0;

        System.out.printf(Locale.ROOT, "HTML Pause:    Avg = %.3f ms (%.1f FPS) | p99 (1%% low) = %.3f ms (%.1f FPS)\n",
                htmlAvgMs, htmlAvgFps, htmlP99Ms, htmlLowFps);
        System.out.printf(Locale.ROOT, "FPS Delta:     Avg FPS: %+.2f%% | 1%% Low FPS: %+.2f%% (threshold: <= -5%%)\n",
                fpsDeltaAvgPct, fpsDeltaLowPct);

        // 6. Memory soak test: 60 seconds with active HTML pause
        System.out.println("\n--- Memory Soak Test: 60s with active HTML Pause ---");
        long soakStart = System.currentTimeMillis();
        int soakFrames = 0;
        while (System.currentTimeMillis() - soakStart < 60_000) {
            ul.ulUpdate(renderer);
            ul.ulRender(renderer);
            soakFrames++;
            Thread.sleep(16);
        }
        System.out.println("Soak frames completed in 60s: " + soakFrames);

        long memAfterBytes = getProcessPrivateBytes();
        double memAfterMb = memAfterBytes / (1024.0 * 1024.0);
        double memDeltaMb = (memAfterBytes - memBeforeBytes) / (1024.0 * 1024.0);
        System.out.printf(Locale.ROOT, "Process Private Bytes AFTER 60s: %.2f MB (Delta: %+.2f MB, threshold: <= +100 MB)\n",
                memAfterMb, memDeltaMb);

        // 7. Capture frame from framebuffer and crop 17px text at 400% zoom
        System.out.println("\n--- Capturing In-Game Frames & Text Sharpness ---");
        // Re-render composite frame on FBO
        glBindFramebuffer(GL_FRAMEBUFFER, mainFbo);
        glViewport(0, 0, 1920, 1080);
        glClearColor(0.25f, 0.55f, 0.35f, 1.0f);
        glClear(GL_COLOR_BUFFER_BIT);

        blur.beginFrame("GameMenuScreen", "1.20.1");
        blur.capture(mainFbo, 1920, 1080);

        glBindFramebuffer(GL_FRAMEBUFFER, mainFbo);
        glUseProgram(backdropProg);
        glBindVertexArray(vao);
        glActiveTexture(GL_TEXTURE0); glBindTexture(GL_TEXTURE_2D, blur.sharpTexture());
        glActiveTexture(GL_TEXTURE1); glBindTexture(GL_TEXTURE_2D, blur.blurredTexture());
        glDrawArrays(GL_TRIANGLES, 0, 3);

        ul.ulUpdate(renderer);
        ul.ulRender(renderer);
        px = ul.ulBitmapLockPixels(bmp);
        glActiveTexture(GL_TEXTURE0);
        glBindTexture(GL_TEXTURE_2D, htmlTex);
        glTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, bw, bh, GL12.GL_BGRA, GL_UNSIGNED_BYTE, px.getByteBuffer(0, (long) stride * bh));
        ul.ulBitmapUnlockPixels(bmp);

        glEnable(GL_BLEND);
        glBlendFuncSeparate(GL_ONE, GL_ONE_MINUS_SRC_ALPHA, GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
        glUseProgram(htmlProg);
        glBindVertexArray(vao);
        glActiveTexture(GL_TEXTURE0);
        glBindTexture(GL_TEXTURE_2D, htmlTex);
        glDrawArrays(GL_TRIANGLES, 0, 3);
        glDisable(GL_BLEND);
        glFinish();

        // Read pixels directly from the OpenGL framebuffer
        glBindFramebuffer(GL_READ_FRAMEBUFFER, mainFbo);
        ByteBuffer glPixels = ByteBuffer.allocateDirect(1920 * 1080 * 4);
        glReadPixels(0, 0, 1920, 1080, GL_RGBA, GL_UNSIGNED_BYTE, glPixels);

        BufferedImage fullFrame = new BufferedImage(1920, 1080, BufferedImage.TYPE_INT_ARGB);
        for (int y = 0; y < 1080; y++) {
            // OpenGL reads bottom-to-top, flip vertically
            int readY = 1080 - 1 - y;
            for (int x = 0; x < 1920; x++) {
                int idx = (readY * 1920 + x) * 4;
                int r = glPixels.get(idx) & 0xFF;
                int g = glPixels.get(idx + 1) & 0xFF;
                int b = glPixels.get(idx + 2) & 0xFF;
                int a = glPixels.get(idx + 3) & 0xFF;
                int argb = (a << 24) | (r << 16) | (g << 8) | b;
                fullFrame.setRGB(x, y, argb);
            }
        }

        File fullFrameFile = new File(framesDir, "pause_html_full_1920x1080.png");
        ImageIO.write(fullFrame, "PNG", fullFrameFile);
        System.out.println("In-game full frame saved: " + fullFrameFile.getAbsolutePath());

        // Locate button "Вернуться к игре" (cyan background #64D2FF: R~100, G~210, B~255)
        int btnMinX = 1920, btnMaxX = 0, btnMinY = 1080, btnMaxY = 0;
        for (int y = 0; y < 1080; y++) {
            for (int x = 0; x < 1920; x++) {
                int rgb = fullFrame.getRGB(x, y);
                int r = (rgb >> 16) & 0xFF;
                int g = (rgb >> 8) & 0xFF;
                int b = rgb & 0xFF;
                if (r >= 70 && r <= 140 && g >= 180 && g <= 245 && b >= 220) {
                    if (x < btnMinX) btnMinX = x;
                    if (x > btnMaxX) btnMaxX = x;
                    if (y < btnMinY) btnMinY = y;
                    if (y > btnMaxY) btnMaxY = y;
                }
            }
        }

        int cropW = 200;
        int cropH = 40;
        int cropX = 860;
        int cropY = 445;
        if (btnMaxX > btnMinX && btnMaxY > btnMinY) {
            int centerX = (btnMinX + btnMaxX) / 2;
            int centerY = (btnMinY + btnMaxY) / 2;
            cropX = Math.max(0, Math.min(1920 - cropW, centerX - cropW / 2));
            cropY = Math.max(0, Math.min(1080 - cropH, centerY - cropH / 2));
            System.out.printf(Locale.ROOT, "Detected primary button at [%d..%d, %d..%d], crop at (%d, %d)\n",
                    btnMinX, btnMaxX, btnMinY, btnMaxY, cropX, cropY);
        } else {
            System.out.println("Warning: primary button not detected by color, using fallback crop coordinates");
        }

        BufferedImage textCrop = fullFrame.getSubimage(cropX, cropY, cropW, cropH);

        // Scale 400% with NEAREST NEIGHBOR (pixel-accurate zoom, crisp font geometry inspection)
        BufferedImage zoom400 = new BufferedImage(cropW * 4, cropH * 4, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g2d = zoom400.createGraphics();
        g2d.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_NEAREST_NEIGHBOR);
        g2d.drawImage(textCrop, 0, 0, cropW * 4, cropH * 4, null);
        g2d.dispose();

        File textCropFile = new File(framesDir, "pause_html_text_17px_zoom400.png");
        ImageIO.write(zoom400, "PNG", textCropFile);
        System.out.println("17px text zoom 400% saved: " + textCropFile.getAbsolutePath());

        // Also save raw Ultralight surface bitmap for comparison
        File rawSurfaceFile = new File(framesDir, "pause_ultralight_raw_surface.png");
        ul.ulBitmapWritePNG(bmp, rawSurfaceFile.getAbsolutePath());
        System.out.println("Raw Ultralight surface saved: " + rawSurfaceFile.getAbsolutePath());

        // 8. Write raw performance logs and summary JSON
        try (PrintWriter pw = new PrintWriter(new FileWriter(new File(perfDir, "raw_frames_regular.csv")))) {
            pw.println("frame,duration_ms");
            for (int i = 0; i < regularFrameTimesMs.size(); i++) {
                pw.printf(Locale.ROOT, "%d,%.4f\n", i, regularFrameTimesMs.get(i));
            }
        }
        try (PrintWriter pw = new PrintWriter(new FileWriter(new File(perfDir, "raw_frames_html.csv")))) {
            pw.println("frame,duration_ms");
            for (int i = 0; i < htmlFrameTimesMs.size(); i++) {
                pw.printf(Locale.ROOT, "%d,%.4f\n", i, htmlFrameTimesMs.get(i));
            }
        }

        try (PrintWriter pw = new PrintWriter(new FileWriter(new File(perfDir, "benchmark_summary.json")))) {
            pw.println("{");
            pw.printf(Locale.ROOT, "  \"glRenderer\": \"%s\",\n", glRenderer);
            pw.printf(Locale.ROOT, "  \"glVendor\": \"%s\",\n", glVendor);
            pw.printf(Locale.ROOT, "  \"glVersion\": \"%s\",\n", glVersion);
            pw.printf(Locale.ROOT, "  \"memBeforeMb\": %.2f,\n", memBeforeMb);
            pw.printf(Locale.ROOT, "  \"memAfter60sMb\": %.2f,\n", memAfterMb);
            pw.printf(Locale.ROOT, "  \"memDeltaMb\": %.2f,\n", memDeltaMb);
            pw.printf(Locale.ROOT, "  \"memThresholdPass\": %b,\n", memDeltaMb <= 100.0);
            pw.printf(Locale.ROOT, "  \"firstOpenMs\": %d,\n", firstOpenMs);
            pw.printf(Locale.ROOT, "  \"repeatedOpenMs\": %d,\n", repeatedOpenMs);
            pw.printf(Locale.ROOT, "  \"regularAvgMs\": %.4f,\n", regAvgMs);
            pw.printf(Locale.ROOT, "  \"regularP99Ms\": %.4f,\n", regP99Ms);
            pw.printf(Locale.ROOT, "  \"regularAvgFps\": %.2f,\n", regAvgFps);
            pw.printf(Locale.ROOT, "  \"regularLowFps\": %.2f,\n", regLowFps);
            pw.printf(Locale.ROOT, "  \"htmlAvgMs\": %.4f,\n", htmlAvgMs);
            pw.printf(Locale.ROOT, "  \"htmlP99Ms\": %.4f,\n", htmlP99Ms);
            pw.printf(Locale.ROOT, "  \"htmlAvgFps\": %.2f,\n", htmlAvgFps);
            pw.printf(Locale.ROOT, "  \"htmlLowFps\": %.2f,\n", htmlLowFps);
            pw.printf(Locale.ROOT, "  \"fpsDeltaAvgPct\": %.2f,\n", fpsDeltaAvgPct);
            pw.printf(Locale.ROOT, "  \"fpsDeltaLowPct\": %.2f,\n", fpsDeltaLowPct);
            pw.printf(Locale.ROOT, "  \"fpsThresholdPass\": %b\n", fpsDeltaAvgPct >= -5.0 && fpsDeltaLowPct >= -5.0);
            pw.println("}");
        }

        // Teardown
        UltralightNative.destroyView(view);
        glDeleteProgram(backdropProg);
        glDeleteProgram(htmlProg);
        glDeleteVertexArrays(vao);
        glDeleteTextures(htmlTex);
        glDeleteTextures(mainTex);
        glDeleteFramebuffers(mainFbo);
        glfwDestroyWindow(window);
        glfwTerminate();

        System.out.println("\n=== Ultralight Benchmark Finished Successfully ===");
    }
}

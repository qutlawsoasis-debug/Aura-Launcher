package net.auramc.glass.client.html;

import com.sun.jna.Pointer;

import java.io.File;
import java.util.concurrent.atomic.AtomicReference;

public final class UltralightSmokeTest {
    public static void main(String[] args) {
        System.out.println("=== Testing Ultralight Native Pipeline ===");

        long memBefore = getProcessMemoryBytes();
        long t0 = System.currentTimeMillis();

        boolean ok = UltralightNative.init();
        if (!ok) {
            throw new RuntimeException("UltralightNative.init() failed");
        }
        long initTime = System.currentTimeMillis() - t0;
        System.out.println("Ultralight initialized in " + initTime + " ms");

        UltralightCAPI ul = UltralightNative.ul();
        Pointer renderer = UltralightNative.renderer();

        long tView0 = System.currentTimeMillis();
        Pointer view = UltralightNative.createView(1920, 1080, 1.0);
        if (view == null) {
            throw new RuntimeException("createView(1920, 1080) returned null");
        }
        long viewCreateTime = System.currentTimeMillis() - tView0;
        System.out.println("View created in " + viewCreateTime + " ms");

        AtomicReference<String> lastAction = new AtomicReference<>();
        ul.ulViewSetAddConsoleMessageCallback(view, (userData, caller, source, level, message, line, col, sourceId) -> {
            String msg = ul.ulStringGetData(message);
            if (msg != null && msg.startsWith("[aura-action]:")) {
                lastAction.set(msg.substring("[aura-action]:".length()));
                System.out.println("Intercepted action: " + lastAction.get());
            }
        }, null);

        Pointer url = ul.ulCreateString("file:///pause.html");
        ul.ulViewLoadURL(view, url);
        ul.ulDestroyString(url);

        // Run 40 update/render steps to allow DOM & styles & fonts to settle
        for (int i = 0; i < 40; i++) {
            ul.ulUpdate(renderer);
            ul.ulRender(renderer);
            try { Thread.sleep(16); } catch (InterruptedException ignored) {}
        }

        Pointer surface = ul.ulViewGetSurface(view);
        if (surface == null) throw new RuntimeException("ulViewGetSurface returned null");

        Pointer bitmap = ul.ulBitmapSurfaceGetBitmap(surface);
        if (bitmap == null) throw new RuntimeException("ulBitmapSurfaceGetBitmap returned null");

        int w = ul.ulBitmapGetWidth(bitmap);
        int h = ul.ulBitmapGetHeight(bitmap);
        int stride = ul.ulBitmapGetRowBytes(bitmap);
        long size = ul.ulBitmapGetSize(bitmap);

        System.out.println("Bitmap dimensions: " + w + "x" + h + ", stride=" + stride + ", total bytes=" + size);
        if (w != 1920 || h != 1080 || stride < 1920 * 4) {
            throw new RuntimeException("Unexpected bitmap geometry: " + w + "x" + h + " stride=" + stride);
        }

        Pointer pixels = ul.ulBitmapLockPixels(bitmap);
        if (pixels == null) throw new RuntimeException("ulBitmapLockPixels returned null");

        // Inspect non-zero pixels (HTML content rendered onto transparent surface)
        byte[] sample = pixels.getByteArray(0, Math.min((int) size, 100_000));
        int nonZero = 0;
        for (byte b : sample) if (b != 0) nonZero++;
        System.out.println("Sample rendered pixels (first 100k bytes non-zero): " + nonZero);

        ul.ulBitmapUnlockPixels(bitmap);

        // Test JS injection & action callback
        Pointer js = ul.ulCreateString("window.aura = { action: function(a) { console.log('[aura-action]:' + a); } }; window.aura.action('resume');");
        ul.ulViewEvaluateScript(view, js, null);
        ul.ulDestroyString(js);

        ul.ulUpdate(renderer);
        if (!"resume".equals(lastAction.get())) {
            throw new RuntimeException("Expected action 'resume', but got: " + lastAction.get());
        }
        System.out.println("JS -> Java bridge verified successfully!");

        // Write test PNG
        File outPng = new File("build/pause_rendered.png");
        outPng.getParentFile().mkdirs();
        boolean pngOk = ul.ulBitmapWritePNG(bitmap, outPng.getAbsolutePath());
        System.out.println("PNG saved: " + pngOk + " -> " + outPng.getAbsolutePath() + " (" + outPng.length() + " bytes)");

        UltralightNative.destroyView(view);

        long memAfter = getProcessMemoryBytes();
        long memDeltaMb = (memAfter - memBefore) / (1024 * 1024);
        System.out.println("Process Memory before: " + (memBefore / 1024 / 1024) + " MB, after: " + (memAfter / 1024 / 1024) + " MB, delta: " + memDeltaMb + " MB");

        System.out.println("=== Ultralight Smoke Test PASSED ===");
    }

    private static long getProcessMemoryBytes() {
        return Runtime.getRuntime().totalMemory() - Runtime.getRuntime().freeMemory();
    }
}

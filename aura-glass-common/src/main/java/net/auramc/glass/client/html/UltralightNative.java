package net.auramc.glass.client.html;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;

import java.io.File;

public final class UltralightNative {
    private interface WinKernel32 extends Library {
        WinKernel32 INSTANCE = Native.load("kernel32", WinKernel32.class);
        boolean SetDllDirectoryA(String path);
    }
    private static volatile boolean initialized = false;
    private static UltralightCAPI ul;
    private static AppCoreCAPI app;
    private static Pointer renderer;
    private static File activeSdkDir;

    public static synchronized boolean isAvailable() {
        if (initialized) return renderer != null;
        return init();
    }

    public static synchronized boolean init() {
        if (initialized) return renderer != null;
        try {
            File sdkDir = findSdkDir();
            if (sdkDir == null || !sdkDir.exists()) {
                System.err.println("[Aura] Ultralight SDK directory not found");
                return false;
            }
            activeSdkDir = sdkDir;
            File binDir = new File(sdkDir, "bin");
            File resDir = new File(sdkDir, "resources");
            if (!binDir.exists() || !resDir.exists()) {
                System.err.println("[Aura] Ultralight bin or resources directory missing in " + sdkDir);
                return false;
            }

            // Set DLL search directory
            WinKernel32.INSTANCE.SetDllDirectoryA(binDir.getAbsolutePath());

            // Load dependencies in exact order
            System.load(new File(binDir, "WebCore.dll").getAbsolutePath());
            System.load(new File(binDir, "UltralightCore.dll").getAbsolutePath());
            System.load(new File(binDir, "AppCore.dll").getAbsolutePath());
            System.load(new File(binDir, "Ultralight.dll").getAbsolutePath());

            app = Native.load(new File(binDir, "AppCore.dll").getAbsolutePath(), AppCoreCAPI.class);
            ul = Native.load(new File(binDir, "Ultralight.dll").getAbsolutePath(), UltralightCAPI.class);

            // Platform initialization
            app.ulEnablePlatformFontLoader();

            File htmlDir = findHtmlDir();
            String htmlPath = (htmlDir != null ? htmlDir.getAbsolutePath() : sdkDir.getAbsolutePath()) + File.separator;
            Pointer baseDirStr = ul.ulCreateString(htmlPath);
            app.ulEnablePlatformFileSystem(baseDirStr);
            ul.ulDestroyString(baseDirStr);

            Pointer config = ul.ulCreateConfig();
            String resPath = resDir.getAbsolutePath() + File.separator;
            Pointer resPrefixStr = ul.ulCreateString(resPath);
            ul.ulConfigSetResourcePathPrefix(config, resPrefixStr);
            ul.ulDestroyString(resPrefixStr);

            renderer = ul.ulCreateRenderer(config);
            ul.ulDestroyConfig(config);

            initialized = true;
            System.out.println("[Aura] Ultralight SDK 1.3 initialized successfully from " + sdkDir.getAbsolutePath());
            return true;
        } catch (Throwable t) {
            System.err.println("[Aura] Failed to initialize Ultralight: " + t.getMessage());
            t.printStackTrace();
            return false;
        }
    }

    public static File findSdkDir() {
        String custom = System.getProperty("aura.ultralight.path");
        if (custom != null && new File(custom).exists()) return new File(custom);

        String[] candidates = {
                "aura-ultralight",
                "../aura-glass/aura-ultralight",
                "aura-glass/aura-ultralight",
                "C:/Users/magne/Documents/AuraDev/.agent-state/agy/w5/aura-glass/aura-ultralight"
        };
        for (String c : candidates) {
            File f = new File(c);
            if (f.exists() && new File(f, "bin/Ultralight.dll").exists()) return f.getAbsoluteFile();
        }
        return null;
    }

    public static File findHtmlDir() {
        String custom = System.getProperty("aura.html.path");
        if (custom != null && new File(custom).exists()) return new File(custom);

        String[] candidates = {
                "C:/Users/magne/Documents/AuraDev/.agent-state/agy/html",
                "../../html",
                "../html",
                "html"
        };
        for (String c : candidates) {
            File f = new File(c);
            if (f.exists() && new File(f, "pause.html").exists()) return f.getAbsoluteFile();
        }
        return null;
    }

    public static UltralightCAPI ul() { return ul; }
    public static AppCoreCAPI app() { return app; }
    public static Pointer renderer() { return renderer; }

    public static Pointer createView(int width, int height, double scale) {
        if (!isAvailable()) return null;
        Pointer vc = ul.ulCreateViewConfig();
        ul.ulViewConfigSetIsAccelerated(vc, false);
        ul.ulViewConfigSetIsTransparent(vc, true);
        ul.ulViewConfigSetInitialDeviceScale(vc, scale);
        ul.ulViewConfigSetInitialFocus(vc, true);

        Pointer view = ul.ulCreateView(renderer, width, height, vc, null);
        ul.ulDestroyViewConfig(vc);
        return view;
    }

    public static void destroyView(Pointer view) {
        if (view != null && ul != null) {
            ul.ulDestroyView(view);
        }
    }

    public static void updateAndRender() {
        if (renderer != null && ul != null) {
            ul.ulUpdate(renderer);
            ul.ulRender(renderer);
        }
    }
}

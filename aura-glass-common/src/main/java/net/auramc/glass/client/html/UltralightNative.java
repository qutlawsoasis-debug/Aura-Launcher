package net.auramc.glass.client.html;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;

import java.io.File;
import java.util.Locale;

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
    private static UltralightCAPI.ULFileSystem.ByValue fileSystem;

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
            // Setup custom file system to guarantee font/woff2, html, and asset resolution
            fileSystem = new UltralightCAPI.ULFileSystem.ByValue();
            fileSystem.file_exists = new UltralightCAPI.FileExistsCallback() {
                @Override
                public boolean invoke(Pointer path) {
                    return resolveFile(path) != null;
                }
            };
            fileSystem.get_file_mime_type = new UltralightCAPI.GetFileMimeTypeCallback() {
                @Override
                public Pointer invoke(Pointer path) {
                    if (path == null) return ul.ulCreateString("application/unknown");
                    String p = ul.ulStringGetData(path);
                    if (p == null) return ul.ulCreateString("application/unknown");
                    String lower = p.toLowerCase(Locale.ROOT);
                    if (lower.endsWith(".woff2")) return ul.ulCreateString("font/woff2");
                    if (lower.endsWith(".woff")) return ul.ulCreateString("font/woff");
                    if (lower.endsWith(".ttf")) return ul.ulCreateString("font/ttf");
                    if (lower.endsWith(".otf")) return ul.ulCreateString("font/otf");
                    if (lower.endsWith(".html") || lower.endsWith(".htm")) return ul.ulCreateString("text/html");
                    if (lower.endsWith(".png")) return ul.ulCreateString("image/png");
                    if (lower.endsWith(".jpg") || lower.endsWith(".jpeg")) return ul.ulCreateString("image/jpeg");
                    if (lower.endsWith(".css")) return ul.ulCreateString("text/css");
                    if (lower.endsWith(".js")) return ul.ulCreateString("text/javascript");
                    return ul.ulCreateString("application/unknown");
                }
            };
            fileSystem.get_file_charset = new UltralightCAPI.GetFileCharsetCallback() {
                @Override
                public Pointer invoke(Pointer path) {
                    return ul.ulCreateString("utf-8");
                }
            };
            fileSystem.open_file = new UltralightCAPI.OpenFileCallback() {
                @Override
                public Pointer invoke(Pointer path) {
                    File f = resolveFile(path);
                    String raw = (path != null && ul != null) ? ul.ulStringGetData(path) : "null";
                    if (f == null || !f.exists()) {
                        System.out.println("[AuraFS] open_file NOT FOUND: " + raw);
                        return null;
                    }
                    try {
                        byte[] bytes = java.nio.file.Files.readAllBytes(f.toPath());
                        System.out.println("[AuraFS] open_file OK: " + raw + " -> " + f.getName() + " (" + bytes.length + " bytes)");
                        return ul.ulCreateBufferFromCopy(bytes, bytes.length);
                    } catch (Exception e) {
                        System.err.println("[AuraFS] Failed to open file " + f + ": " + e.getMessage());
                        return null;
                    }
                }
            };
            ul.ulPlatformSetFileSystem(fileSystem);

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

    public static File resolveFile(Pointer pathStr) {
        if (pathStr == null || ul == null) return null;
        String raw = ul.ulStringGetData(pathStr);
        if (raw == null || raw.isEmpty()) return null;
        if (raw.startsWith("file:")) {
            raw = raw.substring("file:".length());
            while (raw.startsWith("/")) {
                if (raw.length() >= 3 && Character.isLetter(raw.charAt(1)) && raw.charAt(2) == ':') {
                    raw = raw.substring(1);
                    break;
                }
                raw = raw.substring(1);
            }
        }
        raw = raw.replace('\\', '/');
        if (raw.length() >= 3 && raw.charAt(0) == '/' && Character.isLetter(raw.charAt(1)) && raw.charAt(2) == ':') {
            raw = raw.substring(1);
        }
        File direct = new File(raw);
        if (direct.exists()) return direct;

        File htmlDir = findHtmlDir();
        if (htmlDir != null) {
            String name = new File(raw).getName();
            File candidate = new File(htmlDir, name);
            if (candidate.exists()) return candidate;
            File sub = new File(htmlDir, raw);
            if (sub.exists()) return sub;
        }
        return null;
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

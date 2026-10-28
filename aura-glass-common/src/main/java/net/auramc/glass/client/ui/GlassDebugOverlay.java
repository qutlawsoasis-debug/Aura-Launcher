package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.BlurService;
import net.auramc.glass.common.render.GlassDiagnostics;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.option.KeybindsScreen;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import net.minecraft.text.Style;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
import org.lwjgl.glfw.GLFW;
import java.lang.reflect.Method;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

public final class GlassDebugOverlay {
    private static final String VERSION = FabricLoader.getInstance().getModContainer("minecraft")
            .map(mod -> mod.getMetadata().getVersion().getFriendlyString()).orElse("unknown");
    private static final Style MONOSPACE = Style.EMPTY.withFont(Identifier.tryParse("minecraft:uniform"));
    private static final Set<String> LOGGED_SCREENS = new HashSet<>();
    private static KeyBinding overlayKey, panelKey;
    private static boolean irisLoaded;
    private static String irisState = "not sampled";
    private static long nextIrisCheck;

    private GlassDebugOverlay() {}

    public static void initialize() {
        overlayKey = KeyBindingHelper.registerKeyBinding(new KeyBinding("key.aura.glass_diagnostics", InputUtil.Type.KEYSYM, GLFW.GLFW_KEY_F7, "category.aura"));
        panelKey = KeyBindingHelper.registerKeyBinding(new KeyBinding("key.aura.glass_test", InputUtil.Type.KEYSYM, GLFW.GLFW_KEY_F6, "category.aura"));
        ClientLifecycleEvents.CLIENT_STOPPING.register(client -> GlassDiagnostics.shutdown());
    }

    private static boolean accepts(long window) {
        MinecraftClient client = MinecraftClient.getInstance();
        return client != null && client.getWindow() != null && window == client.getWindow().getHandle()
                && !(client.currentScreen instanceof KeybindsScreen controls && controls.selectedKeyBinding != null);
    }

    public static boolean handleKey(long window, int key, int scanCode, int action) {
        if (!accepts(window)) return false;
        boolean overlay = overlayKey != null && overlayKey.matchesKey(key, scanCode);
        boolean panel = panelKey != null && panelKey.matchesKey(key, scanCode);
        if (!overlay && !panel) return GlassGalleryActions.handleKey(key,scanCode,action);
        if (action == GLFW.GLFW_PRESS) return toggle(overlay, panel);
        return overlay || panel;
    }

    public static boolean handleMouse(long window, int button, int action) {
        if (!accepts(window)) return false;
        boolean overlay = overlayKey != null && overlayKey.matchesMouse(button);
        boolean panel = panelKey != null && panelKey.matchesMouse(button);
        if (!overlay && !panel) return GlassGalleryActions.handleMouse(button,action);
        if (action == GLFW.GLFW_PRESS) return toggle(overlay, panel);
        return overlay || panel;
    }

    private static boolean toggle(boolean overlay, boolean panel) {
        if (overlay) {
            GlassDiagnostics.toggleOverlay();
            LOGGED_SCREENS.clear();
        }
        if (panel) GlassDiagnostics.togglePanel();
        return overlay || panel;
    }

    public static void beginFrame() {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client == null) return;
        GlassFrameHooks.beginFrame();
        Object screen = client.currentScreen;
        net.auramc.glass.common.render.AdaptiveText.beginFrame(screen);
        GlassDiagnostics.beginFrame(screen, VERSION);
        BlurService.get().beginFrame(screen, VERSION);
        var framebuffer = client.getFramebuffer();
        if (framebuffer != null) net.auramc.glass.common.render.GlassRenderer.beginFrame(framebuffer.fbo, framebuffer.textureWidth, framebuffer.textureHeight, (float) client.getWindow().getScaleFactor());
    }

    public static void render(DrawContext context) {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client == null || client.getWindow() == null) return;
        if (!GlassDiagnostics.overlayEnabled() && !GlassDiagnostics.panelEnabled()) {
            GlassDiagnostics.endFrame();
            return;
        }
        var window = client.getWindow();
        var framebuffer = client.getFramebuffer();
        sampleIris();
        GlassDiagnostics.context(framebuffer == null ? 0 : framebuffer.fbo,
                framebuffer == null ? window.getFramebufferWidth() : framebuffer.textureWidth,
                framebuffer == null ? window.getFramebufferHeight() : framebuffer.textureHeight,
                window.getWidth(), window.getHeight(), window.getScaleFactor(), client.getCurrentFps(), irisLoaded, irisState);
        GlassDiagnostics.endFrame();
        if (!GlassDiagnostics.overlayEnabled()) return;
        List<String> lines = GlassDiagnostics.lines();
        String screen = client.currentScreen == null ? "in game" : client.currentScreen.getClass().getName();
        if (LOGGED_SCREENS.add(screen)) {
            for (String line : lines) System.out.println("[AuraGlass] snapshot " + line);
        }
        int columns = Math.max(12, (window.getScaledWidth() - 16) / 6);
        int row = 0;
        for (String line : lines) row += Math.max(1, (line.length() + columns - 1) / columns);
        int height = row * 10 + 8;
        context.getMatrices().push();
        try {
            context.getMatrices().translate(0, 0, 500);
            context.fill(4, 4, Math.min(window.getScaledWidth() - 4, columns * 6 + 12), 4 + height, 0xC0000000);
            int y = 8;
            for (String line : lines) {
                for (int start = 0; start < Math.max(1, line.length()); start += columns) {
                    String part = line.substring(start, Math.min(line.length(), start + columns));
                    // Fixed 6px cells keep diagnostic columns stable even with a resource-pack font.
                    for (int i = 0; i < part.length(); i++) {
                        context.drawText(client.textRenderer, Text.literal(part.substring(i, i + 1)).setStyle(MONOSPACE), 8 + i * 6, y, 0xFFFFFFFF, false);
                    }
                    y += 10;
                }
            }
        } finally { context.getMatrices().pop(); }
    }

    private static void sampleIris() {
        long now = System.nanoTime();
        if (now < nextIrisCheck) return;
        nextIrisCheck = now + 1_000_000_000L;
        irisLoaded = FabricLoader.getInstance().isModLoaded("iris");
        if (!irisLoaded) { irisState = "inactive (not installed)"; return; }
        try {
            Class<?> apiClass = Class.forName("net.irisshaders.iris.api.v0.IrisApi");
            Object api = apiClass.getMethod("getInstance").invoke(null);
            Method active = apiClass.getMethod("isShaderPackInUse");
            irisState = Boolean.TRUE.equals(active.invoke(api)) ? "active" : "inactive";
        } catch (ReflectiveOperationException | LinkageError modernFailure) {
            try {
                Class<?> irisClass;
                try { irisClass = Class.forName("net.irisshaders.iris.Iris"); }
                catch (ClassNotFoundException absent) { irisClass = Class.forName("net.coderbot.iris.Iris"); }
                Object pack = irisClass.getMethod("getCurrentPack").invoke(null);
                irisState = pack instanceof java.util.Optional<?> optional ? (optional.isPresent() ? "active" : "inactive") : "unknown (unexpected Iris API)";
            } catch (ReflectiveOperationException | LinkageError legacyFailure) {
                irisState = "unknown (" + legacyFailure.getClass().getSimpleName() + ")";
                GlassDiagnostics.log("Iris shaderpack query unavailable: " + legacyFailure);
            }
        }
    }
}

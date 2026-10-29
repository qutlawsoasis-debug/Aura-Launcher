package net.auramc.glass.client.quickmenu;

import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.option.ParticlesMode;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;

public final class QuickMenuEffects {
    private static KeyBinding zoom;
    private static long previousFrame;
    private static double zoomScale = 1, gamma = Double.NaN, lastFov, lastAppliedGamma;
    private static int appliedParticles = -1;
    private static double appliedBrightness = Double.NaN;
    private static long crosshairDraws, gammaSamples, fireDraws, shieldDraws, outlineDraws;
    private static double lastFireOffset, lastShieldOffset, lastOutlineWidth;
    private static int lastOutlineColor;
    private QuickMenuEffects() {}

    public static void initialize() {
        QuickMenuVisuals.initialize();
        zoom = KeyBindingHelper.registerKeyBinding(new KeyBinding("Увеличение Aura (удерживать)", InputUtil.Type.KEYSYM, GLFW.GLFW_KEY_C, "Aura"));
        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            if (client.options == null) return;
            var config = QuickMenuConfig.get();
            double vanillaBrightness = client.options.getGamma().getValue();
            if (config.brightness < 0) {
                config.brightness = vanillaBrightness;
                QuickMenuConfig.save();
            }
            if (!Double.isFinite(appliedBrightness) || Math.abs(config.brightness - appliedBrightness) > .00001) {
                client.options.getGamma().setValue(Math.max(0, Math.min(1, config.brightness)));
                appliedBrightness = config.brightness;
            } else if (Math.abs(vanillaBrightness - appliedBrightness) > .00001) {
                config.brightness = vanillaBrightness;
                appliedBrightness = vanillaBrightness;
                QuickMenuConfig.save();
            }
            if (QuickMenuConfig.get().particles < 0) {
                QuickMenuConfig.get().particles = client.options.getParticles().getValue().ordinal();
                QuickMenuConfig.save();
            }
            int particles = Math.max(0, Math.min(2, QuickMenuConfig.get().particles));
            if (particles != appliedParticles) {
                client.options.getParticles().setValue(ParticlesMode.values()[particles]);
                appliedParticles = particles;
            } else if (client.options.getParticles().getValue().ordinal() != appliedParticles) {
                appliedParticles = client.options.getParticles().getValue().ordinal();
                QuickMenuConfig.get().particles = appliedParticles;
                QuickMenuConfig.save();
            }
        });
    }

    public static KeyBinding zoomKey() { return zoom; }
    public static boolean zoomHeld() {
        var client = MinecraftClient.getInstance();
        return zoom != null && zoom.isPressed() && client.world != null && client.currentScreen == null && QuickMenuConfig.get().zoomEnabled;
    }
    public static void beginFrame() {
        var client = MinecraftClient.getInstance();
        if (client.options == null) return;
        long now = System.nanoTime();
        double dt = previousFrame == 0 ? 0 : Math.max(0, Math.min(.1, (now - previousFrame) / 1_000_000_000.0));
        previousFrame = now;
        double targetZoom = zoomHeld() ? 1 / Math.max(1, Math.min(16, QuickMenuConfig.get().zoomFactor)) : 1;
        zoomScale += (targetZoom - zoomScale) * (1 - Math.exp(-dt * 16));
        double baseline = client.options.getGamma().getValue();
        if (!Double.isFinite(gamma)) gamma = baseline;
        double targetGamma = QuickMenuConfig.get().fullbright ? 16 : baseline;
        gamma += (targetGamma - gamma) * (1 - Math.exp(-dt * 10));
        if (!QuickMenuConfig.get().fullbright && Math.abs(gamma - baseline) < 0.001) {
            gamma = baseline;
        }
    }
    public static boolean needsLightmapUpdate() {
        var client = MinecraftClient.getInstance();
        if (client.options == null) return false;
        double baseline = client.options.getGamma().getValue();
        if (QuickMenuConfig.get().fullbright) return true;
        return Double.isFinite(gamma) && Math.abs(gamma - baseline) > 0.001;
    }
    public static double zoomScale() { return zoomScale; }
    public static double effectiveGamma() { return gamma; }
    public static double lastFov() { return lastFov; }
    public static long crosshairDraws() { return crosshairDraws; }
    public static long fireDraws() { return fireDraws; }
    public static long shieldDraws() { return shieldDraws; }
    public static long outlineDraws() { return outlineDraws; }
    public static double lastFireOffset() { return lastFireOffset; }
    public static double lastShieldOffset() { return lastShieldOffset; }
    public static double lastOutlineWidth() { return lastOutlineWidth; }
    public static int lastOutlineColor() { return lastOutlineColor; }
    public static void fireDrawn(double offset) { fireDraws++; lastFireOffset = offset; }
    public static void shieldDrawn(double offset) { shieldDraws++; lastShieldOffset = offset; }
    public static void outlineDrawn(double width, int color) { outlineDraws++; lastOutlineWidth = width; lastOutlineColor = color; }
    public static long gammaSamples() { return gammaSamples; }
    public static double lastAppliedGamma() { return lastAppliedGamma; }
    public static double fov(double vanilla) { lastFov = vanilla * zoomScale; return lastFov; }
    public static double appliedGamma(double vanilla) {
        lastAppliedGamma = Double.isFinite(gamma) ? gamma : vanilla;
        gammaSamples++;
        return lastAppliedGamma;
    }
    public static boolean scroll(double amount) {
        if (!zoomHeld() || amount == 0) return false;
        var config = QuickMenuConfig.get();
        config.zoomFactor = Math.max(1, Math.min(16, config.zoomFactor * Math.exp(amount * .12)));
        QuickMenuConfig.save();
        return true;
    }
    public static boolean crosshair(DrawContext context) {
        var config = QuickMenuConfig.get();
        var client = MinecraftClient.getInstance();
        if (!config.customCrosshair || config.crosshairStyle == 0 || client.player == null
                || client.player.isSpectator() || !client.options.getPerspective().isFirstPerson() || client.options.hudHidden) return false;
        int size = Math.max(1, Math.min(24, (int) Math.round(config.crosshairSize)));
        int x = context.getScaledWindowWidth() / 2, y = context.getScaledWindowHeight() / 2;
        int color = 0xFF000000 | config.crosshairColor & 0xFFFFFF;
        if (config.crosshairStyle == 1) {
            int radius = Math.max(1, size / 3);
            context.fill(x - radius, y - radius, x + radius + 1, y + radius + 1, 0xDD000000);
            context.fill(x - radius + 1, y - radius + 1, x + radius, y + radius, color);
        } else if (config.crosshairStyle == 2) {
            context.fill(x - size - 1, y - 1, x + size + 2, y + 2, 0xDD000000);
            context.fill(x - 1, y - size - 1, x + 2, y + size + 2, 0xDD000000);
            context.fill(x - size, y, x + size + 1, y + 1, color);
            context.fill(x, y - size, x + 1, y + size + 1, color);
        } else {
            circle(context, x, y, size, 0xDD000000, 2);
            circle(context, x, y, size, color, 1);
        }
        crosshairDraws++;
        return true;
    }
    private static void circle(DrawContext context, int x, int y, int radius, int color, int thickness) {
        int previousX = Integer.MIN_VALUE, previousY = Integer.MIN_VALUE;
        for (int angle = 0; angle < 360; angle += 4) {
            int px = x + (int) Math.round(Math.cos(Math.toRadians(angle)) * radius);
            int py = y + (int) Math.round(Math.sin(Math.toRadians(angle)) * radius);
            if (px != previousX || py != previousY) context.fill(px - thickness + 1, py - thickness + 1, px + thickness, py + thickness, color);
            previousX = px; previousY = py;
        }
    }
}

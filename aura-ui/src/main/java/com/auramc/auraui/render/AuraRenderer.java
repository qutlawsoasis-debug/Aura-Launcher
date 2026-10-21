package com.auramc.auraui.render;

import com.auramc.auraui.AuraUi;
import com.auramc.auraui.ui.Theme;
import com.mojang.blaze3d.systems.RenderSystem;
import java.io.InputStream;
import java.nio.ByteBuffer;
import java.util.HashMap;
import java.util.Map;
import net.minecraft.client.MinecraftClient;
import net.minecraft.util.Identifier;
import org.lwjgl.BufferUtils;
import org.lwjgl.nanovg.NVGColor;
import org.lwjgl.nanovg.NVGPaint;
import org.lwjgl.nanovg.NanoVG;
import org.lwjgl.nanovg.NanoVGGL3;
import org.lwjgl.opengl.GL11;

public final class AuraRenderer {
    private static long vg = 0;
    private static boolean initialized = false;
    private static int fontOnest = -1;
    private static final Map<String, Integer> imageCache = new HashMap<>();

    // Reusable structs to avoid per-frame GC allocations
    private static final NVGColor colorA = NVGColor.create();
    private static final NVGColor colorB = NVGColor.create();
    private static final NVGPaint paint = NVGPaint.create();

    private static ByteBuffer fontBuffer = null;

    private AuraRenderer() {}

    public static synchronized boolean init() {
        if (initialized) return vg != 0;
        try {
            vg = NanoVGGL3.nvgCreate(NanoVGGL3.NVG_ANTIALIAS | NanoVGGL3.NVG_STENCIL_STROKES);
            if (vg == 0) {
                AuraUi.LOGGER.error("[aura-ui] Failed to create NanoVG GL3 context");
                return false;
            }

            // Load Onest.ttf from assets
            Identifier fontId = new Identifier("aura-ui", "fonts/Onest.ttf");
            var resourceOpt = MinecraftClient.getInstance().getResourceManager().getResource(fontId);
            if (resourceOpt.isPresent()) {
                try (InputStream is = resourceOpt.get().getInputStream()) {
                    byte[] bytes = is.readAllBytes();
                    fontBuffer = BufferUtils.createByteBuffer(bytes.length);
                    fontBuffer.put(bytes);
                    fontBuffer.flip();
                    fontOnest = NanoVG.nvgCreateFontMem(vg, Theme.FONT_ONEST, fontBuffer, 0);
                    AuraUi.LOGGER.info("[aura-ui] NanoVG loaded font Onest (handle={})", fontOnest);
                }
            } else {
                AuraUi.LOGGER.warn("[aura-ui] Font Onest.ttf not found in assets");
            }

            initialized = true;
            return true;
        } catch (Throwable t) {
            AuraUi.LOGGER.error("[aura-ui] NanoVG init failed", t);
            return false;
        }
    }

    public static long getVg() {
        return vg;
    }

    public static void beginFrame(float width, float height, float devicePixelRatio) {
        if (!init()) return;

        // Save OpenGL state before NanoVG touches shaders and states
        GL11.glPushAttrib(GL11.GL_ALL_ATTRIB_BITS);

        NanoVG.nvgBeginFrame(vg, width, height, devicePixelRatio);
    }

    public static void endFrame() {
        if (vg == 0) return;
        NanoVG.nvgEndFrame(vg);

        // Restore OpenGL state
        GL11.glPopAttrib();

        // Ensure Minecraft shaders and blending are restored
        RenderSystem.enableBlend();
        RenderSystem.defaultBlendFunc();
    }

    private static void setNvgColor(NVGColor target, int argb) {
        float a = ((argb >> 24) & 0xFF) / 255.0f;
        float r = ((argb >> 16) & 0xFF) / 255.0f;
        float g = ((argb >> 8) & 0xFF) / 255.0f;
        float b = (argb & 0xFF) / 255.0f;
        target.r(r).g(g).b(b).a(a);
    }

    public static void rect(float x, float y, float w, float h, int color) {
        if (vg == 0) return;
        setNvgColor(colorA, color);
        NanoVG.nvgBeginPath(vg);
        NanoVG.nvgRect(vg, x, y, w, h);
        NanoVG.nvgFillColor(vg, colorA);
        NanoVG.nvgFill(vg);
    }

    public static void roundRect(float x, float y, float w, float h, float radius, int color) {
        if (vg == 0) return;
        setNvgColor(colorA, color);
        NanoVG.nvgBeginPath(vg);
        NanoVG.nvgRoundedRect(vg, x, y, w, h, radius);
        NanoVG.nvgFillColor(vg, colorA);
        NanoVG.nvgFill(vg);
    }

    public static void outline(float x, float y, float w, float h, float radius, float strokeWidth, int color) {
        if (vg == 0) return;
        setNvgColor(colorA, color);
        NanoVG.nvgBeginPath(vg);
        NanoVG.nvgRoundedRect(vg, x + strokeWidth * 0.5f, y + strokeWidth * 0.5f,
                w - strokeWidth, h - strokeWidth, Math.max(0, radius - strokeWidth * 0.5f));
        NanoVG.nvgStrokeWidth(vg, strokeWidth);
        NanoVG.nvgStrokeColor(vg, colorA);
        NanoVG.nvgStroke(vg);
    }

    public static void gradient(float x, float y, float w, float h, float radius, int topColor, int bottomColor) {
        if (vg == 0) return;
        setNvgColor(colorA, topColor);
        setNvgColor(colorB, bottomColor);
        NanoVG.nvgLinearGradient(vg, x, y, x, y + h, colorA, colorB, paint);
        NanoVG.nvgBeginPath(vg);
        if (radius > 0) {
            NanoVG.nvgRoundedRect(vg, x, y, w, h, radius);
        } else {
            NanoVG.nvgRect(vg, x, y, w, h);
        }
        NanoVG.nvgFillPaint(vg, paint);
        NanoVG.nvgFill(vg);
    }

    public static void shadow(float x, float y, float w, float h, float radius, float blur, float spread, int shadowColor) {
        if (vg == 0) return;
        setNvgColor(colorA, shadowColor);
        setNvgColor(colorB, Theme.rgba(0, 0, 0, 0));
        NanoVG.nvgBoxGradient(vg, x - spread, y - spread, w + spread * 2, h + spread * 2,
                radius + spread, blur, colorA, colorB, paint);
        NanoVG.nvgBeginPath(vg);
        NanoVG.nvgRect(vg, x - blur - spread * 2, y - blur - spread * 2,
                w + (blur + spread * 2) * 2, h + (blur + spread * 2) * 2);
        NanoVG.nvgFillPaint(vg, paint);
        NanoVG.nvgFill(vg);
    }

    public static void text(String text, float x, float y, float size, int color, int align) {
        if (vg == 0 || text == null || text.isEmpty()) return;
        setNvgColor(colorA, color);
        NanoVG.nvgFontSize(vg, size);
        NanoVG.nvgFontFace(vg, Theme.FONT_ONEST);
        NanoVG.nvgTextAlign(vg, align);
        NanoVG.nvgFillColor(vg, colorA);
        NanoVG.nvgText(vg, x, y, text);
    }

    public static float textWidth(String text, float size) {
        if (vg == 0 || text == null || text.isEmpty()) return 0;
        NanoVG.nvgFontSize(vg, size);
        NanoVG.nvgFontFace(vg, Theme.FONT_ONEST);
        float[] bounds = new float[4];
        return NanoVG.nvgTextBounds(vg, 0, 0, text, bounds);
    }

    public static int getImage(Identifier id) {
        if (vg == 0) return -1;
        String path = id.toString();
        if (imageCache.containsKey(path)) {
            return imageCache.get(path);
        }
        try {
            var resOpt = MinecraftClient.getInstance().getResourceManager().getResource(id);
            if (resOpt.isPresent()) {
                try (InputStream is = resOpt.get().getInputStream()) {
                    byte[] data = is.readAllBytes();
                    ByteBuffer buf = BufferUtils.createByteBuffer(data.length);
                    buf.put(data);
                    buf.flip();
                    int handle = NanoVG.nvgCreateImageMem(vg, 0, buf);
                    imageCache.put(path, handle);
                    return handle;
                }
            }
        } catch (Throwable t) {
            AuraUi.LOGGER.error("[aura-ui] Failed to load image: {}", path, t);
        }
        imageCache.put(path, -1);
        return -1;
    }

    public static void drawImage(int imageHandle, float x, float y, float w, float h, float alpha) {
        if (vg == 0 || imageHandle <= 0) return;
        NanoVG.nvgImagePattern(vg, x, y, w, h, 0, imageHandle, alpha, paint);
        NanoVG.nvgBeginPath(vg);
        NanoVG.nvgRect(vg, x, y, w, h);
        NanoVG.nvgFillPaint(vg, paint);
        NanoVG.nvgFill(vg);
    }

    public static void scissor(float x, float y, float w, float h) {
        if (vg == 0) return;
        NanoVG.nvgScissor(vg, x, y, w, h);
    }

    public static void resetScissor() {
        if (vg == 0) return;
        NanoVG.nvgResetScissor(vg);
    }
}

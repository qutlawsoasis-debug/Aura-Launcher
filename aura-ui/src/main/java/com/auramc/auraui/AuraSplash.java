package com.auramc.auraui;

import com.mojang.blaze3d.platform.GlStateManager;
import com.mojang.blaze3d.systems.RenderSystem;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.MathHelper;
import net.minecraft.util.math.RotationAxis;

/**
 * Экран загрузки «Остров»: размытый мир, плавающее «северное сияние»,
 * парящая стеклянная капсула Aura с бегущим кольцом и полосой прогресса.
 * Все размеры — в пикселях макета лаунчера (капсула 400×76 при высоте окна 768).
 */
public final class AuraSplash {
    private static final float DESIGN_HEIGHT = 768f;
    private static final long START = System.currentTimeMillis();

    private AuraSplash() {
    }

    /**
     * @param alpha        общая непрозрачность экрана (как у ванилы: появление при перезагрузке ресурсов и уход)
     * @param capsuleAlpha непрозрачность капсулы (гаснет первой, когда загрузка закончилась)
     * @param progress     сглаженный прогресс загрузки 0..1
     */
    public static void render(DrawContext ctx, float alpha, float capsuleAlpha, float progress) {
        MinecraftClient client = MinecraftClient.getInstance();
        int sw = ctx.getScaledWindowWidth();
        int sh = ctx.getScaledWindowHeight();
        double guiScale = client.getWindow().getScaleFactor();
        float k = MathHelper.clamp(client.getWindow().getFramebufferHeight() / DESIGN_HEIGHT, 0.6f, 3f);
        // Пиксели макета → единицы GUI
        float px = (float) (k / guiScale);
        float t = (System.currentTimeMillis() - START) / 1000f;

        RenderSystem.disableDepthTest();
        RenderSystem.depthMask(false);
        RenderSystem.enableBlend();
        RenderSystem.defaultBlendFunc();

        drawBackground(ctx, sw, sh, t, alpha);
        drawAurora(ctx, sw, sh, px, t, alpha);
        drawCapsule(ctx, sw, sh, px, t, alpha * capsuleAlpha, progress);

        ctx.setShaderColor(1f, 1f, 1f, 1f);
        RenderSystem.defaultBlendFunc();
        RenderSystem.disableBlend();
        RenderSystem.depthMask(true);
        RenderSystem.enableDepthTest();
    }

    /** Мир под матовым стеклом: «cover» на весь экран, медленный наплыв и затемнение как в лаунчере. */
    private static void drawBackground(DrawContext ctx, int sw, int sh, float t, float alpha) {
        float zoom = 1.12f - 0.10f * easeOut(Math.min(t / 14f, 1f));
        float cover = Math.max(sw / 1280f, sh / 720f) * zoom;
        float w = 1280f * cover;
        float h = 720f * cover;
        drawTex(ctx, AuraTextures.BACKGROUND, (sw - w) / 2f, (sh - h) / 2f, w, h, 1280, 720, 1f, 1f, 1f, alpha, false);

        int top = argb(alpha * 0.55f, 8, 11, 18);
        int mid = argb(alpha * 0.35f, 8, 11, 18);
        int bottom = argb(alpha * 0.75f, 8, 11, 18);
        ctx.fillGradient(0, 0, sw, (int) (sh * 0.45f), top, mid);
        ctx.fillGradient(0, (int) (sh * 0.45f), sw, sh, mid, bottom);
    }

    /** Три мягких пятна света (бирюза, фиолет, мята) плавают за капсулой, складываясь по яркости. */
    private static void drawAurora(DrawContext ctx, int sw, int sh, float px, float t, float alpha) {
        float cx = sw / 2f;
        float cy = sh / 2f;
        glow(ctx, cx, cy, px, wave(t, 11f), -180, -60, 120, 40, 1f, 1.25f, 100, 210, 255, 0.16f * alpha);
        glow(ctx, cx, cy, px, wave(t, 13f), 160, 80, -120, -70, 1.15f, 0.9f, 140, 110, 255, 0.12f * alpha);
        glow(ctx, cx, cy, px, wave(t, 9f), 0, 140, 40, -120, 0.8f, 1.1f, 80, 255, 200, 0.08f * alpha);
    }

    private static void glow(DrawContext ctx, float cx, float cy, float px, float p,
                             float x0, float y0, float x1, float y1, float s0, float s1,
                             int r, int g, int b, float a) {
        float size = 520f * px * MathHelper.lerp(p, s0, s1);
        float x = cx + MathHelper.lerp(p, x0, x1) * px - size / 2f;
        float y = cy + MathHelper.lerp(p, y0, y1) * px - size / 2f;
        drawTex(ctx, AuraTextures.GLOW, x, y, size, size, 512, 512, r / 255f, g / 255f, b / 255f, a, true);
    }

    /** Капсула парит вверх-вниз; кольцо вокруг знака крутится; внизу — полоса прогресса. */
    private static void drawCapsule(DrawContext ctx, int sw, int sh, float px, float t, float alpha, float progress) {
        if (alpha <= 0.01f) return;
        float w = 400f * px;
        float h = 76f * px;
        float floatY = (float) Math.sin(t * Math.PI * 2 / 4.2) * 6f * px;
        float x = (sw - w) / 2f;
        float y = (sh - h) / 2f + floatY;

        drawTex(ctx, AuraTextures.SHADOW, x - 40f * px, y + 10f * px, 480f * px, 140f * px, 960, 280, 1f, 1f, 1f, alpha * 0.8f, false);
        drawTex(ctx, AuraTextures.CAPSULE, x, y, w, h, 800, 152, 1f, 1f, 1f, alpha, false);

        // Кольцо вокруг знака: центр знака в макете — (38, 38) от левого верхнего угла капсулы
        MatrixStack m = ctx.getMatrices();
        m.push();
        m.translate(x + 38f * px, y + 38f * px, 0f);
        m.multiply(RotationAxis.POSITIVE_Z.rotationDegrees((t * 225f) % 360f));
        drawTex(ctx, AuraTextures.RING, -24f * px, -24f * px, 48f * px, 48f * px, 96, 96, 1f, 1f, 1f, alpha, false);
        m.pop();

        // Полоса прогресса под надписью, в пикселях макета
        m.push();
        m.translate(x, y, 0f);
        m.scale(px, px, 1f);
        int left = 72;
        int right = 372;
        int top = 64;
        int fillTo = left + Math.round((right - left) * MathHelper.clamp(progress, 0f, 1f));
        ctx.fill(left, top, right, top + 3, argb(alpha * 0.12f, 255, 255, 255));
        ctx.fill(left, top, fillTo, top + 3, argb(alpha, 100, 210, 255));
        RenderSystem.enableBlend();
        RenderSystem.defaultBlendFunc();
        m.pop();
    }

    private static void drawTex(DrawContext ctx, Identifier tex, float x, float y, float w, float h,
                                int texW, int texH, float r, float g, float b, float a, boolean additive) {
        if (a <= 0.003f) return;
        RenderSystem.enableBlend();
        if (additive) {
            RenderSystem.blendFunc(GlStateManager.SrcFactor.SRC_ALPHA, GlStateManager.DstFactor.ONE);
        } else {
            RenderSystem.defaultBlendFunc();
        }
        MatrixStack m = ctx.getMatrices();
        m.push();
        m.translate(x, y, 0f);
        m.scale(w / texW, h / texH, 1f);
        ctx.setShaderColor(r, g, b, a);
        ctx.drawTexture(tex, 0, 0, 0f, 0f, texW, texH, texW, texH);
        ctx.setShaderColor(1f, 1f, 1f, 1f);
        m.pop();
        if (additive) {
            RenderSystem.defaultBlendFunc();
        }
    }

    /** Плавное «туда-обратно» за период (как animation-direction: alternate). */
    private static float wave(float t, float period) {
        return (float) (0.5 - 0.5 * Math.cos(t * Math.PI / period));
    }

    private static float easeOut(float x) {
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }

    private static int argb(float a, int r, int g, int b) {
        int ai = MathHelper.clamp(Math.round(a * 255f), 0, 255);
        return ai << 24 | r << 16 | g << 8 | b;
    }
}

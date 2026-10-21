package com.auramc.auraui;

import com.mojang.blaze3d.platform.GlStateManager;
import com.mojang.blaze3d.systems.RenderSystem;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.MathHelper;

/**
 * Кинематографичный экран загрузки по концепту docs/redesign/game/loading.html:
 * мир на весь экран с медленным наплывом, виньетка, крупный AURA с бегущим бликом,
 * подзаголовок «Сервер друзей», линия прогресса 2px и плавно сменяющиеся подсказки.
 */
public final class AuraSplash {
    private static final float DESIGN_HEIGHT = 768f;
    private static final long START = System.currentTimeMillis();

    private AuraSplash() {
    }

    /**
     * @param alpha        общая непрозрачность экрана (появление при перезагрузке ресурсов и уход)
     * @param capsuleAlpha непрозрачность элементов интерфейса (гаснет первой при завершении загрузки)
     * @param progress     сглаженный прогресс загрузки 0..1
     */
    public static void render(DrawContext ctx, float alpha, float capsuleAlpha, float progress) {
        MinecraftClient client = MinecraftClient.getInstance();
        int sw = ctx.getScaledWindowWidth();
        int sh = ctx.getScaledWindowHeight();
        double guiScale = client.getWindow().getScaleFactor();
        float k = MathHelper.clamp(client.getWindow().getFramebufferHeight() / DESIGN_HEIGHT, 0.6f, 3f);
        float px = (float) (k / guiScale);
        float t = (System.currentTimeMillis() - START) / 1000f;

        RenderSystem.disableDepthTest();
        RenderSystem.depthMask(false);
        RenderSystem.enableBlend();
        RenderSystem.defaultBlendFunc();

        // 1. Фон мира с медленным наплывом (1.08 -> 1.0 за 14 сек) и виньетка
        drawBackground(ctx, sw, sh, t, alpha);

        // 2. Центр: крупный вордмарк AURA с бегущим бликом и подзаголовок
        drawCenterBrand(ctx, sw, sh, px, t, alpha * capsuleAlpha);

        // 3. Низ: линия прогресса, статус загрузки слева и подсказки справа
        drawBottomBar(ctx, sw, sh, px, t, alpha * capsuleAlpha, progress);

        ctx.setShaderColor(1f, 1f, 1f, 1f);
        RenderSystem.defaultBlendFunc();
        RenderSystem.disableBlend();
        RenderSystem.depthMask(true);
        RenderSystem.enableDepthTest();
    }

    private static void drawBackground(DrawContext ctx, int sw, int sh, float t, float alpha) {
        // Наплыв 1.08 -> 1.00 за 14 сек
        float zoom = 1.08f - 0.08f * easeOut(Math.min(t / 14f, 1f));
        float cover = Math.max(sw / 1920f, sh / 1080f) * zoom;
        float w = 1920f * cover;
        float h = 1080f * cover;
        float bx = (sw - w) / 2f;
        float by = (sh - h) / 2f;

        drawTex(ctx, AuraTextures.BACKGROUND, bx, by, w, h, 1920, 1080, 1f, 1f, 1f, alpha, false);

        // Виньетка поверх фона
        drawTex(ctx, AuraTextures.VIGNETTE, 0, 0, sw, sh, 1920, 1080, 1f, 1f, 1f, alpha * 0.9f, false);
    }

    private static void drawCenterBrand(DrawContext ctx, int sw, int sh, float px, float t, float alpha) {
        if (alpha <= 0.005f) return;

        // Вордмарк: текстура 738x256 (исходный размер с отступами и тенью)
        // В макете вордмарк ~370x128 CSS px при высоте 768
        float ww = (738f / 2f) * px;
        float wh = (256f / 2f) * px;
        float cx = (sw - ww) / 2f;
        float cy = sh * 0.44f - wh / 2f;

        drawTex(ctx, AuraTextures.WORDMARK, cx, cy, ww, wh, 738, 256, 1f, 1f, 1f, alpha, false);

        // Блик только по буквам (wordmark_mask + scissor) раз в 4 сек
        float cycle = t % 4.0f;
        if (cycle < 1.5f) {
            float p = cycle / 1.5f;
            float smoothP = p * p * (3f - 2f * p); // ease-in-out
            float shineCenterX = cx + smoothP * ww;

            // 3 мягких слоя scissor разной ширины: 20%, 12%, 6% ширины вордмарка
            float[] widths = new float[] { ww * 0.20f, ww * 0.12f, ww * 0.06f };
            float[] alphas = new float[] { 0.10f, 0.20f, 0.35f };

            int topY = (int) Math.max(0, cy);
            int botY = (int) Math.min(sh, cy + wh);

            for (int i = 0; i < 3; i++) {
                float halfW = widths[i] / 2f;
                int leftX = (int) Math.max(cx, shineCenterX - halfW);
                int rightX = (int) Math.min(cx + ww, shineCenterX + halfW);
                if (rightX > leftX && botY > topY) {
                    ctx.enableScissor(leftX, topY, rightX, botY);
                    drawTex(ctx, AuraTextures.WORDMARK_MASK, cx, cy, ww, wh, 740, 256, 1f, 1f, 1f, alpha * alphas[i], true);
                    ctx.disableScissor();
                }
            }
        }

        // Подзаголовок: текстура 410x54 (исходный 205x27)
        float subW = (410f / 2f) * px;
        float subH = (54f / 2f) * px;
        float subX = (sw - subW) / 2f;
        float subY = cy + wh - 10f * px;

        drawTex(ctx, AuraTextures.SUBTITLE, subX, subY, subW, subH, 410, 54, 1f, 1f, 1f, alpha * 0.95f, false);
    }

    private static void drawBottomBar(DrawContext ctx, int sw, int sh, float px, float t, float alpha, float progress) {
        if (alpha <= 0.005f) return;

        // Линия прогресса: 62.5% ширины экрана, 2px высота
        float barWidth = sw * 0.625f;
        float barX = (sw - barWidth) / 2f;
        float barY = sh - 56f * px;
        float fillWidth = barWidth * MathHelper.clamp(progress, 0f, 1f);

        // 1. Метка слева: label.png (202x52 -> 101x26)
        float labelW = (202f / 2f) * px;
        float labelH = (52f / 2f) * px;
        float labelX = barX;
        float labelY = barY - labelH - 6f * px;
        drawTex(ctx, AuraTextures.LABEL, labelX, labelY, labelW, labelH, 202, 52, 1f, 1f, 1f, alpha, false);

        // 2. Сменяющиеся подсказки справа (каждые 4 с со сглаженным затуханием 0.4 с)
        float tipCycle = 4.0f;
        int tipIndex = ((int) (t / tipCycle)) % AuraTextures.TIPS.length;
        float phase = t % tipCycle;
        float tipAlpha = 1.0f;
        if (phase < 0.4f) {
            tipAlpha = phase / 0.4f;
        } else if (phase > 3.6f) {
            tipAlpha = (4.0f - phase) / 0.4f;
        }
        tipAlpha *= alpha * 0.8f;

        Identifier curTip = AuraTextures.TIPS[tipIndex];
        int tipNativeW = tipIndex == 0 ? 976 : (tipIndex == 1 ? 958 : (tipIndex == 2 ? 780 : 816));
        float tipW = (tipNativeW / 2f) * px;
        float tipH = (50f / 2f) * px;
        float tipX = barX + barWidth - tipW;
        float tipY = barY - tipH - 6f * px;
        drawTex(ctx, curTip, tipX, tipY, tipW, tipH, tipNativeW, 50, 1f, 1f, 1f, tipAlpha, false);

        // 3. Подложка линии: белый 12%
        int bgCol = argb(alpha * 0.12f, 255, 255, 255);
        ctx.fill((int) barX, (int) barY, (int) (barX + barWidth), (int) (barY + 2f), bgCol);

        // 4. Заполнение линии: акцент #64D2FF
        if (fillWidth > 0.5f) {
            int fillCol = argb(alpha, 100, 210, 255);
            ctx.fill((int) barX, (int) barY, (int) (barX + fillWidth), (int) (barY + 2f), fillCol);
        }

        // После fill обязательно восстанавливаем смешивание
        RenderSystem.enableBlend();
        RenderSystem.defaultBlendFunc();

        // 5. Маленькая светящаяся точка на конце заполнения
        if (fillWidth > 1f && fillWidth < barWidth - 1f) {
            float dotX = barX + fillWidth;
            float dotY = barY + 1f;
            int dotCol = argb(alpha, 255, 255, 255);
            ctx.fill((int) (dotX - 1f), (int) (dotY - 2f), (int) (dotX + 2f), (int) (dotY + 3f), dotCol);
            RenderSystem.enableBlend();
            RenderSystem.defaultBlendFunc();
        }
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

    private static float easeOut(float x) {
        return 1f - (1f - x) * (1f - x) * (1f - x);
    }

    private static int argb(float a, int r, int g, int b) {
        int ai = MathHelper.clamp(Math.round(a * 255f), 0, 255);
        return ai << 24 | r << 16 | g << 8 | b;
    }
}

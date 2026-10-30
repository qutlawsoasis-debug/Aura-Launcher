package net.auramc.glass.client.ui;

import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.render.BlurService;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.TabButtonWidget;
import org.lwjgl.glfw.GLFW;

import java.util.List;

public final class TabSegmentControl {
    private static float currentThumbX = -1;
    private static float thumbVelocity = 0;
    private static float flightStartX = -1;
    private static float flightTargetX = -1;
    private static int lastSelectedTab = -1;
    private static long lastTimeNanos = 0;
    private static float pressScale = 1.0f;
    private static final float[] tabAlpha = new float[16];
    private static boolean forceMidFlight = false;

    // Spring parameters: response = 0.3s, damping = 0.82
    public static final float SPRING_RESPONSE = 0.30f;
    public static final float SPRING_DAMPING = 0.82f;
    private static final float OMEGA_N = (float) (2.0 * Math.PI / SPRING_RESPONSE); // ~20.94395
    private static final float SPRING_K = OMEGA_N * OMEGA_N;                        // ~438.649
    private static final float SPRING_C = 2.0f * SPRING_DAMPING * OMEGA_N;         // ~34.348

    private static final java.lang.reflect.Field HEIGHT_FIELD;
    static {
        java.lang.reflect.Field f = null;
        try {
            f = net.minecraft.client.gui.widget.ClickableWidget.class.getDeclaredField("height");
            f.setAccessible(true);
        } catch (Throwable ignored) {}
        HEIGHT_FIELD = f;
    }

    private static void setWidgetHeight(net.minecraft.client.gui.widget.ClickableWidget widget, int height) {
        if (HEIGHT_FIELD != null) {
            try {
                HEIGHT_FIELD.setInt(widget, height);
                return;
            } catch (Throwable ignored) {}
        }
        try {
            net.minecraft.client.gui.widget.ClickableWidget.class.getMethod("setHeight", int.class).invoke(widget, height);
        } catch (Throwable ignored) {}
    }

    public static void setForceMidFlight(boolean force) {
        forceMidFlight = force;
    }

    public static boolean isForceMidFlight() {
        return forceMidFlight;
    }

    public static void reset() {
        currentThumbX = -1;
        thumbVelocity = 0;
        flightStartX = -1;
        flightTargetX = -1;
        lastSelectedTab = -1;
        lastTimeNanos = 0;
        pressScale = 1.0f;
        for (int i = 0; i < tabAlpha.length; i++) tabAlpha[i] = 0;
        forceMidFlight = false;
    }

    public static float computeContainerHeight(int windowHeight, float scaleFactor) {
        float heightPhys = 32.0f * ((float) windowHeight / 1080.0f);
        return Math.max(16.0f, heightPhys / scaleFactor);
    }

    public static float computeThumbHeight(float containerHeight, float scaleFactor) {
        float pad = 2.0f / scaleFactor;
        return containerHeight - 2.0f * pad;
    }

    public static float computeStretchFactor(float currentX, float startX, float targetX) {
        float dist = Math.abs(targetX - startX);
        if (dist < 0.1f) return 1.0f;
        float s = (currentX - startX) / (targetX - startX);
        float sClamped = Math.max(0.0f, Math.min(1.0f, s));
        return 1.0f + 0.08f * (4.0f * sClamped * (1.0f - sClamped));
    }

    public static int computeTextColor(boolean isLight, float alphaProgress) {
        int baseAlpha = Math.round(153 + (242 - 153) * Math.max(0f, Math.min(1f, alphaProgress)));
        int rgb = isLight ? 0x000000 : 0xFFFFFF;
        return (baseAlpha << 24) | rgb;
    }

    public static float getCurrentThumbX() {
        return currentThumbX;
    }

    public static float getPressScale() {
        return pressScale;
    }

    public static void render(DrawContext context, List<TabButtonWidget> tabButtons, int mouseX, int mouseY, float delta) {
        if (tabButtons == null || tabButtons.isEmpty()) return;
        MinecraftClient client = MinecraftClient.getInstance();
        if (client == null || client.getWindow() == null) return;

        int winH = client.getWindow().getHeight();
        float scaleFactor = (float) client.getWindow().getScaleFactor();
        if (scaleFactor <= 0) scaleFactor = 2.0f;

        float containerH = computeContainerHeight(winH, scaleFactor);
        float containerY = 6.0f;
        float containerRadius = containerH * 0.5f;
        float pad = 2.0f / scaleFactor;

        int n = tabButtons.size();
        int screenWidth = client.getWindow().getScaledWidth();
        float totalAvailableW = Math.min(400, screenWidth) - 28;
        float segmentW = (float) Math.round(totalAvailableW / (float) n);
        float containerW = segmentW * n;
        float containerX = (screenWidth - containerW) * 0.5f;

        // Synchronize tab button hitboxes with exact segment boundaries
        int currentTab = 0;
        for (int i = 0; i < n; i++) {
            TabButtonWidget btn = tabButtons.get(i);
            float segX = containerX + i * segmentW;
            btn.setX((int) segX);
            btn.setY((int) containerY);
            btn.setWidth((int) segmentW);
            setWidgetHeight(btn, (int) containerH);
            if (btn.isCurrentTab()) {
                currentTab = i;
            }
        }

        // Timing and spring animation update
        long now = System.nanoTime();
        float dt = lastTimeNanos == 0 ? 0.016f : Math.min(0.050f, (now - lastTimeNanos) / 1_000_000_000.0f);
        lastTimeNanos = now;

        float thumbBaseW = segmentW - 2.0f * pad;
        float thumbH = containerH - 2.0f * pad;
        float thumbRadius = thumbH * 0.5f;
        float targetThumbX = containerX + currentTab * segmentW + pad;

        if (currentThumbX < 0) {
            currentThumbX = targetThumbX;
            flightStartX = targetThumbX;
            flightTargetX = targetThumbX;
            thumbVelocity = 0;
            lastSelectedTab = currentTab;
        } else if (lastSelectedTab != currentTab) {
            flightStartX = currentThumbX;
            flightTargetX = targetThumbX;
            lastSelectedTab = currentTab;
        }

        if (forceMidFlight) {
            float x0 = containerX + pad;
            float x1 = containerX + segmentW + pad;
            currentThumbX = (x0 + x1) * 0.5f;
            flightStartX = x0;
            flightTargetX = x1;
            thumbVelocity = 0;
        } else {
            // Semi-implicit Euler spring sub-stepping (1 ms steps for stability)
            float remaining = dt;
            while (remaining > 0) {
                float step = Math.min(remaining, 0.001f);
                float force = -SPRING_K * (currentThumbX - targetThumbX) - SPRING_C * thumbVelocity;
                thumbVelocity += force * step;
                currentThumbX += thumbVelocity * step;
                remaining -= step;
            }
            if (Math.abs(currentThumbX - targetThumbX) < 0.04f && Math.abs(thumbVelocity) < 0.08f) {
                currentThumbX = targetThumbX;
                thumbVelocity = 0;
                flightStartX = targetThumbX;
                flightTargetX = targetThumbX;
            }
        }

        // Stretch during flight (up to +8% at midpoint)
        float stretch = computeStretchFactor(currentThumbX, flightStartX, flightTargetX);
        float stretchedW = thumbBaseW * stretch;

        // Press scale (0.96 when pressed)
        long windowHandle = client.getWindow().getHandle();
        boolean mouseDown = GLFW.glfwGetMouseButton(windowHandle, GLFW.GLFW_MOUSE_BUTTON_LEFT) == GLFW.GLFW_PRESS;
        boolean mouseOver = mouseX >= containerX && mouseX <= containerX + containerW
                && mouseY >= containerY && mouseY <= containerY + containerH;
        float targetPress = (mouseDown && mouseOver) ? 0.96f : 1.0f;
        pressScale += (targetPress - pressScale) * Math.min(1.0f, dt * 25.0f);

        float finalThumbW = stretchedW * pressScale;
        float finalThumbH = thumbH * pressScale;
        float drawThumbX = currentThumbX - (stretchedW - thumbBaseW) * 0.5f + (stretchedW - finalThumbW) * 0.5f;
        float drawThumbY = (containerY + pad) + (thumbH - finalThumbH) * 0.5f;

        // Ambient brightness for dark/light mode
        float cx = (containerX + containerW * 0.5f) / (float) screenWidth;
        float cy = (containerY + containerH * 0.5f) / (float) screenWidth;
        float ambient = BlurService.get() != null ? BlurService.get().sampleAmbient(cx, cy) : 0.2f;
        boolean isLight = ambient > 0.55f;

        // Render Container and Thumb
        context.draw();
        boolean nativeReady = AuraRenderer.isReady() && AuraRenderer.begin(
                context.getMatrices().peek().getPositionMatrix(),
                RenderSystem.getProjectionMatrix(),
                RenderSystem.getModelViewMatrix()
        );

        if (nativeReady) {
            // 1. Container Capsule: rgba(118, 118, 128, 0.24) -> 0x3D767680
            AuraRenderer.rect(containerX, containerY, containerW, containerH, containerRadius, 0, 0x3D767680, 0);

            // 2. Thumb
            if (isLight) {
                // Light mode: shadow 0 1 3 rgba(0,0,0,0.12), fill white 0.90 (0xE6FFFFFF)
                AuraRenderer.shadow(drawThumbX, drawThumbY + 1.0f / scaleFactor, finalThumbW, finalThumbH, thumbRadius * pressScale, 0x1F000000);
                AuraRenderer.rect(drawThumbX, drawThumbY, finalThumbW, finalThumbH, thumbRadius * pressScale, 0, 0xE6FFFFFF, 0);
            } else {
                // Dark mode: rgba(255, 255, 255, 0.18) -> 0x2EFFFFFF
                AuraRenderer.rect(drawThumbX, drawThumbY, finalThumbW, finalThumbH, thumbRadius * pressScale, 0, 0x2EFFFFFF, 0);
            }

            // 3. Tab Text
            for (int i = 0; i < n; i++) {
                TabButtonWidget btn = tabButtons.get(i);
                float targetA = btn.isCurrentTab() ? 1.0f : 0.0f;
                float stepA = dt / 0.150f;
                if (tabAlpha[i] < targetA) tabAlpha[i] = Math.min(targetA, tabAlpha[i] + stepA);
                else if (tabAlpha[i] > targetA) tabAlpha[i] = Math.max(targetA, tabAlpha[i] - stepA);

                int textColor = computeTextColor(isLight, tabAlpha[i]);
                int weight = tabAlpha[i] >= 0.5f ? 600 : 500;
                float fontSize = 14.0f;
                String text = btn.getMessage().getString();
                float tw = AuraRenderer.textWidth(text, fontSize, weight);
                float tx = containerX + i * segmentW + (segmentW - tw) * 0.5f;
                float ty = containerY + (containerH - fontSize) * 0.5f;
                AuraRenderer.text(text, tx, ty, fontSize, textColor, weight);
            }

            AuraRenderer.flush();
        } else {
            // Fallback for non-MSDF environments
            context.fill((int) containerX, (int) containerY, (int) (containerX + containerW), (int) (containerY + containerH), 0x3D767680);
            int thumbColor = isLight ? 0xE6FFFFFF : 0x2EFFFFFF;
            context.fill((int) drawThumbX, (int) drawThumbY, (int) (drawThumbX + finalThumbW), (int) (drawThumbY + finalThumbH), thumbColor);
            for (int i = 0; i < n; i++) {
                TabButtonWidget btn = tabButtons.get(i);
                int textColor = computeTextColor(isLight, btn.isCurrentTab() ? 1.0f : 0.0f);
                String text = btn.getMessage().getString();
                int tw = client.textRenderer.getWidth(text);
                int tx = (int) (containerX + i * segmentW + (segmentW - tw) * 0.5f);
                int ty = (int) (containerY + (containerH - 8) * 0.5f);
                context.drawText(client.textRenderer, text, tx, ty, textColor, false);
            }
        }
    }
}

package com.auramc.auraui.screen;

import com.auramc.auraui.AuraUi;
import com.auramc.auraui.render.AuraRenderer;
import com.auramc.auraui.render.Blur;
import com.auramc.auraui.ui.Anim;
import com.auramc.auraui.ui.Theme;
import java.util.ArrayList;
import java.util.List;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.MessageScreen;
import net.minecraft.client.gui.screen.OpenToLanScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.option.OptionsScreen;
import net.minecraft.text.Text;
import org.lwjgl.nanovg.NanoVG;

public class AuraPauseScreen extends Screen {
    public static class PauseItem {
        public String label;
        public boolean isPrimary;
        public Runnable action;
        public float hover = 0.0f;
        public float x = 0.0f;
        public float y = 0.0f;
        public float width = 0.0f;
        public float height = 0.0f;

        public PauseItem(String label, boolean isPrimary, Runnable action) {
            this.label = label;
            this.isPrimary = isPrimary;
            this.action = action;
        }
    }

    private final List<PauseItem> items = new ArrayList<>();
    private float openTime = 0.0f;
    private long lastTimeNanos = 0;

    // Capsule dimensions
    private float capsuleX = 0.0f;
    private float capsuleY = 0.0f;
    private float capsuleW = 0.0f;
    private float capsuleH = 76.0f;

    public AuraPauseScreen() {
        super(Text.literal("Aura Pause"));
    }

    @Override
    protected void init() {
        super.init();
        Blur.loadConfig();
        openTime = 0.0f;
        lastTimeNanos = System.nanoTime();

        items.clear();
        // Items: Продолжить (акцент) · Пригласить · Открыть для сети · Настройки · В меню
        items.add(new PauseItem("Продолжить", true, this::close));

        items.add(new PauseItem("Пригласить", false, () -> {
            AuraUi.LOGGER.info("[aura-ui] Invite friend clicked");
        }));

        items.add(new PauseItem("Открыть для сети", false, () -> {
            if (client != null) client.setScreen(new OpenToLanScreen(this));
        }));

        items.add(new PauseItem("Настройки", false, () -> {
            if (client != null) client.setScreen(new OptionsScreen(this, client.options));
        }));

        items.add(new PauseItem("В меню", false, () -> {
            if (client == null) return;
            boolean isSinglePlayer = client.isInSingleplayer();
            if (client.world != null) {
                client.world.disconnect();
            }
            if (isSinglePlayer) {
                client.disconnect(new MessageScreen(Text.translatable("menu.savingLevel")));
            } else {
                client.disconnect();
            }
            client.setScreen(new TitleScreen());
        }));
    }

    @Override
    public boolean shouldCloseOnEsc() {
        return true;
    }

    @Override
    public void render(DrawContext context, int mouseX, int mouseY, float deltaTick) {
        if (client == null) return;

        long now = System.nanoTime();
        float dt = lastTimeNanos == 0 ? 0.016f : (float) ((now - lastTimeNanos) / 1_000_000_000.0);
        lastTimeNanos = now;
        openTime += dt;

        int fbWidth = client.getWindow().getFramebufferWidth();
        int fbHeight = client.getWindow().getFramebufferHeight();
        int winWidth = client.getWindow().getWidth();
        int winHeight = client.getWindow().getHeight();
        float scale = AuraRenderer.uiScale();

        float winW = fbWidth / scale;
        float winH = fbHeight / scale;

        float realMouseX = (float) (client.mouse.getX() * fbWidth / (winWidth > 0 ? winWidth : 1) / scale);
        float realMouseY = (float) (client.mouse.getY() * fbHeight / (winHeight > 0 ? winHeight : 1) / scale);

        // 1. Two-pass GPU Blur of the in-game world on full screen
        Blur.renderBlur(44.0f);

        // 2. NanoVG rendering
        if (!AuraRenderer.beginFrame(winW, winH, scale)) {
            return;
        }

        // Appearance animation: opacity & scale 0.96 -> 1.0
        float enterT = Anim.clamp(openTime / 0.22f, 0.0f, 1.0f);
        float animProgress = Anim.easeOutCubic(enterT);
        float scaleFactor = 0.96f + 0.04f * animProgress;

        // Darken overlay
        int overlayAlpha = (int) (0.45f * 255 * animProgress);
        AuraRenderer.rect(0, 0, winW, winH, (overlayAlpha << 24) | 0x04080E);

        // In low/off mode, render a fallback backdrop
        if (Blur.getMode() == Blur.Mode.LOW) {
            AuraRenderer.rect(0, 0, winW, winH, Theme.rgba(18, 22, 32, 0.85f * animProgress));
        }

        // Capsule layout centered at the bottom
        // Width: 740px, Height: 76px, Bottom: 36px
        capsuleW = Math.min(740.0f, winW - 40.0f) * scaleFactor;
        capsuleH = 76.0f * scaleFactor;
        capsuleX = (winW - capsuleW) * 0.5f;
        capsuleY = winH - 36.0f - capsuleH;

        float radius = capsuleH * 0.5f;

        // Capsule shadow
        AuraRenderer.shadow(capsuleX, capsuleY, capsuleW, capsuleH, radius, 32.0f, 0.0f,
                Theme.withAlpha(Theme.GLASS_SHADOW, animProgress));

        // Glass capsule fill
        AuraRenderer.roundRect(capsuleX, capsuleY, capsuleW, capsuleH, radius,
                Theme.withAlpha(Theme.GLASS_BG, animProgress));

        // Glass capsule border: 1px line
        AuraRenderer.outline(capsuleX, capsuleY, capsuleW, capsuleH, radius, 1.0f,
                Theme.withAlpha(Theme.GLASS_BORDER, animProgress));

        // Glass sheen 1px top highlight
        AuraRenderer.rect(capsuleX + radius * 0.4f, capsuleY + 1.0f, capsuleW - radius * 0.8f, 1.0f,
                Theme.withAlpha(Theme.GLASS_SHEEN, animProgress));

        // Render Capsule Items
        // Items: Продолжить (акцент) · Пригласить · Открыть для сети · Настройки · В меню
        float paddingX = 14.0f;
        float innerW = capsuleW - paddingX * 2.0f;

        // "Продолжить" button takes fixed width (160px), remaining items share the rest
        float primaryW = 160.0f;
        float remainingW = innerW - primaryW - 12.0f;
        float regularItemW = remainingW / (items.size() - 1);

        float curX = capsuleX + paddingX;
        float itemH = 50.0f;
        float itemY = capsuleY + (capsuleH - itemH) * 0.5f;

        for (PauseItem item : items) {
            if (item.isPrimary) {
                item.x = curX;
                item.y = itemY;
                item.width = primaryW;
                item.height = itemH;
                curX += primaryW + 12.0f;

                boolean hovered = realMouseX >= item.x && realMouseX <= item.x + item.width
                        && realMouseY >= item.y && realMouseY <= item.y + item.height;
                item.hover = Anim.lerp(item.hover, hovered ? 1.0f : 0.0f, dt * 10.0f);

                int bgCol = item.hover > 0.5f ? Theme.ACCENT_HOVER : Theme.ACCENT;
                AuraRenderer.roundRect(item.x, item.y, item.width, item.height, 99.0f,
                        Theme.withAlpha(bgCol, animProgress));
                AuraRenderer.shadow(item.x, item.y, item.width, item.height, 99.0f, 16.0f, 0.0f,
                        Theme.withAlpha(Theme.rgba(100, 210, 255, 0.35f), animProgress));

                AuraRenderer.text(item.label, item.x + item.width * 0.5f, item.y + item.height * 0.5f + 6.0f,
                        16.0f, Theme.withAlpha(Theme.ACCENT_INK, animProgress), NanoVG.NVG_ALIGN_CENTER);
            } else {
                item.x = curX;
                item.y = itemY;
                item.width = regularItemW - 4.0f;
                item.height = itemH;
                curX += regularItemW;

                boolean hovered = realMouseX >= item.x && realMouseX <= item.x + item.width
                        && realMouseY >= item.y && realMouseY <= item.y + item.height;
                item.hover = Anim.lerp(item.hover, hovered ? 1.0f : 0.0f, dt * 10.0f);

                if (item.hover > 0.01f) {
                    AuraRenderer.roundRect(item.x, item.y, item.width, item.height, 99.0f,
                            Theme.withAlpha(Theme.FILL_2, item.hover * animProgress));
                }

                int textCol = item.hover > 0.5f ? Theme.TEXT_PRIMARY : Theme.TEXT_SECONDARY;
                AuraRenderer.text(item.label, item.x + item.width * 0.5f, item.y + item.height * 0.5f + 6.0f,
                        15.0f, Theme.withAlpha(textCol, animProgress), NanoVG.NVG_ALIGN_CENTER);
            }
        }

        AuraRenderer.endFrame();
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (client == null || button != 0) return super.mouseClicked(mouseX, mouseY, button);

        int fbWidth = client.getWindow().getFramebufferWidth();
        int fbHeight = client.getWindow().getFramebufferHeight();
        int winWidth = client.getWindow().getWidth();
        int winHeight = client.getWindow().getHeight();
        float scale = AuraRenderer.uiScale();

        float realMouseX = (float) (client.mouse.getX() * fbWidth / (winWidth > 0 ? winWidth : 1) / scale);
        float realMouseY = (float) (client.mouse.getY() * fbHeight / (winHeight > 0 ? winHeight : 1) / scale);

        for (PauseItem item : items) {
            if (realMouseX >= item.x && realMouseX <= item.x + item.width
                    && realMouseY >= item.y && realMouseY <= item.y + item.height) {
                item.action.run();
                return true;
            }
        }

        return super.mouseClicked(mouseX, mouseY, button);
    }
}

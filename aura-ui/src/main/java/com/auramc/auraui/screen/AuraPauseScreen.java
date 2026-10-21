package com.auramc.auraui.screen;

import com.auramc.auraui.AuraUi;
import com.auramc.auraui.render.AuraRenderer;
import com.auramc.auraui.render.Blur;
import com.auramc.auraui.ui.Anim;
import com.auramc.auraui.ui.Button;
import com.auramc.auraui.ui.Panel;
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
    private final Panel panel = new Panel();
    private final List<Button> buttons = new ArrayList<>();
    private float openTime = 0.0f;
    private long lastTimeNanos = 0;

    public AuraPauseScreen() {
        super(Text.literal("Aura Pause"));
    }

    @Override
    protected void init() {
        super.init();
        Blur.loadConfig();
        openTime = 0.0f;
        lastTimeNanos = System.nanoTime();

        buttons.clear();

        // 1. Продолжить (Primary)
        buttons.add(new Button("Продолжить", Button.Type.PRIMARY, this::close));

        // 2. Пригласить друга
        buttons.add(new Button("Пригласить друга", Button.Type.REGULAR, () -> {
            AuraUi.LOGGER.info("[aura-ui] Invite friend clicked");
        }));

        // 3. Открыть для сети
        buttons.add(new Button("Открыть для сети", Button.Type.REGULAR, () -> {
            if (client != null) client.setScreen(new OpenToLanScreen(this));
        }));

        // 4. Настройки
        buttons.add(new Button("Настройки", Button.Type.REGULAR, () -> {
            if (client != null) client.setScreen(new OptionsScreen(this, client.options));
        }));

        // 5. В главное меню (Danger)
        buttons.add(new Button("В главное меню", Button.Type.DANGER, () -> {
            if (client == null) return;
            boolean singlePlayer = client.isInSingleplayer();
            if (client.world != null) {
                client.world.disconnect();
            }
            if (singlePlayer) {
                client.disconnect(new MessageScreen(Text.translatable("menu.savingLevel")));
            } else {
                client.disconnect();
            }
            client.setScreen(new AuraTitleScreen());
        }));
    }

    @Override
    public void close() {
        if (client != null) client.setScreen(null);
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

        // 1. Two-pass GPU Blur of the in-game world
        Blur.renderBlur(44.0f);

        // 2. NanoVG rendering in scaled window coordinates (sharp HiDPI via scale factor)
        if (!AuraRenderer.beginFrame(winW, winH, scale)) {
            return;
        }

        // Darken overlay
        float animProgress = Anim.easeOutCubic(openTime / 0.25f);
        int overlayAlpha = (int) (0.65f * 255 * animProgress);
        AuraRenderer.rect(0, 0, winW, winH, (overlayAlpha << 24) | 0x080B12);

        // In low/off mode, render a fallback backdrop
        if (Blur.getMode() == Blur.Mode.LOW) {
            AuraRenderer.rect(0, 0, winW, winH, Theme.rgba(18, 22, 32, 0.85f * animProgress));
        }

        // Panel layout (centered, width 380px)
        float panelW = Math.min(380.0f, winW * 0.9f);
        panel.width = panelW;
        float btnH = 48.0f;
        float gap = 10.0f;
        float totalH = 60.0f + buttons.size() * (btnH + gap) + 20.0f;
        panel.height = totalH;

        float slideOffset = (1.0f - animProgress) * 16.0f;
        panel.x = (winW - panel.width) * 0.5f;
        panel.y = (winH - panel.height) * 0.5f + slideOffset;

        // Render Title
        float titleFontSize = 22.0f;
        float titleY = panel.y + 36.0f;
        AuraRenderer.text("ИГРА НА ПАУЗЕ", winW * 0.5f, titleY, titleFontSize,
                Theme.withAlpha(0xFFFFFFFF, animProgress), NanoVG.NVG_ALIGN_CENTER);

        // Subtitle line decorations
        float tw = AuraRenderer.textWidth("ИГРА НА ПАУЗЕ", titleFontSize);
        float lineW = 32.0f;
        AuraRenderer.rect(winW * 0.5f - tw * 0.5f - lineW - 14.0f, titleY - titleFontSize * 0.35f,
                lineW, 1.0f, Theme.rgba(255, 255, 255, 0.25f * animProgress));
        AuraRenderer.rect(winW * 0.5f + tw * 0.5f + 14.0f, titleY - titleFontSize * 0.35f,
                lineW, 1.0f, Theme.rgba(255, 255, 255, 0.25f * animProgress));

        // Render buttons
        float curY = panel.y + 64.0f;
        for (Button btn : buttons) {
            btn.x = panel.x;
            btn.y = curY;
            btn.width = panel.width;
            btn.height = btnH;
            btn.render(realMouseX, realMouseY, dt);
            curY += btnH + gap;
        }

        AuraRenderer.endFrame();
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (client == null) return false;
        int fbWidth = client.getWindow().getFramebufferWidth();
        int fbHeight = client.getWindow().getFramebufferHeight();
        int winWidth = client.getWindow().getWidth();
        int winHeight = client.getWindow().getHeight();
        float scale = AuraRenderer.uiScale();
        float realMouseX = (float) (client.mouse.getX() * fbWidth / (winWidth > 0 ? winWidth : 1) / scale);
        float realMouseY = (float) (client.mouse.getY() * fbHeight / (winHeight > 0 ? winHeight : 1) / scale);

        for (Button btn : buttons) {
            if (btn.mouseClicked(realMouseX, realMouseY, button)) return true;
        }
        return super.mouseClicked(mouseX, mouseY, button);
    }

    @Override
    public boolean mouseReleased(double mouseX, double mouseY, int button) {
        if (client == null) return false;
        int fbWidth = client.getWindow().getFramebufferWidth();
        int fbHeight = client.getWindow().getFramebufferHeight();
        int winWidth = client.getWindow().getWidth();
        int winHeight = client.getWindow().getHeight();
        float scale = AuraRenderer.uiScale();
        float realMouseX = (float) (client.mouse.getX() * fbWidth / (winWidth > 0 ? winWidth : 1) / scale);
        float realMouseY = (float) (client.mouse.getY() * fbHeight / (winHeight > 0 ? winHeight : 1) / scale);

        for (Button btn : buttons) {
            if (btn.mouseReleased(realMouseX, realMouseY, button)) return true;
        }
        return super.mouseReleased(mouseX, mouseY, button);
    }
}

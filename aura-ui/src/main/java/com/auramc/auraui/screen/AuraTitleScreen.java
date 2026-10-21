package com.auramc.auraui.screen;

import com.auramc.auraui.AuraUi;
import com.auramc.auraui.render.AuraRenderer;
import com.auramc.auraui.ui.Anim;
import com.auramc.auraui.ui.Theme;
import java.util.ArrayList;
import java.util.List;
import net.fabricmc.loader.api.FabricLoader;
import net.fabricmc.loader.api.ModContainer;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.ConnectScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.multiplayer.MultiplayerScreen;
import net.minecraft.client.gui.screen.option.OptionsScreen;
import net.minecraft.client.gui.screen.world.SelectWorldScreen;
import net.minecraft.client.network.ServerAddress;
import net.minecraft.client.network.ServerInfo;
import net.minecraft.client.option.ServerList;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
import org.lwjgl.nanovg.NanoVG;

public class AuraTitleScreen extends Screen {
    public static class NavItem {
        public String label;
        public String desc;
        public String badge;
        public Runnable action;
        public float hover = 0.0f;

        public NavItem(String label, String desc, String badge, Runnable action) {
            this.label = label;
            this.desc = desc;
            this.badge = badge;
            this.action = action;
        }
    }

    private final List<NavItem> navItems = new ArrayList<>();
    private final Identifier bgTexture = new Identifier("aura-ui", "textures/menu/bg_5.jpg");
    private float openTime = 0.0f;
    private long lastTimeNanos = 0;
    private float mouseParallaxX = 0.0f;
    private float mouseParallaxY = 0.0f;

    public AuraTitleScreen() {
        super(Text.literal("Aura Title"));
    }

    @Override
    protected void init() {
        super.init();
        openTime = 0.0f;
        lastTimeNanos = System.nanoTime();

        navItems.clear();

        // 1. Играть на сервере
        navItems.add(new NavItem("Играть на сервере", "Подключиться к миру сервера", "СЕТЬ", () -> {
            if (client == null) return;
            ServerList serverList = new ServerList(client);
            serverList.loadFile();
            if (serverList.size() > 0) {
                ServerInfo serverInfo = serverList.get(0);
                ServerAddress address = ServerAddress.parse(serverInfo.address);
                ConnectScreen.connect(this, client, address, serverInfo, false);
            } else {
                client.setScreen(new MultiplayerScreen(this));
            }
        }));

        // 2. Одиночная игра
        navItems.add(new NavItem("Одиночная игра", "Локальные миры и приключения", null, () -> {
            if (client != null) client.setScreen(new SelectWorldScreen(this));
        }));

        // 3. Друзья онлайн
        navItems.add(new NavItem("Друзья онлайн", "Список контактов и совместная игра", null, () -> {
            AuraUi.LOGGER.info("[aura-ui] Friends clicked");
        }));

        // 4. Настройки
        navItems.add(new NavItem("Настройки", "Графика, звук и управление", null, () -> {
            if (client != null) client.setScreen(new OptionsScreen(this, client.options));
        }));

        // 5. Выход
        navItems.add(new NavItem("Выход", "Выйти на рабочий стол", null, () -> {
            if (client != null) client.scheduleStop();
        }));
    }

    @Override
    public boolean shouldCloseOnEsc() {
        return false;
    }

    private static boolean loggedFirstRender = false;

    @Override
    public void render(DrawContext context, int mouseX, int mouseY, float deltaTick) {
        if (!loggedFirstRender) {
            AuraUi.LOGGER.info("[aura-ui] AuraTitleScreen.render called! width={}, height={}", width, height);
            loggedFirstRender = true;
        }
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

        // Target mouse parallax
        float targetPx = (realMouseX / winW - 0.5f) * 20.0f;
        float targetPy = (realMouseY / winH - 0.5f) * 20.0f;
        mouseParallaxX = Anim.lerp(mouseParallaxX, targetPx, dt * 4.0f);
        mouseParallaxY = Anim.lerp(mouseParallaxY, targetPy, dt * 4.0f);

        if (!AuraRenderer.beginFrame(winW, winH, scale)) {
            return;
        }

        // 1. Background image cover with slow continuous zoom
        int bgImage = AuraRenderer.getImage(bgTexture);
        float zoomProgress = (float) (Math.sin(openTime * 0.15f) * 0.5 + 0.5);
        float bgScale = 1.04f + zoomProgress * 0.05f;
        float bgW = winW * bgScale;
        float bgH = winH * bgScale;
        float bgX = (winW - bgW) * 0.5f - mouseParallaxX;
        float bgY = (winH - bgH) * 0.5f - mouseParallaxY;
        if (bgImage > 0) {
            AuraRenderer.drawImage(bgImage, bgX, bgY, bgW, bgH, 1.0f);
        } else {
            AuraRenderer.rect(0, 0, winW, winH, 0xFF080B12);
        }

        // Atmospheric vignette and contrast shade
        AuraRenderer.gradient(0, 0, winW * 0.55f, winH, 0,
                Theme.rgba(8, 11, 18, 0.85f), Theme.rgba(8, 11, 18, 0.40f));
        AuraRenderer.gradient(0, 0, winW, winH * 0.25f, 0,
                Theme.rgba(8, 11, 18, 0.50f), Theme.rgba(8, 11, 18, 0.0f));
        AuraRenderer.gradient(0, winH * 0.75f, winW, winH * 0.25f, 0,
                Theme.rgba(8, 11, 18, 0.0f), Theme.rgba(8, 11, 18, 0.65f));

        // 2. Header (Top Bar)
        float topPad = 32.0f;
        float rightPad = 36.0f;

        // Player badge on top-right
        String playerNick = client.getSession() != null ? client.getSession().getUsername() : "Player";
        float badgeH = 36.0f;
        float nickW = AuraRenderer.textWidth(playerNick, 14.0f);
        float badgeW = nickW + 48.0f;
        float badgeX = winW - rightPad - badgeW;
        float badgeY = topPad;

        AuraRenderer.shadow(badgeX, badgeY, badgeW, badgeH, badgeH * 0.5f, 16.0f, 0, Theme.rgba(0, 0, 0, 0.35f));
        AuraRenderer.roundRect(badgeX, badgeY, badgeW, badgeH, badgeH * 0.5f, Theme.PANEL_GLASS);
        AuraRenderer.outline(badgeX, badgeY, badgeW, badgeH, badgeH * 0.5f, 1.0f, Theme.PANEL_BORDER);

        // Avatar circle with first letter of nickname
        float avSize = badgeH - 8.0f;
        float avX = badgeX + 4.0f;
        float avY = badgeY + 4.0f;
        AuraRenderer.roundRect(avX, avY, avSize, avSize, avSize * 0.5f, Theme.FILL_2);
        String initial = playerNick.length() > 0 ? playerNick.substring(0, 1).toUpperCase() : "P";
        AuraRenderer.text(initial, avX + avSize * 0.5f, avY + avSize * 0.5f + 4.5f, 13.0f,
                Theme.ACCENT, NanoVG.NVG_ALIGN_CENTER);

        AuraRenderer.text(playerNick, avX + avSize + 8.0f, badgeY + badgeH * 0.5f + 4.5f,
                14.0f, Theme.TEXT_PRIMARY, NanoVG.NVG_ALIGN_LEFT);

        // Friends pill next to profile
        float friendsPillW = 160.0f;
        float friendsPillX = badgeX - friendsPillW - 12.0f;
        AuraRenderer.roundRect(friendsPillX, badgeY, friendsPillW, badgeH, badgeH * 0.5f, Theme.PANEL_GLASS);
        AuraRenderer.outline(friendsPillX, badgeY, friendsPillW, badgeH, badgeH * 0.5f, 1.0f, Theme.PANEL_BORDER);

        // Live dot
        float dotX = friendsPillX + 16.0f;
        float dotY = badgeY + badgeH * 0.5f;
        AuraRenderer.shadow(dotX - 3.5f, dotY - 3.5f, 7.0f, 7.0f, 3.5f, 8.0f, 0, 0xFF34D399);
        AuraRenderer.roundRect(dotX - 3.5f, dotY - 3.5f, 7.0f, 7.0f, 3.5f, 0xFF34D399);

        AuraRenderer.text("Друзья онлайн: —", dotX + 10.0f, badgeY + badgeH * 0.5f + 4.5f,
                13.0f, Theme.TEXT_SECONDARY, NanoVG.NVG_ALIGN_LEFT);

        // 3. Center Left Brand Block
        float leftPad = 80.0f;
        float brandY = winH * 0.28f;

        // Wordmark
        float titleProgress = Anim.easeOutCubic(Math.min(1.0f, openTime / 0.35f));
        float titleShift = (1.0f - titleProgress) * 20.0f;
        AuraRenderer.text("Aura", leftPad, brandY + titleShift, 52.0f,
                Theme.withAlpha(Theme.TEXT_PRIMARY, titleProgress), NanoVG.NVG_ALIGN_LEFT);

        // Tagline
        AuraRenderer.text("Сервер друзей", leftPad, brandY + 36.0f + titleShift, 16.0f,
                Theme.withAlpha(Theme.ACCENT, titleProgress), NanoVG.NVG_ALIGN_LEFT);

        // 4. Navigation Menu Items
        float navStartY = brandY + 70.0f;
        float itemH = 50.0f;
        float itemW = 380.0f;

        for (int i = 0; i < navItems.size(); i++) {
            NavItem item = navItems.get(i);
            float itemDelay = 0.2f + i * 0.06f;
            float itemProgress = Anim.easeOutCubic(Math.max(0.0f, Math.min(1.0f, (openTime - itemDelay) / 0.3f)));

            float curY = navStartY + i * (itemH + 12.0f);
            boolean hovered = realMouseX >= leftPad && realMouseX <= leftPad + itemW &&
                              realMouseY >= curY && realMouseY <= curY + itemH;

            item.hover = Anim.lerp(item.hover, hovered ? 1.0f : 0.0f, dt * 14.0f);

            float slideX = leftPad + item.hover * 16.0f;
            float cascadeY = curY + (1.0f - itemProgress) * 16.0f;
            float alpha = itemProgress;

            // Cyan left accent line when hovered
            if (item.hover > 0.01f) {
                float lineH = itemH - 14.0f;
                float lineTop = cascadeY + 7.0f;
                AuraRenderer.shadow(slideX - 14.0f, lineTop, 3.0f, lineH, 1.5f, 14.0f * item.hover, 0,
                        Theme.rgba(100, 210, 255, 0.6f * item.hover));
                AuraRenderer.roundRect(slideX - 14.0f, lineTop, 3.0f, lineH, 1.5f,
                        Theme.withAlpha(Theme.ACCENT, item.hover * alpha));
            }

            // Label
            int labelColor = hovered ? 0xFFFFFFFF : Theme.rgba(255, 255, 255, 0.85f);
            AuraRenderer.text(item.label, slideX, cascadeY + 22.0f, 20.0f,
                    Theme.withAlpha(labelColor, alpha), NanoVG.NVG_ALIGN_LEFT);

            // Badge (e.g. СЕТЬ)
            if (item.badge != null) {
                float lw = AuraRenderer.textWidth(item.label, 20.0f);
                float badgeX2 = slideX + lw + 12.0f;
                float badgeY2 = cascadeY + 8.0f;
                float badgeH2 = 18.0f;
                float badgeW2 = AuraRenderer.textWidth(item.badge, 10.0f) + 14.0f;

                AuraRenderer.roundRect(badgeX2, badgeY2, badgeW2, badgeH2, 9.0f,
                        Theme.rgba(100, 210, 255, 0.15f * alpha));
                AuraRenderer.outline(badgeX2, badgeY2, badgeW2, badgeH2, 9.0f, 1.0f,
                        Theme.rgba(100, 210, 255, 0.35f * alpha));
                AuraRenderer.text(item.badge, badgeX2 + badgeW2 * 0.5f, badgeY2 + badgeH2 * 0.5f + 3.5f,
                        10.0f, Theme.withAlpha(Theme.ACCENT, alpha), NanoVG.NVG_ALIGN_CENTER);
            }

            // Description (appears on hover)
            if (item.hover > 0.01f) {
                float descShift = (1.0f - item.hover) * -8.0f;
                AuraRenderer.text(item.desc, slideX + descShift, cascadeY + 40.0f, 13.0f,
                        Theme.withAlpha(Theme.TEXT_DIM, item.hover * alpha), NanoVG.NVG_ALIGN_LEFT);
            }
        }

        // 5. Bottom Status Bar
        float bottomY = winH - 40.0f;

        // Server status pill
        float pillW = 240.0f;
        float pillH = 34.0f;
        float pillX = leftPad;
        AuraRenderer.shadow(pillX, bottomY - pillH * 0.5f, pillW, pillH, pillH * 0.5f, 12.0f, 0, Theme.rgba(0, 0, 0, 0.3f));
        AuraRenderer.roundRect(pillX, bottomY - pillH * 0.5f, pillW, pillH, pillH * 0.5f, Theme.PANEL_GLASS);
        AuraRenderer.outline(pillX, bottomY - pillH * 0.5f, pillW, pillH, pillH * 0.5f, 1.0f, Theme.PANEL_BORDER);

        float sDotX = pillX + 16.0f;
        float sDotY = bottomY;
        AuraRenderer.roundRect(sDotX - 3.5f, sDotY - 3.5f, 7.0f, 7.0f, 3.5f, 0xFF34D399);

        AuraRenderer.text("Сервер: —   |   Пинг: —", sDotX + 10.0f, bottomY + 4.5f,
                13.0f, Theme.TEXT_SECONDARY, NanoVG.NVG_ALIGN_LEFT);

        // Version info on bottom right
        String modVer = FabricLoader.getInstance().getModContainer("aura-ui")
                .map(ModContainer::getMetadata)
                .map(meta -> meta.getVersion().getFriendlyString())
                .orElse("1.0.0");
        String verText = "Minecraft 1.20.1 Fabric • v" + modVer;
        AuraRenderer.text(verText, winW - rightPad, bottomY + 4.5f,
                13.0f, Theme.TEXT_DIM, NanoVG.NVG_ALIGN_RIGHT);

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

        float leftPad = 80.0f;
        float brandY = (fbHeight / scale) * 0.28f;
        float navStartY = brandY + 70.0f;
        float itemH = 50.0f;
        float itemW = 380.0f;

        for (int i = 0; i < navItems.size(); i++) {
            float curY = navStartY + i * (itemH + 12.0f);
            if (realMouseX >= leftPad && realMouseX <= leftPad + itemW &&
                realMouseY >= curY && realMouseY <= curY + itemH) {
                NavItem item = navItems.get(i);
                if (item.action != null) {
                    item.action.run();
                    return true;
                }
            }
        }

        return super.mouseClicked(mouseX, mouseY, button);
    }
}

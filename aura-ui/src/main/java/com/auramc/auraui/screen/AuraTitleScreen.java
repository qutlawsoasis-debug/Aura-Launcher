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
        public Runnable action;
        public float hover = 0.0f;
        public float x = 0.0f;
        public float y = 0.0f;
        public float width = 0.0f;
        public float height = 0.0f;

        public NavItem(String label, Runnable action) {
            this.label = label;
            this.action = action;
        }
    }

    private final List<NavItem> navItems = new ArrayList<>();
    private final Identifier bgTexture = new Identifier("aura-ui", "textures/menu/bg_5.jpg");
    private float openTime = 0.0f;
    private long lastTimeNanos = 0;
    private float mouseParallaxX = 0.0f;
    private float mouseParallaxY = 0.0f;

    // Island spring expand progress: 0.0 = idle (400px), 1.0 = expanded (780px)
    private float expandProgress = 0.0f;
    private float playButtonHover = 0.0f;

    // Hitbox bounds in current frame
    private float islandX = 0.0f;
    private float islandY = 0.0f;
    private float islandW = 0.0f;
    private float islandH = 76.0f;

    private float playX = 0.0f;
    private float playY = 0.0f;
    private float playW = 150.0f;
    private float playH = 52.0f;

    public AuraTitleScreen() {
        super(Text.literal("Aura Title"));
    }

    @Override
    protected void init() {
        super.init();
        openTime = 0.0f;
        lastTimeNanos = System.nanoTime();

        navItems.clear();
        // Items: Одиночная · Сервер · Друзья · Настройки · Выход
        navItems.add(new NavItem("Одиночная", () -> {
            if (client != null) client.setScreen(new SelectWorldScreen(this));
        }));

        navItems.add(new NavItem("Сервер", () -> {
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

        navItems.add(new NavItem("Друзья", () -> {
            AuraUi.LOGGER.info("[aura-ui] Friends clicked");
        }));

        navItems.add(new NavItem("Настройки", () -> {
            if (client != null) client.setScreen(new OptionsScreen(this, client.options));
        }));

        navItems.add(new NavItem("Выход", () -> {
            if (client != null) client.scheduleStop();
        }));
    }

    @Override
    public boolean shouldCloseOnEsc() {
        return false;
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

        // Smooth mouse parallax
        float targetPx = (realMouseX / winW - 0.5f) * 20.0f;
        float targetPy = (realMouseY / winH - 0.5f) * 20.0f;
        mouseParallaxX = Anim.lerp(mouseParallaxX, targetPx, dt * 4.0f);
        mouseParallaxY = Anim.lerp(mouseParallaxY, targetPy, dt * 4.0f);

        if (!AuraRenderer.beginFrame(winW, winH, scale)) {
            return;
        }

        // 1. Fullscreen background image (clean cover, subtle vignette)
        int bgImage = AuraRenderer.getImage(bgTexture);
        float zoomProgress = (float) (Math.sin(openTime * 0.15f) * 0.5 + 0.5);
        float bgScale = 1.04f + zoomProgress * 0.05f;
        float bgW = winW * bgScale;
        float bgH = winH * bgScale;
        float bgX = (winW - bgW) * 0.5f + mouseParallaxX;
        float bgY = (winH - bgH) * 0.5f + mouseParallaxY;

        if (bgImage > 0) {
            AuraRenderer.drawImage(bgImage, bgX, bgY, bgW, bgH, 1.0f);
        } else {
            AuraRenderer.rect(0, 0, winW, winH, 0xFF070B10);
        }

        // Subtle vignette around borders (shade from island.html)
        AuraRenderer.gradient(0, 0, winW, winH * 0.22f, 0.0f,
                Theme.rgba(4, 8, 14, 0.35f), Theme.rgba(4, 8, 14, 0.0f));
        AuraRenderer.gradient(0, winH * 0.70f, winW, winH * 0.30f, 0.0f,
                Theme.rgba(4, 8, 14, 0.0f), Theme.rgba(4, 8, 14, 0.50f));

        // 2. Brand "Aura" top-left (island.html)
        AuraRenderer.text("Aura", 40.0f, 48.0f, 26.0f, Theme.TEXT_PRIMARY, NanoVG.NVG_ALIGN_LEFT);

        // 3. Version text in bottom-right corner (40% alpha, small)
        String versionStr = "1.20.1";
        var containerOpt = FabricLoader.getInstance().getModContainer("aura-ui");
        if (containerOpt.isPresent()) {
            versionStr = containerOpt.get().getMetadata().getVersion().getFriendlyString();
        }
        AuraRenderer.text("Aura UI v" + versionStr, winW - 32.0f, winH - 24.0f, 13.0f,
                Theme.TEXT_DIM, NanoVG.NVG_ALIGN_RIGHT);

        // 4. Island Capsule Bottom Center (island.html)
        float idleW = 400.0f;
        float expandedW = Math.min(780.0f, winW - 40.0f);

        // Check if mouse is hovering over island
        float currentW = Anim.lerp(idleW, expandedW, Anim.easeOutCubic(expandProgress));
        islandW = currentW;
        islandH = 76.0f;
        islandX = (winW - islandW) * 0.5f;
        islandY = winH - 36.0f - islandH;

        boolean isMouseOverIsland = realMouseX >= islandX && realMouseX <= islandX + islandW
                && realMouseY >= islandY && realMouseY <= islandY + islandH;

        float targetExpand = isMouseOverIsland ? 1.0f : 0.0f;
        expandProgress = Anim.lerp(expandProgress, targetExpand, dt * 8.5f);

        float radius = islandH * 0.5f; // 38px capsule border radius

        // Capsule backdrop shadow
        AuraRenderer.shadow(islandX, islandY, islandW, islandH, radius, 32.0f, 0.0f, Theme.GLASS_SHADOW);

        // Glass capsule fill: rgba(118, 118, 128, .24)
        AuraRenderer.roundRect(islandX, islandY, islandW, islandH, radius, Theme.GLASS_BG);

        // Glass capsule border: 1px line rgba(255, 255, 255, .22)
        AuraRenderer.outline(islandX, islandY, islandW, islandH, radius, 1.0f, Theme.GLASS_BORDER);

        // Glass sheen 1px top highlight
        AuraRenderer.rect(islandX + radius * 0.4f, islandY + 1.0f, islandW - radius * 0.8f, 1.0f, Theme.GLASS_SHEEN);

        // Inside the capsule:
        // Left: Avatar circle with first letter of nick
        String playerNick = client.getSession() != null ? client.getSession().getUsername() : "Player";
        if (playerNick == null || playerNick.isEmpty()) playerNick = "Player";
        String initial = playerNick.substring(0, 1).toUpperCase();

        float avatarSize = 44.0f;
        float avatarX = islandX + 16.0f;
        float avatarY = islandY + (islandH - avatarSize) * 0.5f;
        AuraRenderer.circle(avatarX + avatarSize * 0.5f, avatarY + avatarSize * 0.5f, avatarSize * 0.5f,
                Theme.rgba(255, 255, 255, 0.18f));
        AuraRenderer.circleStroke(avatarX + avatarSize * 0.5f, avatarY + avatarSize * 0.5f, avatarSize * 0.5f,
                1.5f, Theme.rgba(255, 255, 255, 0.35f));
        AuraRenderer.text(initial, avatarX + avatarSize * 0.5f, avatarY + avatarSize * 0.5f + 7.0f, 20.0f,
                Theme.TEXT_PRIMARY, NanoVG.NVG_ALIGN_CENTER);

        // Idle state: Nickname and subtitle ("Aura Pack")
        float idleOpacity = Anim.clamp(1.0f - expandProgress * 2.2f, 0.0f, 1.0f);
        if (idleOpacity > 0.01f) {
            float metaX = avatarX + avatarSize + 14.0f;
            AuraRenderer.text(playerNick, metaX, islandY + 32.0f, 17.0f,
                    Theme.withAlpha(Theme.TEXT_PRIMARY, idleOpacity), NanoVG.NVG_ALIGN_LEFT);
            AuraRenderer.text("Aura Pack 1.2", metaX, islandY + 52.0f, 13.0f,
                    Theme.withAlpha(Theme.TEXT_TERTIARY, idleOpacity), NanoVG.NVG_ALIGN_LEFT);
        }

        // Expanded state: Navigation items (Одиночная · Сервер · Друзья · Настройки · Выход)
        float navOpacity = Anim.clamp((expandProgress - 0.25f) / 0.75f, 0.0f, 1.0f);
        if (navOpacity > 0.01f) {
            float navStartX = avatarX + avatarSize + 10.0f;
            float availableNavW = (islandX + islandW - 170.0f) - navStartX;
            float itemW = availableNavW / navItems.size();

            for (int i = 0; i < navItems.size(); i++) {
                NavItem item = navItems.get(i);
                item.x = navStartX + i * itemW;
                item.y = islandY + (islandH - 38.0f) * 0.5f;
                item.width = itemW - 4.0f;
                item.height = 38.0f;

                boolean itemHovered = isMouseOverIsland && realMouseX >= item.x && realMouseX <= item.x + item.width
                        && realMouseY >= item.y && realMouseY <= item.y + item.height;

                item.hover = Anim.lerp(item.hover, itemHovered ? 1.0f : 0.0f, dt * 10.0f);

                if (item.hover > 0.01f) {
                    AuraRenderer.roundRect(item.x, item.y, item.width, item.height, 99.0f,
                            Theme.withAlpha(Theme.FILL_2, item.hover * navOpacity));
                }

                int labelColor = item.hover > 0.5f ? Theme.TEXT_PRIMARY : Theme.TEXT_SECONDARY;
                AuraRenderer.text(item.label, item.x + item.width * 0.5f, item.y + item.height * 0.5f + 6.0f,
                        15.0f, Theme.withAlpha(labelColor, navOpacity), NanoVG.NVG_ALIGN_CENTER);
            }
        }

        // Right side: "Играть" pill button
        playW = 150.0f;
        playH = 52.0f;
        playX = islandX + islandW - playW - 12.0f;
        playY = islandY + (islandH - playH) * 0.5f;

        boolean isPlayHovered = realMouseX >= playX && realMouseX <= playX + playW
                && realMouseY >= playY && realMouseY <= playY + playH;

        playButtonHover = Anim.lerp(playButtonHover, isPlayHovered ? 1.0f : 0.0f, dt * 10.0f);

        int playBg = playButtonHover > 0.5f ? Theme.ACCENT_HOVER : Theme.ACCENT;
        AuraRenderer.roundRect(playX, playY, playW, playH, 99.0f, playBg);
        AuraRenderer.shadow(playX, playY, playW, playH, 99.0f, 16.0f, 0.0f, Theme.rgba(100, 210, 255, 0.35f));

        // Triangle icon + "Играть" text (#00283A)
        float contentCenterX = playX + playW * 0.5f;
        float contentCenterY = playY + playH * 0.5f;

        AuraRenderer.triangle(
                contentCenterX - 36.0f, contentCenterY - 7.0f,
                contentCenterX - 36.0f, contentCenterY + 7.0f,
                contentCenterX - 24.0f, contentCenterY,
                Theme.ACCENT_INK
        );

        AuraRenderer.text("Играть", contentCenterX + 6.0f, contentCenterY + 6.0f, 17.0f,
                Theme.ACCENT_INK, NanoVG.NVG_ALIGN_CENTER);

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

        // Check Play button click
        if (realMouseX >= playX && realMouseX <= playX + playW
                && realMouseY >= playY && realMouseY <= playY + playH) {
            if (client != null) client.setScreen(new SelectWorldScreen(this));
            return true;
        }

        // If expanded, check nav items
        if (expandProgress > 0.3f) {
            for (NavItem item : navItems) {
                if (realMouseX >= item.x && realMouseX <= item.x + item.width
                        && realMouseY >= item.y && realMouseY <= item.y + item.height) {
                    item.action.run();
                    return true;
                }
            }
        }

        return super.mouseClicked(mouseX, mouseY, button);
    }
}

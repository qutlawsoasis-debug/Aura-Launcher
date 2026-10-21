package com.auramc.auraui.menu;

import com.auramc.auraui.AuraUi;
import com.cinemamod.mcef.MCEF;
import com.cinemamod.mcef.MCEFBrowser;
import com.cinemamod.mcef.MCEFRenderer;
import com.mojang.blaze3d.systems.RenderSystem;
import java.net.URI;
import net.fabricmc.loader.api.FabricLoader;
import net.fabricmc.loader.api.ModContainer;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.ConnectScreen;
import net.minecraft.client.gui.screen.MessageScreen;
import net.minecraft.client.gui.screen.OpenToLanScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.option.OptionsScreen;
import net.minecraft.client.gui.screen.world.SelectWorldScreen;
import net.minecraft.client.network.ServerAddress;
import net.minecraft.client.network.ServerInfo;
import net.minecraft.client.option.ServerList;
import net.minecraft.client.render.BufferBuilder;
import net.minecraft.client.render.GameRenderer;
import net.minecraft.client.render.Tessellator;
import net.minecraft.client.render.VertexFormat;
import net.minecraft.client.render.VertexFormats;
import net.minecraft.text.Text;
import org.cef.browser.CefBrowser;
import org.cef.browser.CefFrame;
import org.cef.browser.CefMessageRouter;
import org.cef.callback.CefQueryCallback;
import org.cef.handler.CefLoadHandlerAdapter;
import org.cef.handler.CefMessageRouterHandlerAdapter;
import org.lwjgl.glfw.GLFW;

public class AuraWebScreen extends Screen {
    private final String pageName;
    private final boolean isPauseMenu;
    private MCEFBrowser browser;
    private CefMessageRouter router;
    private CefMessageRouterHandlerAdapter queryHandler;
    private int lastFbWidth = -1;
    private int lastFbHeight = -1;

    public AuraWebScreen(String pageName, boolean isPauseMenu) {
        super(Text.literal(pageName));
        this.pageName = pageName;
        this.isPauseMenu = isPauseMenu;
    }

    public static boolean isSupported() {
        try {
            return MCEF.isInitialized();
        } catch (Throwable t) {
            return false;
        }
    }

    @Override
    protected void init() {
        super.init();
        if (!isSupported()) {
            AuraUi.LOGGER.warn("[aura-ui] MCEF is not initialized during AuraWebScreen.init()");
            return;
        }

        if (browser == null) {
            String url = WebAssetManager.getFileUrl(pageName);
            AuraUi.LOGGER.info("[aura-ui] Creating MCEFBrowser for {}", url);
            // transparent = true
            browser = MCEF.createBrowser(url, true);

            // Ignore cursor changes to prevent GLFW error 65539 on virtual desktops
            browser.setCursorChangeListener(cursor -> {});

            // Register cefQuery router for JS -> Java commands
            router = CefMessageRouter.create();
            queryHandler = new CefMessageRouterHandlerAdapter() {
                @Override
                public boolean onQuery(CefBrowser b, CefFrame frame, long queryId, String request, boolean persistent, CefQueryCallback callback) {
                    AuraUi.LOGGER.info("[aura-ui] cefQuery request: {}", request);
                    if (client != null) {
                        client.execute(() -> handleAction(request));
                    }
                    callback.success("");
                    return true;
                }
            };
            router.addHandler(queryHandler, true);
            browser.getClient().addMessageRouter(router);

            // Send player info and build version on load end (main frame only)
            browser.getClient().addLoadHandler(new CefLoadHandlerAdapter() {
                @Override
                public void onLoadEnd(CefBrowser b, CefFrame frame, int httpStatusCode) {
                    if (frame.isMain()) {
                        sendPlayerInfo();
                    }
                }
            });

            resizeBrowser();
        } else {
            resizeBrowser();
        }
    }

    private void sendPlayerInfo() {
        if (client == null || browser == null) return;
        String username = client.getSession() != null ? client.getSession().getUsername() : "Player";
        String modVersion = FabricLoader.getInstance()
            .getModContainer("aura-ui")
            .map(ModContainer::getMetadata)
            .map(meta -> meta.getVersion().getFriendlyString())
            .orElse("1.0.0");
        String versionText = "Minecraft 1.20.1 Fabric • v" + modVersion;

        String js = String.format(
            "if (window.setPlayerInfo) { window.setPlayerInfo('%s', '%s'); }",
            escapeJs(username), escapeJs(versionText)
        );
        browser.executeJavaScript(js, browser.getURL(), 0);
    }

    private static String escapeJs(String text) {
        if (text == null) return "";
        return text.replace("\\", "\\\\").replace("'", "\\'").replace("\n", "\\n").replace("\r", "");
    }

    private void handleAction(String action) {
        if (client == null) return;

        switch (action) {
            case "play_server" -> {
                ServerList serverList = new ServerList(client);
                serverList.loadFile();
                if (serverList.size() > 0) {
                    ServerInfo serverInfo = serverList.get(0);
                    ServerAddress address = ServerAddress.parse(serverInfo.address);
                    ConnectScreen.connect(this, client, address, serverInfo, false);
                } else {
                    AuraUi.LOGGER.warn("[aura-ui] No servers in servers.dat, opening multiplayer screen");
                    client.setScreen(new net.minecraft.client.gui.screen.multiplayer.MultiplayerScreen(this));
                }
            }
            case "singleplayer" -> {
                client.setScreen(new SelectWorldScreen(this));
            }
            case "settings" -> {
                client.setScreen(new OptionsScreen(this, client.options));
            }
            case "open_lan" -> {
                client.setScreen(new OpenToLanScreen(this));
            }
            case "resume" -> {
                this.close();
            }
            case "main_menu" -> {
                boolean singlePlayer = client.isInSingleplayer();
                if (client.world != null) {
                    client.world.disconnect();
                }
                if (singlePlayer) {
                    client.disconnect(new MessageScreen(Text.translatable("menu.savingLevel")));
                } else {
                    client.disconnect();
                }
                client.setScreen(new TitleScreen());
            }
            case "quit" -> {
                client.scheduleStop();
            }
            case "friends", "invite" -> {
                AuraUi.LOGGER.info("[aura-ui] Action {} clicked (placeholder)", action);
            }
            default -> {
                AuraUi.LOGGER.warn("[aura-ui] Unknown action: {}", action);
            }
        }
    }

    private int mouseX(double x) {
        if (client == null) return (int) x;
        return (int) (x * client.getWindow().getScaleFactor());
    }

    private int mouseY(double y) {
        if (client == null) return (int) y;
        return (int) (y * client.getWindow().getScaleFactor());
    }

    private void resizeBrowser() {
        if (client == null || browser == null) return;
        int fbWidth = client.getWindow().getFramebufferWidth();
        int fbHeight = client.getWindow().getFramebufferHeight();
        if (fbWidth > 0 && fbHeight > 0 && (fbWidth != lastFbWidth || fbHeight != lastFbHeight)) {
            browser.resize(fbWidth, fbHeight);
            lastFbWidth = fbWidth;
            lastFbHeight = fbHeight;
        }
    }

    @Override
    public void resize(MinecraftClient client, int width, int height) {
        super.resize(client, width, height);
        resizeBrowser();
    }

    @Override
    public void close() {
        if (isPauseMenu) {
            if (client != null) {
                client.setScreen(null);
            }
        } else {
            super.close();
        }
    }

    @Override
    public void removed() {
        if (browser != null) {
            if (router != null && queryHandler != null) {
                router.removeHandler(queryHandler);
                browser.getClient().removeMessageRouter(router);
                router = null;
                queryHandler = null;
            }
            browser.close();
            browser = null;
        }
        super.removed();
    }

    @Override
    public boolean shouldCloseOnEsc() {
        return isPauseMenu;
    }

    @Override
    public boolean keyPressed(int keyCode, int scanCode, int modifiers) {
        if (keyCode == GLFW.GLFW_KEY_ESCAPE && shouldCloseOnEsc()) {
            this.close();
            return true;
        }
        if (browser != null) {
            browser.sendKeyPress(keyCode, scanCode, modifiers);
            browser.setFocus(true);
        }
        return super.keyPressed(keyCode, scanCode, modifiers);
    }

    @Override
    public boolean keyReleased(int keyCode, int scanCode, int modifiers) {
        if (browser != null) {
            browser.sendKeyRelease(keyCode, scanCode, modifiers);
            browser.setFocus(true);
        }
        return super.keyReleased(keyCode, scanCode, modifiers);
    }

    @Override
    public boolean charTyped(char chr, int modifiers) {
        if (chr == 0) return false;
        if (browser != null) {
            browser.sendKeyTyped(chr, modifiers);
            browser.setFocus(true);
        }
        return super.charTyped(chr, modifiers);
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (browser != null) {
            browser.sendMousePress(mouseX(mouseX), mouseY(mouseY), button);
            browser.setFocus(true);
        }
        return super.mouseClicked(mouseX, mouseY, button);
    }

    @Override
    public boolean mouseReleased(double mouseX, double mouseY, int button) {
        if (browser != null) {
            browser.sendMouseRelease(mouseX(mouseX), mouseY(mouseY), button);
            browser.setFocus(true);
        }
        return super.mouseReleased(mouseX, mouseY, button);
    }

    @Override
    public void mouseMoved(double mouseX, double mouseY) {
        if (browser != null) {
            browser.sendMouseMove(mouseX(mouseX), mouseY(mouseY));
        }
        super.mouseMoved(mouseX, mouseY);
    }

    @Override
    public boolean mouseScrolled(double mouseX, double mouseY, double amount) {
        if (browser != null) {
            browser.sendMouseWheel(mouseX(mouseX), mouseY(mouseY), amount, 0);
        }
        return super.mouseScrolled(mouseX, mouseY, amount);
    }

    @Override
    public void render(DrawContext context, int mouseX, int mouseY, float delta) {
        resizeBrowser();

        if (browser != null) {
            MCEFRenderer renderer = browser.getRenderer();
            if (renderer != null) {
                int texId = renderer.getTextureID();
                if (texId > 0) {
                    RenderSystem.disableDepthTest();
                    RenderSystem.enableBlend();
                    RenderSystem.defaultBlendFunc();
                    RenderSystem.setShader(GameRenderer::getPositionTexColorProgram);
                    RenderSystem.setShaderTexture(0, texId);

                    Tessellator tessellator = Tessellator.getInstance();
                    BufferBuilder buffer = tessellator.getBuffer();
                    buffer.begin(VertexFormat.DrawMode.QUADS, VertexFormats.POSITION_TEXTURE_COLOR);

                    // Draw full screen: 0,0 to width,height
                    buffer.vertex(0.0, this.height, 0.0).texture(0.0f, 1.0f).color(255, 255, 255, 255).next();
                    buffer.vertex(this.width, this.height, 0.0).texture(1.0f, 1.0f).color(255, 255, 255, 255).next();
                    buffer.vertex(this.width, 0.0, 0.0).texture(1.0f, 0.0f).color(255, 255, 255, 255).next();
                    buffer.vertex(0.0, 0.0, 0.0).texture(0.0f, 0.0f).color(255, 255, 255, 255).next();

                    tessellator.draw();
                    RenderSystem.setShaderTexture(0, 0);
                    RenderSystem.enableDepthTest();
                }
            }
        }
        super.render(context, mouseX, mouseY, delta);
    }
}

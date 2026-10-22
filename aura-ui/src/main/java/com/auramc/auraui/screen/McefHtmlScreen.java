package com.auramc.auraui.screen;

import com.auramc.auraui.AuraUi;
import com.cinemamod.mcef.MCEF;
import com.cinemamod.mcef.MCEFBrowser;
import com.cinemamod.mcef.MCEFRenderer;
import com.mojang.blaze3d.systems.RenderSystem;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gl.PostEffectProcessor;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.MessageScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.render.BufferBuilder;
import net.minecraft.client.render.GameRenderer;
import net.minecraft.client.render.Tessellator;
import net.minecraft.client.render.VertexFormat;
import net.minecraft.client.render.VertexFormats;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
import org.cef.browser.CefBrowser;
import org.cef.browser.CefFrame;
import org.cef.browser.CefMessageRouter;
import org.cef.callback.CefQueryCallback;
import org.cef.handler.CefMessageRouterHandlerAdapter;

/**
 * Пауза на HTML: страница рендерится Chromium (MCEF) в размер кадра 1:1,
 * под ней мир размывается штатным пост-эффектом Minecraft.
 */
public class McefHtmlScreen extends Screen {
    private static final Identifier BLUR = new Identifier("shaders/post/blur.json");

    private final String url;
    private MCEFBrowser browser;
    private CefMessageRouter router;
    private CefMessageRouterHandlerAdapter queryHandler;
    private PostEffectProcessor blur;
    private int fbW = -1, fbH = -1;

    public McefHtmlScreen(String url) {
        super(Text.literal("Aura"));
        this.url = url;
    }

    @Override
    protected void init() {
        if (!MCEF.isInitialized()) {
            AuraUi.LOGGER.warn("[aura-ui] MCEF not initialized, falling back to native pause");
            client.setScreen(new AuraPauseScreen());
            return;
        }
        if (browser == null) {
            AuraUi.LOGGER.info("[aura-ui] HTML pause: {}", url);
            browser = MCEF.createBrowser(url, true);
            browser.setCursorChangeListener(cursor -> {});
            router = CefMessageRouter.create();
            queryHandler = new CefMessageRouterHandlerAdapter() {
                @Override
                public boolean onQuery(CefBrowser b, CefFrame frame, long queryId, String request, boolean persistent, CefQueryCallback callback) {
                    if (client != null) client.execute(() -> handleAction(request));
                    callback.success("");
                    return true;
                }
            };
            router.addHandler(queryHandler, true);
            browser.getClient().addMessageRouter(router);
        }
        resizeAll();
    }

    private void handleAction(String action) {
        AuraUi.LOGGER.info("[aura-ui] HTML action: {}", action);
        switch (action) {
            case "resume" -> client.setScreen(null);
            case "main_menu" -> {
                boolean single = client.isInSingleplayer();
                if (client.world != null) client.world.disconnect();
                if (single) client.disconnect(new MessageScreen(Text.translatable("menu.savingLevel")));
                else client.disconnect();
                client.setScreen(new TitleScreen());
            }
            default -> AuraUi.LOGGER.info("[aura-ui] HTML action not wired yet: {}", action);
        }
    }

    private void resizeAll() {
        int w = client.getWindow().getFramebufferWidth();
        int h = client.getWindow().getFramebufferHeight();
        if (w <= 0 || h <= 0 || (w == fbW && h == fbH)) return;
        fbW = w;
        fbH = h;
        if (browser != null) browser.resize(w, h);
        try {
            if (blur == null) {
                blur = new PostEffectProcessor(client.getTextureManager(), client.getResourceManager(), client.getFramebuffer(), BLUR);
            }
            blur.setupDimensions(w, h);
        } catch (Exception e) {
            AuraUi.LOGGER.warn("[aura-ui] Blur post effect unavailable", e);
            blur = null;
        }
    }

    // Координаты GUI → пиксели кадра (страница отрисована 1:1 с кадром)
    private int px(double v) {
        return (int) Math.round(v * client.getWindow().getScaleFactor());
    }

    @Override
    public void renderBackground(DrawContext context) {
        // Своё размытие вместо ванильного затемнения
    }

    @Override
    public void render(DrawContext context, int mouseX, int mouseY, float delta) {
        resizeAll();
        if (blur != null) {
            blur.render(delta);
            client.getFramebuffer().beginWrite(false);
        }
        MCEFRenderer renderer = browser != null ? browser.getRenderer() : null;
        int tex = renderer != null ? renderer.getTextureID() : 0;
        if (tex > 0) {
            RenderSystem.disableDepthTest();
            RenderSystem.enableBlend();
            // Chromium отдаёт премультиплицированную альфу
            RenderSystem.blendFunc(com.mojang.blaze3d.platform.GlStateManager.SrcFactor.ONE,
                com.mojang.blaze3d.platform.GlStateManager.DstFactor.ONE_MINUS_SRC_ALPHA);
            RenderSystem.setShader(GameRenderer::getPositionTexColorProgram);
            RenderSystem.setShaderTexture(0, tex);
            BufferBuilder buf = Tessellator.getInstance().getBuffer();
            buf.begin(VertexFormat.DrawMode.QUADS, VertexFormats.POSITION_TEXTURE_COLOR);
            buf.vertex(0, height, 0).texture(0, 1).color(255, 255, 255, 255).next();
            buf.vertex(width, height, 0).texture(1, 1).color(255, 255, 255, 255).next();
            buf.vertex(width, 0, 0).texture(1, 0).color(255, 255, 255, 255).next();
            buf.vertex(0, 0, 0).texture(0, 0).color(255, 255, 255, 255).next();
            Tessellator.getInstance().draw();
            RenderSystem.setShaderTexture(0, 0);
            RenderSystem.defaultBlendFunc();
            RenderSystem.enableDepthTest();
        }
        if (Boolean.getBoolean("aura.html.autoshot")) autoshot();
    }

    // Сценарий автопроверки: кадр ESC → открыть настройки → кадр → выход из игры
    private long openedAt = 0;
    private int shotStep = 0;

    private void autoshot() {
        if (openedAt == 0) openedAt = System.currentTimeMillis();
        long t = System.currentTimeMillis() - openedAt;
        if (shotStep == 0 && t > 3500) { shot("html_esc"); shotStep++; }
        else if (shotStep == 1 && t > 7000) { browser.executeJavaScript("setView('settings')", browser.getURL(), 0); shotStep++; }
        else if (shotStep == 2 && t > 10000) { shot("html_settings"); shotStep++; }
        else if (shotStep == 3 && t > 10500) {
            browser.executeJavaScript("document.querySelector('.nav-item[data-sec=\"ui\"]').click()", browser.getURL(), 0); shotStep++;
        }
        else if (shotStep == 4 && t > 13000) { shot("html_settings_ui"); shotStep++; }
        else if (shotStep == 5 && t > 14000) { client.scheduleStop(); shotStep++; }
    }

    private void shot(String name) {
        AuraUi.LOGGER.info("[aura-ui] AUTOSHOT {} fps={} fb={}x{} gui={}", name, client.getCurrentFps(), fbW, fbH, client.getWindow().getScaleFactor());
        net.minecraft.client.util.ScreenshotRecorder.saveScreenshot(client.runDirectory, name + ".png", client.getFramebuffer(),
            msg -> AuraUi.LOGGER.info("[aura-ui] AUTOSHOT saved {}", msg.getString()));
    }

    @Override
    public boolean shouldPause() {
        return true;
    }

    // Esc обрабатывает страница: из настроек — назад, из паузы — cefQuery("resume")
    @Override
    public boolean shouldCloseOnEsc() {
        return false;
    }

    @Override
    public boolean keyPressed(int keyCode, int scanCode, int modifiers) {
        if (browser != null) {
            browser.sendKeyPress(keyCode, scanCode, modifiers);
            browser.setFocus(true);
        }
        return true;
    }

    @Override
    public boolean keyReleased(int keyCode, int scanCode, int modifiers) {
        if (browser != null) browser.sendKeyRelease(keyCode, scanCode, modifiers);
        return true;
    }

    @Override
    public boolean charTyped(char chr, int modifiers) {
        if (browser != null && chr != 0) browser.sendKeyTyped(chr, modifiers);
        return true;
    }

    @Override
    public boolean mouseClicked(double x, double y, int button) {
        if (browser != null) {
            browser.sendMousePress(px(x), px(y), button);
            browser.setFocus(true);
        }
        return true;
    }

    @Override
    public boolean mouseReleased(double x, double y, int button) {
        if (browser != null) browser.sendMouseRelease(px(x), px(y), button);
        return true;
    }

    @Override
    public boolean mouseDragged(double x, double y, int button, double dx, double dy) {
        if (browser != null) browser.sendMouseMove(px(x), px(y));
        return true;
    }

    @Override
    public void mouseMoved(double x, double y) {
        if (browser != null) browser.sendMouseMove(px(x), px(y));
    }

    @Override
    public boolean mouseScrolled(double x, double y, double amount) {
        if (browser != null) browser.sendMouseWheel(px(x), px(y), amount, 0);
        return true;
    }

    @Override
    public void removed() {
        if (browser != null) {
            if (router != null) {
                router.removeHandler(queryHandler);
                browser.getClient().removeMessageRouter(router);
            }
            browser.close();
            browser = null;
        }
        if (blur != null) {
            blur.close();
            blur = null;
        }
        MinecraftClient.getInstance().getFramebuffer().beginWrite(true);
    }
}

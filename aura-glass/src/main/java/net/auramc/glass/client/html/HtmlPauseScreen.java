package net.auramc.glass.client.html;

import com.mojang.blaze3d.platform.GlStateManager;
import com.mojang.blaze3d.systems.RenderSystem;
import com.sun.jna.Pointer;
import net.auramc.glass.common.render.BackdropRenderer;
import net.auramc.glass.common.render.GlassRenderer;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.MessageScreen;
import net.minecraft.client.gui.screen.OpenToLanScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.StatsScreen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.advancement.AdvancementsScreen;
import net.minecraft.client.gui.screen.option.OptionsScreen;
import net.minecraft.client.render.BufferBuilder;
import net.minecraft.client.render.BufferRenderer;
import net.minecraft.client.render.GameRenderer;
import net.minecraft.client.render.Tessellator;
import net.minecraft.client.render.VertexFormat;
import net.minecraft.client.render.VertexFormats;
import net.minecraft.text.Text;
import org.lwjgl.glfw.GLFW;
import org.lwjgl.opengl.GL11;
import org.lwjgl.opengl.GL12;
import org.lwjgl.opengl.GL15;
import org.lwjgl.opengl.GL21;

import java.io.File;
import java.nio.ByteBuffer;
import java.util.Locale;

public class HtmlPauseScreen extends Screen {
    private Pointer view;
    private int textureId;
    private int viewWidth;
    private int viewHeight;
    private boolean textureInitialized = false;
    private long lastStateUpdate;
    private UltralightCAPI.ConsoleCallback consoleCallback;
    private UltralightCAPI.WindowObjectReadyCallback windowObjectReadyCallback;
    private UltralightCAPI.DOMReadyCallback domReadyCallback;

    public HtmlPauseScreen() {
        super(Text.translatable("menu.game"));
    }

    @Override
    protected void init() {
        super.init();
        if (!UltralightNative.isAvailable()) {
            System.err.println("[Aura] Ultralight not available, fallback to default pause menu");
            return;
        }

        int fbW = client.getWindow().getFramebufferWidth();
        int fbH = client.getWindow().getFramebufferHeight();
        if (fbW <= 0) fbW = 1920;
        if (fbH <= 0) fbH = 1080;

        initView(fbW, fbH);
    }

    private void initView(int width, int height) {
        UltralightCAPI ul = UltralightNative.ul();
        if (ul == null) return;

        if (view == null || viewWidth != width || viewHeight != height) {
            if (view != null) {
                UltralightNative.destroyView(view);
            }
            viewWidth = width;
            viewHeight = height;
            textureInitialized = false;
            view = UltralightNative.createView(width, height, 1.0);
            if (view == null) return;

            consoleCallback = (userData, caller, source, level, message, lineNumber, columnNumber, sourceId) -> {
                String msg = ul.ulStringGetData(message);
                if (msg != null) {
                    System.out.println("[AuraJS] " + msg);
                    if (msg.startsWith("[aura-action]:")) {
                        String action = msg.substring("[aura-action]:".length()).trim();
                        client.execute(() -> handleAction(action));
                    }
                }
            };
            ul.ulViewSetAddConsoleMessageCallback(view, consoleCallback, null);

            windowObjectReadyCallback = (userData, caller, frameId, isMainFrame, url) -> {
                injectBridge();
            };
            ul.ulViewSetWindowObjectReadyCallback(view, windowObjectReadyCallback, null);

            domReadyCallback = (userData, caller, frameId, isMainFrame, url) -> {
                updateState(true);
            };
            ul.ulViewSetDOMReadyCallback(view, domReadyCallback, null);

            File htmlDir = UltralightNative.findHtmlDir();
            String htmlUrl = "file:///pause.html";
            if (htmlDir != null) {
                File htmlFile = new File(htmlDir, "pause.html");
                if (htmlFile.exists()) {
                    htmlUrl = htmlFile.toURI().toString();
                }
            }

            Pointer urlStr = ul.ulCreateString(htmlUrl);
            ul.ulViewLoadURL(view, urlStr);
            ul.ulDestroyString(urlStr);

            injectBridge();
            updateState(true);
        }

        if (textureId == 0) {
            textureId = GlStateManager._genTexture();
            GlStateManager._bindTexture(textureId);
            GlStateManager._texParameter(GL11.GL_TEXTURE_2D, GL11.GL_TEXTURE_MIN_FILTER, GL11.GL_LINEAR);
            GlStateManager._texParameter(GL11.GL_TEXTURE_2D, GL11.GL_TEXTURE_MAG_FILTER, GL11.GL_LINEAR);
            GlStateManager._texParameter(GL11.GL_TEXTURE_2D, GL11.GL_TEXTURE_WRAP_S, GL12.GL_CLAMP_TO_EDGE);
            GlStateManager._texParameter(GL11.GL_TEXTURE_2D, GL11.GL_TEXTURE_WRAP_T, GL12.GL_CLAMP_TO_EDGE);
        }
    }

    private void injectBridge() {
        if (view == null) return;
        UltralightCAPI ul = UltralightNative.ul();
        String js = "window.auraNative = true;\n" +
                "window.aura = window.aura || {};\n" +
                "window.aura.action = function(a) { console.log('[aura-action]:' + a); };\n" +
                "window.aura._state = null;\n" +
                "window.aura.state = function(s) {\n" +
                "  if (!s) return;\n" +
                "  window.aura._state = s;\n" +
                "  var p = document.getElementById('player');\n" +
                "  var w = document.getElementById('world');\n" +
                "  var f = document.getElementById('fps');\n" +
                "  if (p && s.player) p.textContent = s.player;\n" +
                "  if (w && s.world) w.textContent = s.world;\n" +
                "  if (f && s.fps != null) f.textContent = s.fps + ' FPS';\n" +
                "  console.log('[aura-bridge] state applied: player=' + (s.player||'') + ' world=' + (s.world||''));\n" +
                "};\n";
        Pointer jsStr = ul.ulCreateString(js);
        ul.ulViewEvaluateScript(view, jsStr, null);
        ul.ulDestroyString(jsStr);
    }

    private void updateState(boolean force) {
        if (view == null) return;
        long now = System.currentTimeMillis();
        if (!force && now - lastStateUpdate < 500) return;
        lastStateUpdate = now;

        UltralightCAPI ul = UltralightNative.ul();
        String player = (client != null && client.player != null) ? client.player.getName().getString() : "AuraReleaseQA";
        String world = (client != null && client.isInSingleplayer()) ?
                (client.getServer() != null ? client.getServer().getSaveProperties().getLevelName() : "Новый мир") :
                (client != null && client.getCurrentServerEntry() != null ? client.getCurrentServerEntry().name : "Сервер");
        int fps = (client != null) ? client.getCurrentFps() : 60;

        String js = String.format(Locale.ROOT,
                "if (window.aura && window.aura.state) window.aura.state({ player: '%s', world: '%s', fps: %d });",
                escapeJson(player), escapeJson(world), fps);
        Pointer jsStr = ul.ulCreateString(js);
        ul.ulViewEvaluateScript(view, jsStr, null);
        ul.ulDestroyString(jsStr);
    }

    private static String escapeJson(String s) {
        if (s == null) return "";
        return s.replace("\\", "\\\\").replace("'", "\\'").replace("\"", "\\\"").replace("\n", " ");
    }

    @Override
    public void render(DrawContext context, int mouseX, int mouseY, float delta) {
        // 1. Render Aura Glass background blur
        GlassRenderer.captureBackdrop(true);
        BackdropRenderer.modal(1.0f, true);

        // 2. Update and render Ultralight view
        if (view != null) {
            UltralightCAPI ul = UltralightNative.ul();
            updateState(false);
            ul.ulUpdate(UltralightNative.renderer());

            boolean needsPaint = ul.ulViewGetNeedsPaint(view);
            if (needsPaint) {
                ul.ulRender(UltralightNative.renderer());
            }

            // 3. Upload bitmap to OpenGL texture (only dirty rectangle or full on first frame)
            Pointer surface = ul.ulViewGetSurface(view);
            if (surface != null) {
                UltralightCAPI.ULIntRect.ByValue dirty = ul.ulSurfaceGetDirtyBounds(surface);
                int dw = dirty.right - dirty.left;
                int dh = dirty.bottom - dirty.top;

                // Ensure no PBO is bound to GL_PIXEL_UNPACK_BUFFER (e.g. from Sodium/ImmediatelyFast)
                GL15.glBindBuffer(GL21.GL_PIXEL_UNPACK_BUFFER, 0);
                GL11.glPixelStorei(GL11.GL_UNPACK_ALIGNMENT, 4);
                GL11.glPixelStorei(GL11.GL_UNPACK_ROW_LENGTH, 0);
                GL11.glPixelStorei(GL12.GL_UNPACK_SKIP_PIXELS, 0);
                GL11.glPixelStorei(GL12.GL_UNPACK_SKIP_ROWS, 0);

                if (!textureInitialized) {
                    Pointer bitmap = ul.ulBitmapSurfaceGetBitmap(surface);
                    if (bitmap != null) {
                        int bw = ul.ulBitmapGetWidth(bitmap);
                        int bh = ul.ulBitmapGetHeight(bitmap);
                        int stride = ul.ulBitmapGetRowBytes(bitmap);
                        Pointer pixels = ul.ulBitmapLockPixels(bitmap);
                        if (pixels != null) {
                            ByteBuffer buf = pixels.getByteBuffer(0, (long) stride * bh);
                            GlStateManager._bindTexture(textureId);
                            GL11.glPixelStorei(GL11.GL_UNPACK_ROW_LENGTH, stride / 4);
                            GL11.glTexImage2D(GL11.GL_TEXTURE_2D, 0, GL11.GL_RGBA8, bw, bh, 0, GL12.GL_BGRA, GL11.GL_UNSIGNED_BYTE, buf);
                            GL11.glPixelStorei(GL11.GL_UNPACK_ROW_LENGTH, 0);
                            ul.ulBitmapUnlockPixels(bitmap);
                            textureInitialized = true;
                            ul.ulSurfaceClearDirtyBounds(surface);
                        }
                    }
                } else if (dw > 0 && dh > 0 && dirty.left >= 0 && dirty.top >= 0) {
                    Pointer bitmap = ul.ulBitmapSurfaceGetBitmap(surface);
                    if (bitmap != null) {
                        int bw = ul.ulBitmapGetWidth(bitmap);
                        int bh = ul.ulBitmapGetHeight(bitmap);
                        int stride = ul.ulBitmapGetRowBytes(bitmap);
                        Pointer pixels = ul.ulBitmapLockPixels(bitmap);
                        if (pixels != null) {
                            int x = Math.max(0, dirty.left);
                            int y = Math.max(0, dirty.top);
                            int w = Math.min(dw, bw - x);
                            int h = Math.min(dh, bh - y);

                            if (w > 0 && h > 0) {
                                ByteBuffer buf = pixels.getByteBuffer(0, (long) stride * bh);
                                GlStateManager._bindTexture(textureId);

                                GL11.glPixelStorei(GL11.GL_UNPACK_ROW_LENGTH, stride / 4);
                                GL11.glPixelStorei(GL12.GL_UNPACK_SKIP_PIXELS, x);
                                GL11.glPixelStorei(GL12.GL_UNPACK_SKIP_ROWS, y);

                                GL11.glTexSubImage2D(GL11.GL_TEXTURE_2D, 0, x, y, w, h, GL12.GL_BGRA, GL11.GL_UNSIGNED_BYTE, buf);

                                GL11.glPixelStorei(GL11.GL_UNPACK_ROW_LENGTH, 0);
                                GL11.glPixelStorei(GL12.GL_UNPACK_SKIP_PIXELS, 0);
                                GL11.glPixelStorei(GL12.GL_UNPACK_SKIP_ROWS, 0);
                            }
                            ul.ulBitmapUnlockPixels(bitmap);
                            ul.ulSurfaceClearDirtyBounds(surface);
                        }
                    }
                }
            }

            // 4. Render HTML texture 1:1 onto screen quad with premultiplied alpha
            drawHtmlOverlay(context);
        }

        super.render(context, mouseX, mouseY, delta);
        HtmlPauseBenchmark.onRenderFrame(client);
    }

    @Override
    public boolean shouldPause() {
        return true;
    }

    private void drawHtmlOverlay(DrawContext context) {
        if (textureId == 0) return;
        context.draw(); // Flush previous 2D batched draws

        RenderSystem.enableBlend();
        RenderSystem.blendFuncSeparate(
                GlStateManager.SrcFactor.ONE,
                GlStateManager.DstFactor.ONE_MINUS_SRC_ALPHA,
                GlStateManager.SrcFactor.ONE,
                GlStateManager.DstFactor.ONE_MINUS_SRC_ALPHA
        );
        RenderSystem.setShader(GameRenderer::getPositionTexProgram);
        RenderSystem.setShaderTexture(0, textureId);
        RenderSystem.setShaderColor(1.0f, 1.0f, 1.0f, 1.0f);

        Tessellator tessellator = Tessellator.getInstance();
        BufferBuilder buffer = tessellator.getBuffer();
        buffer.begin(VertexFormat.DrawMode.QUADS, VertexFormats.POSITION_TEXTURE);
        buffer.vertex(0.0, height, 0.0).texture(0.0f, 1.0f).next();
        buffer.vertex(width, height, 0.0).texture(1.0f, 1.0f).next();
        buffer.vertex(width, 0.0, 0.0).texture(1.0f, 0.0f).next();
        buffer.vertex(0.0, 0.0, 0.0).texture(0.0f, 0.0f).next();
        BufferRenderer.drawWithGlobalProgram(buffer.end());

        RenderSystem.defaultBlendFunc();
    }

    @Override
    public void mouseMoved(double mouseX, double mouseY) {
        super.mouseMoved(mouseX, mouseY);
        if (view != null && client != null) {
            UltralightCAPI ul = UltralightNative.ul();
            int px = (int) Math.round(mouseX * client.getWindow().getScaleFactor());
            int py = (int) Math.round(mouseY * client.getWindow().getScaleFactor());
            Pointer evt = ul.ulCreateMouseEvent(0 /* kMouseEventType_MouseMoved */, px, py, 0 /* kMouseButton_None */);
            ul.ulViewFireMouseEvent(view, evt);
            ul.ulDestroyMouseEvent(evt);
        }
    }

    @Override
    public boolean mouseClicked(double mouseX, double mouseY, int button) {
        if (view != null && client != null) {
            UltralightCAPI ul = UltralightNative.ul();
            int px = (int) Math.round(mouseX * client.getWindow().getScaleFactor());
            int py = (int) Math.round(mouseY * client.getWindow().getScaleFactor());
            int btn = (button == 0) ? 1 : (button == 1 ? 3 : 2); // 1=Left, 3=Right, 2=Middle
            Pointer evt = ul.ulCreateMouseEvent(1 /* kMouseEventType_MouseDown */, px, py, btn);
            ul.ulViewFireMouseEvent(view, evt);
            ul.ulDestroyMouseEvent(evt);
            return true;
        }
        return super.mouseClicked(mouseX, mouseY, button);
    }

    @Override
    public boolean mouseReleased(double mouseX, double mouseY, int button) {
        if (view != null && client != null) {
            UltralightCAPI ul = UltralightNative.ul();
            int px = (int) Math.round(mouseX * client.getWindow().getScaleFactor());
            int py = (int) Math.round(mouseY * client.getWindow().getScaleFactor());
            int btn = (button == 0) ? 1 : (button == 1 ? 3 : 2);
            Pointer evt = ul.ulCreateMouseEvent(2 /* kMouseEventType_MouseUp */, px, py, btn);
            ul.ulViewFireMouseEvent(view, evt);
            ul.ulDestroyMouseEvent(evt);
            return true;
        }
        return super.mouseReleased(mouseX, mouseY, button);
    }

    @Override
    public boolean mouseScrolled(double mouseX, double mouseY, double amount) {
        if (view != null) {
            UltralightCAPI ul = UltralightNative.ul();
            Pointer evt = ul.ulCreateScrollEvent(0, 0, (int) Math.round(amount * 120));
            ul.ulViewFireScrollEvent(view, evt);
            ul.ulDestroyScrollEvent(evt);
            return true;
        }
        return super.mouseScrolled(mouseX, mouseY, amount);
    }

    @Override
    public boolean keyPressed(int keyCode, int scanCode, int modifiers) {
        if (keyCode == GLFW.GLFW_KEY_ESCAPE) {
            handleAction("resume");
            return true;
        }
        if (view != null) {
            UltralightCAPI ul = UltralightNative.ul();
            Pointer evt = ul.ulCreateKeyEvent(2 /* kKeyEventType_RawKeyDown */, modifiers, keyCode, scanCode, null, null, false, false, false);
            ul.ulViewFireKeyEvent(view, evt);
            ul.ulDestroyKeyEvent(evt);
            return true;
        }
        return super.keyPressed(keyCode, scanCode, modifiers);
    }

    @Override
    public boolean charTyped(char chr, int modifiers) {
        if (view != null) {
            UltralightCAPI ul = UltralightNative.ul();
            Pointer str = ul.ulCreateString(String.valueOf(chr));
            Pointer evt = ul.ulCreateKeyEvent(3 /* kKeyEventType_Char */, modifiers, chr, chr, str, str, false, false, false);
            ul.ulViewFireKeyEvent(view, evt);
            ul.ulDestroyKeyEvent(evt);
            ul.ulDestroyString(str);
            return true;
        }
        return super.charTyped(chr, modifiers);
    }

    public void handleAction(String action) {
        if (client == null) return;
        switch (action) {
            case "resume":
                client.setScreen(null);
                break;
            case "options":
                client.setScreen(new OptionsScreen(this, client.options));
                break;
            case "advancements":
                if (client.player != null) {
                    client.setScreen(new AdvancementsScreen(client.player.networkHandler.getAdvancementHandler()));
                }
                break;
            case "stats":
                if (client.player != null) {
                    client.setScreen(new StatsScreen(this, client.player.getStatHandler()));
                }
                break;
            case "mods":
                try {
                    Class<?> modMenu = Class.forName("com.terraformersmc.modmenu.gui.ModsScreen");
                    Screen s = (Screen) modMenu.getConstructor(Screen.class).newInstance(this);
                    client.setScreen(s);
                } catch (Throwable t) {
                    client.setScreen(new OptionsScreen(this, client.options));
                }
                break;
            case "lan":
                client.setScreen(new OpenToLanScreen(this));
                break;
            case "quit":
                boolean singleplayer = client.isInSingleplayer();
                if (client.world != null) {
                    client.world.disconnect();
                }
                if (singleplayer) {
                    client.disconnect(new MessageScreen(Text.translatable("menu.savingLevel")));
                } else {
                    client.disconnect();
                }
                client.setScreen(new TitleScreen());
                break;
            default:
                System.out.println("[Aura] Unknown HTML pause action: " + action);
                break;
        }
    }

    @Override
    public void close() {
        if (view != null) {
            UltralightNative.destroyView(view);
            view = null;
        }
        if (textureId != 0) {
            GlStateManager._deleteTexture(textureId);
            textureId = 0;
        }
        super.close();
    }
}

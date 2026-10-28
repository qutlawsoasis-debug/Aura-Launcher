package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.GlassDiagnostics;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassPreset;
import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.GameMenuScreen;
import net.minecraft.client.gui.screen.TitleScreen;

public final class GlassFrameHooks {
    private GlassFrameHooks() {}
    private static net.auramc.glass.common.ui.AuraScreenTransition modalTransition;
    public static boolean modalScreen() {
        var c=MinecraftClient.getInstance(); var screen=c.currentScreen;
        if(c.world==null || screen==null || screen instanceof TitleScreen || screen instanceof GlassGalleryView)return false;
        String name=screen.getClass().getName();
        return name.startsWith("net.minecraft.") || name.startsWith("net.auramc.glass.");
    }
    public static void beginFrame() { if(!modalScreen())modalTransition=null; }
    public static void beforeScreen(DrawContext context) {
        var client=MinecraftClient.getInstance();
        if(!styledScreen() || client.currentScreen instanceof TitleScreen)return;
        if(client.currentScreen instanceof net.minecraft.client.gui.screen.ChatScreen){context.draw();GlassRenderer.captureBackdrop();return;}
        if(client.world==null) {
            AuraUiRenderer.renderMenuPanoramaBackground(context,client,client.getWindow().getScaledWidth(),client.getWindow().getScaledHeight());
            context.draw();GlassRenderer.captureBackdrop(true);GlassRenderer.modalBackground(1);return;
        }
        if(modalTransition==null)modalTransition=new net.auramc.glass.common.ui.AuraScreenTransition();
        context.draw();GlassRenderer.captureBackdrop(true);GlassRenderer.modalBackground(modalTransition.getProgress());
    }
    public static boolean styledScreen() {
        var screen = MinecraftClient.getInstance().currentScreen;
        if(screen==null || screen instanceof GlassGalleryView || screen instanceof net.minecraft.client.gui.screen.ingame.HandledScreen<?>)return false;
        String name=screen.getClass().getName();
        return name.startsWith("net.minecraft.client.gui.screen.");
    }

    public static void afterBackground(DrawContext context) {
        var screen = MinecraftClient.getInstance().currentScreen;
        if (screen == null) return;
        String name = screen.getClass().getName();
        if (!name.startsWith("net.minecraft.") && !name.startsWith("net.auramc.glass.")) return;
        if (!styledScreen() && !GlassDiagnostics.panelEnabled()) return;
        context.draw();
        GlassRenderer.captureBackdrop();
    }

    public static void finish(DrawContext context) {
        var client = MinecraftClient.getInstance();
        if (GlassDiagnostics.panelEnabled()) {
            float w = Math.min(300, client.getWindow().getScaledWidth() - 16);
            float h = Math.min(180, client.getWindow().getScaledHeight() - 16);
            float x = (client.getWindow().getScaledWidth() - w) / 2f;
            float y = (client.getWindow().getScaledHeight() - h) / 2f;
            var style = GlassStyles.get(GlassPreset.PANEL);
            style.refraction = .9f; style.bevel = 40; style.frost = .35f;
            style.rimOuter = 1.2f; style.light = .9f; style.dispersion = .12f;
            GlassRenderer.draw(x, y, w, h, style, 1);
            GlassRenderer.foreground(() -> context.drawText(client.textRenderer,
                    "F6: " + GlassRenderer.effectiveQuality() + (GlassRenderer.hasBackdrop() ? " GLASS" : " LOW"), (int) x + 8, (int) y + 8, 0xFFFFFFFF, false));
        }
        context.draw();
        GlassRenderer.flush();
        context.draw();
    }
}

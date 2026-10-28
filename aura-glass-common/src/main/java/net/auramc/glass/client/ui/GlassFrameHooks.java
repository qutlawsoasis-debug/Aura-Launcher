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
    private static Object modalScreen;
    private static long modalStarted;
    private static int nativeContentDepth;
    private static final java.util.Set<String> CARDS = cardScreens();
    private static java.util.Set<String> cardScreens() {
        var cards = new java.util.HashSet<String>(java.util.Set.of(
                net.minecraft.client.gui.screen.ProgressScreen.class.getName(),
                net.minecraft.client.gui.screen.DownloadingTerrainScreen.class.getName(),
                net.minecraft.client.gui.screen.MessageScreen.class.getName(),
                net.minecraft.client.gui.screen.DisconnectedScreen.class.getName(),
                net.minecraft.client.gui.screen.ConfirmScreen.class.getName(),
                net.minecraft.client.gui.screen.ConfirmLinkScreen.class.getName(),
                net.minecraft.client.gui.screen.NoticeScreen.class.getName(),
                net.minecraft.client.gui.screen.world.OptimizeWorldScreen.class.getName()));
        var mappings = net.fabricmc.loader.api.FabricLoader.getInstance().getMappingResolver();
        // These screens moved package, changed name, or were introduced in 1.21.
        for (String id : java.util.List.of("3928", "412", "8897", "405", "5346", "8671", "8816")) {
            cards.add(mappings.mapClassName("intermediary", "net.minecraft.class_" + id));
        }
        return java.util.Set.copyOf(cards);
    }
    public static boolean modalScreen() {
        var c=MinecraftClient.getInstance(); var screen=c.currentScreen;
        return c.world!=null && screen!=null && !(screen instanceof TitleScreen) && styledScreen();
    }
    public static void beginFrame() {
        GlassScreenText.beginFrame();
        AuraNativeSurfaces.beginFrame();
        GlassEntryPaint.beginFrame();
        var screen=MinecraftClient.getInstance().currentScreen;
        if(screen!=modalScreen){modalScreen=screen;modalStarted=System.nanoTime();}
    }
    public static void beforeScreen(DrawContext context) {
        var client=MinecraftClient.getInstance();
        if(!styledScreen() || client.currentScreen instanceof TitleScreen)return;
        if(client.world==null) {
            AuraUiRenderer.renderMenuPanoramaBackground(context,client,client.getWindow().getScaledWidth(),client.getWindow().getScaledHeight());
            context.draw();GlassRenderer.captureBackdrop(true);GlassRenderer.modalBackground(1);card(context);return;
        }
        float elapsed=Math.max(0,Math.min(1,(System.nanoTime()-modalStarted)/200_000_000f));
        float progress=1-(1-elapsed)*(1-elapsed)*(1-elapsed);
        context.draw();GlassRenderer.captureBackdrop(true);GlassRenderer.modalBackground(progress);
        card(context);
    }
    public static boolean styledScreen() {
        var screen = MinecraftClient.getInstance().currentScreen;
        if(screen==null || nativeContentDepth>0 || net.auramc.glass.common.render.MenuResources.stopping())return false;
        boolean ingame = screen instanceof net.minecraft.client.gui.screen.ingame.BookScreen
                || screen instanceof net.minecraft.client.gui.screen.ingame.BookEditScreen
                || screen instanceof net.minecraft.client.gui.screen.ingame.AbstractSignEditScreen
                || screen instanceof net.minecraft.client.gui.screen.ingame.AbstractCommandBlockScreen
                || screen instanceof net.minecraft.client.gui.screen.ingame.StructureBlockScreen
                || screen instanceof net.minecraft.client.gui.screen.ingame.JigsawBlockScreen;
        // Screen's runtime package is named in development and net.minecraft in production.
        return net.auramc.glass.common.ui.MenuScope.applies(screen.getClass().getName(),
                net.minecraft.client.gui.screen.Screen.class.getPackageName(), ingame,
                screen instanceof net.minecraft.client.gui.screen.ingame.HandledScreen<?>,
                screen instanceof net.minecraft.client.gui.screen.ChatScreen);
    }
    public static void beginNativeContent(){nativeContentDepth++;}
    public static void endNativeContent(){nativeContentDepth=Math.max(0,nativeContentDepth-1);}
    private static void card(DrawContext context){
        var client=MinecraftClient.getInstance();
        if(!CARDS.contains(client.currentScreen.getClass().getName()))return;
        float width=Math.min(420,context.getScaledWindowWidth()-24),height=Math.min(240,context.getScaledWindowHeight()-48);
        GlassRenderer.draw((context.getScaledWindowWidth()-width)*.5f,(context.getScaledWindowHeight()-height)*.5f,width,height,GlassStyles.frame(GlassPreset.PANEL),1);
        GlassRenderer.flush();
    }
    public static void titleBackdrop(DrawContext context){
        if(!styledScreen())return;
        context.draw();GlassRenderer.captureBackdrop(true);GlassRenderer.modalBackground(1);
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

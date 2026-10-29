package net.auramc.glass.client;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.loader.api.FabricLoader;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents;
import net.auramc.glass.common.render.AuraRenderer;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.GlassStyles;
import net.auramc.glass.client.ui.GlassDebugOverlay;

public final class AuraGlassClientMod implements ClientModInitializer {
    @Override public void onInitializeClient() {
        GlassStyles.initialize(FabricLoader.getInstance().getConfigDir());
        net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents.END_CLIENT_TICK.register(client->{GlassStyles.poll();net.auramc.glass.common.render.MenuResources.prepare(client);if(client.currentScreen!=null)net.auramc.glass.client.ui.GlassLabels.prepare(client.currentScreen);net.auramc.glass.client.karaoke.KaraokeEngine.getInstance().tick(client);if(Boolean.getBoolean("aura.qa.layoutTour"))net.auramc.glass.client.ui.GlassLayoutTour.tick(client);net.auramc.glass.client.quickmenu.MaxFpsBenchmarkRunner.onClientTick(client);});
        GlassDebugOverlay.initialize();
        net.auramc.glass.common.hud.HudRuntime.initialize(()->net.minecraft.client.MinecraftClient.getInstance().options.debugEnabled);
        net.auramc.glass.client.quickmenu.QuickMenuRuntime.initialize();
        ClientLifecycleEvents.CLIENT_STOPPING.register(client -> { net.auramc.glass.common.render.MenuResources.stop();GlassRenderer.close(); AuraRenderer.close(); });
    }
}

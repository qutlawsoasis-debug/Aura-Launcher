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
        GlassDebugOverlay.initialize();
        net.auramc.glass.client.ui.GlassGalleryActions.initialize(net.auramc.glass.client.gui.GlassGallery121::new);
        ClientLifecycleEvents.CLIENT_STOPPING.register(client -> { GlassRenderer.close(); AuraRenderer.close(); });
    }
}

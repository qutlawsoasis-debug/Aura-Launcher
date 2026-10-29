package net.auramc.glass.client.quickmenu;

import net.auramc.glass.common.style.GlassQuality;
import net.auramc.glass.common.style.GlassStyles;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.CloudRenderMode;
import net.minecraft.client.option.GameOptions;
import net.minecraft.client.option.GraphicsMode;
import net.minecraft.client.option.ParticlesMode;

import java.lang.reflect.Method;

public final class QuickMenuMaxFps {
    private QuickMenuMaxFps() {}

    public static class SavedState {
        public boolean irisPresent;
        public boolean irisShadersEnabled;

        public boolean dhPresent;

        public int viewDistance = 12;
        public int simulationDistance = 10;
        public String particles = ParticlesMode.ALL.name();
        public String cloudRenderMode = CloudRenderMode.FANCY.name();
        public boolean entityShadows = true;
        public double entityDistanceScaling = 1.0;
        public String graphicsMode = GraphicsMode.FANCY.name();
        public boolean ao = true;
        public int mipmapLevels = 4;
        public int biomeBlendRadius = 2;
        public boolean enableVsync = true;

        public String glassQuality = GlassQuality.HIGH.name();
    }

    public static boolean isEnabled() {
        return QuickMenuConfig.get().maxFps;
    }

    public static void setEnabled(boolean enable) {
        if (enable == isEnabled()) return;
        toggle();
    }

    public static void toggle() {
        var config = QuickMenuConfig.get();
        boolean enable = !config.maxFps;
        MinecraftClient client = MinecraftClient.getInstance();

        if (enable) {
            SavedState state = new SavedState();

            // 1. Iris Shaders
            if (FabricLoader.getInstance().isModLoaded("iris")) {
                try {
                    Class<?> apiClass = Class.forName("net.irisshaders.iris.api.v0.IrisApi");
                    Object api = apiClass.getMethod("getInstance").invoke(null);
                    Object irisConfig = apiClass.getMethod("getConfig").invoke(api);
                    Method areShadersEnabled = irisConfig.getClass().getMethod("areShadersEnabled");
                    state.irisPresent = true;
                    state.irisShadersEnabled = Boolean.TRUE.equals(areShadersEnabled.invoke(irisConfig));
                    if (state.irisShadersEnabled) {
                        Method setShadersEnabledAndApply = irisConfig.getClass().getMethod("setShadersEnabledAndApply", boolean.class);
                        setShadersEnabledAndApply.invoke(irisConfig, false);
                    }
                } catch (Throwable t) {
                    System.err.println("[AuraMaxFps] Iris apply failed: " + t);
                }
            }

            // 2. Distant Horizons
            if (FabricLoader.getInstance().isModLoaded("distanthorizons")) {
                state.dhPresent = true;
                try {
                    Class<?> configClass = Class.forName("com.seibel.distanthorizons.core.config.Config");
                    // If Distant Horizons is loaded, disable or set distance 64 via reflection
                } catch (Throwable ignored) {}
            }

            // 3. Vanilla GameOptions
            if (client != null && client.options != null) {
                GameOptions opt = client.options;
                try {
                    state.viewDistance = opt.getViewDistance().getValue();
                    state.simulationDistance = opt.getSimulationDistance().getValue();
                    state.particles = opt.getParticles().getValue().name();
                    state.cloudRenderMode = opt.getCloudRenderMode().getValue().name();
                    state.entityShadows = opt.getEntityShadows().getValue();
                    state.entityDistanceScaling = opt.getEntityDistanceScaling().getValue();
                    state.graphicsMode = opt.getGraphicsMode().getValue().name();
                    state.ao = opt.getAo().getValue();
                    state.mipmapLevels = opt.getMipmapLevels().getValue();
                    state.biomeBlendRadius = opt.getBiomeBlendRadius().getValue();
                    state.enableVsync = opt.getEnableVsync().getValue();

                    // Apply preset
                    opt.getViewDistance().setValue(8);
                    opt.getSimulationDistance().setValue(6);
                    opt.getParticles().setValue(ParticlesMode.MINIMAL);
                    opt.getCloudRenderMode().setValue(CloudRenderMode.OFF);
                    opt.getEntityShadows().setValue(false);
                    opt.getEntityDistanceScaling().setValue(0.75);
                    opt.getGraphicsMode().setValue(GraphicsMode.FAST);
                    opt.getAo().setValue(false);
                    opt.getMipmapLevels().setValue(2);
                    opt.getBiomeBlendRadius().setValue(1);
                    opt.getEnableVsync().setValue(false);
                    opt.write();

                    if (client.worldRenderer != null) {
                        client.worldRenderer.reload();
                    }
                } catch (Throwable t) {
                    System.err.println("[AuraMaxFps] GameOptions apply failed: " + t);
                }
            }

            // 4. Glass Quality
            try {
                state.glassQuality = GlassStyles.quality().name();
                GlassStyles.setQuality(GlassQuality.LOW);
                GlassStyles.save();
            } catch (Throwable t) {
                System.err.println("[AuraMaxFps] Glass apply failed: " + t);
            }

            config.maxFpsSavedState = state;
            config.maxFps = true;
            QuickMenuConfig.save();
        } else {
            // Roll back completely
            SavedState state = config.maxFpsSavedState;
            if (state != null) {
                // 1. Iris Shaders
                if (state.irisPresent && state.irisShadersEnabled && FabricLoader.getInstance().isModLoaded("iris")) {
                    try {
                        Class<?> apiClass = Class.forName("net.irisshaders.iris.api.v0.IrisApi");
                        Object api = apiClass.getMethod("getInstance").invoke(null);
                        Object irisConfig = apiClass.getMethod("getConfig").invoke(api);
                        Method setShadersEnabledAndApply = irisConfig.getClass().getMethod("setShadersEnabledAndApply", boolean.class);
                        setShadersEnabledAndApply.invoke(irisConfig, true);
                    } catch (Throwable t) {
                        System.err.println("[AuraMaxFps] Iris rollback failed: " + t);
                    }
                }

                // 2. Distant Horizons
                if (state.dhPresent && FabricLoader.getInstance().isModLoaded("distanthorizons")) {
                    try {
                        Class<?> configClass = Class.forName("com.seibel.distanthorizons.core.config.Config");
                    } catch (Throwable ignored) {}
                }

                // 3. Vanilla GameOptions
                if (client != null && client.options != null) {
                    GameOptions opt = client.options;
                    try {
                        opt.getViewDistance().setValue(state.viewDistance);
                        opt.getSimulationDistance().setValue(state.simulationDistance);
                        try { opt.getParticles().setValue(ParticlesMode.valueOf(state.particles)); } catch (Throwable ignored) {}
                        try { opt.getCloudRenderMode().setValue(CloudRenderMode.valueOf(state.cloudRenderMode)); } catch (Throwable ignored) {}
                        opt.getEntityShadows().setValue(state.entityShadows);
                        opt.getEntityDistanceScaling().setValue(state.entityDistanceScaling);
                        try { opt.getGraphicsMode().setValue(GraphicsMode.valueOf(state.graphicsMode)); } catch (Throwable ignored) {}
                        opt.getAo().setValue(state.ao);
                        opt.getMipmapLevels().setValue(state.mipmapLevels);
                        opt.getBiomeBlendRadius().setValue(state.biomeBlendRadius);
                        opt.getEnableVsync().setValue(state.enableVsync);
                        opt.write();

                        if (client.worldRenderer != null) {
                            client.worldRenderer.reload();
                        }
                    } catch (Throwable t) {
                        System.err.println("[AuraMaxFps] GameOptions rollback failed: " + t);
                    }
                }

                // 4. Glass Quality
                try {
                    GlassQuality restoredQuality = GlassQuality.HIGH;
                    if (state.glassQuality != null) {
                        try { restoredQuality = GlassQuality.valueOf(state.glassQuality); } catch (Throwable ignored) {}
                    }
                    GlassStyles.setQuality(restoredQuality);
                    GlassStyles.save();
                } catch (Throwable t) {
                    System.err.println("[AuraMaxFps] Glass rollback failed: " + t);
                }
            }

            config.maxFps = false;
            config.maxFpsSavedState = null;
            QuickMenuConfig.save();
        }
    }
}

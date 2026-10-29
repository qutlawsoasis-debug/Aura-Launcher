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
        public boolean dhRenderingEnabled = true;
        public int dhChunkDistance = 128;

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

    public static boolean isModLoaded(String modId) {
        try {
            var loader = FabricLoader.getInstance();
            return loader != null && loader.isModLoaded(modId);
        } catch (Throwable ignored) {
            return false;
        }
    }

    public static void toggle() {
        var config = QuickMenuConfig.get();
        boolean enable = !config.maxFps;
        MinecraftClient client = null;
        try {
            client = MinecraftClient.getInstance();
        } catch (Throwable ignored) {}

        if (enable) {
            SavedState state = new SavedState();

            // 1. Iris Shaders
            applyIris(state);

            // 2. Distant Horizons
            applyDistantHorizons(state);

            // 3. Vanilla GameOptions
            if (client != null && client.options != null) {
                applyVanillaOptions(client.options, state);
                if (client.worldRenderer != null) {
                    try { client.worldRenderer.reload(); } catch (Throwable ignored) {}
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
                rollbackIris(state);

                // 2. Distant Horizons
                rollbackDistantHorizons(state);

                // 3. Vanilla GameOptions
                if (client != null && client.options != null) {
                    rollbackVanillaOptions(client.options, state);
                    if (client.worldRenderer != null) {
                        try { client.worldRenderer.reload(); } catch (Throwable ignored) {}
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

    public static void applyVanillaOptions(GameOptions opt, SavedState state) {
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

            // Preset:
            // 2. Дальность прорисовки 8, дальность симуляции 6; частицы «Минимум»; облака выкл;
            // тени сущностей выкл; дальность сущностей 75%; графика «Быстро»; плавное освещение выкл;
            // Mipmap 2; биомное смешивание 1; VSync выкл;
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
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] GameOptions apply failed: " + t);
        }
    }

    public static void rollbackVanillaOptions(GameOptions opt, SavedState state) {
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
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] GameOptions rollback failed: " + t);
        }
    }

    private static void applyIris(SavedState state) {
        if (!isModLoaded("iris")) return;
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

    private static void rollbackIris(SavedState state) {
        if (!state.irisPresent || !state.irisShadersEnabled || !isModLoaded("iris")) return;
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

    private static void applyDistantHorizons(SavedState state) {
        if (!isModLoaded("distanthorizons")) return;
        state.dhPresent = true;
        try {
            Class<?> dhConfigClass = Class.forName("com.seibel.distanthorizons.core.api.external.methods.config.DhApiConfig");
            Object instance = dhConfigClass.getField("INSTANCE").get(null);
            Object graphics = dhConfigClass.getMethod("graphics").invoke(instance);

            Method renderingEnabledMethod = graphics.getClass().getMethod("renderingEnabled");
            Object renderingEnabled = renderingEnabledMethod.invoke(graphics);
            Method getVal = renderingEnabled.getClass().getMethod("getValue");
            state.dhRenderingEnabled = Boolean.TRUE.equals(getVal.invoke(renderingEnabled));

            Method chunkDistanceMethod = graphics.getClass().getMethod("chunkRenderDistance");
            Object chunkDistance = chunkDistanceMethod.invoke(graphics);
            Object distVal = getVal.invoke(chunkDistance);
            if (distVal instanceof Number n) {
                state.dhChunkDistance = n.intValue();
            }

            setDhConfigValue(renderingEnabled, false);
            setDhConfigValue(chunkDistance, 64);
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Distant Horizons apply failed: " + t);
        }
    }

    private static void rollbackDistantHorizons(SavedState state) {
        if (!state.dhPresent || !isModLoaded("distanthorizons")) return;
        try {
            Class<?> dhConfigClass = Class.forName("com.seibel.distanthorizons.core.api.external.methods.config.DhApiConfig");
            Object instance = dhConfigClass.getField("INSTANCE").get(null);
            Object graphics = dhConfigClass.getMethod("graphics").invoke(instance);

            Method renderingEnabledMethod = graphics.getClass().getMethod("renderingEnabled");
            Object renderingEnabled = renderingEnabledMethod.invoke(graphics);
            setDhConfigValue(renderingEnabled, state.dhRenderingEnabled);

            Method chunkDistanceMethod = graphics.getClass().getMethod("chunkRenderDistance");
            Object chunkDistance = chunkDistanceMethod.invoke(graphics);
            setDhConfigValue(chunkDistance, state.dhChunkDistance);
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Distant Horizons rollback failed: " + t);
        }
    }

    private static void setDhConfigValue(Object configValueObj, Object newValue) {
        if (configValueObj == null) return;
        for (Method m : configValueObj.getClass().getMethods()) {
            if (m.getName().equals("setValue") && m.getParameterCount() == 1) {
                try {
                    m.invoke(configValueObj, newValue);
                    return;
                } catch (Throwable ignored) {}
            }
        }
    }
}

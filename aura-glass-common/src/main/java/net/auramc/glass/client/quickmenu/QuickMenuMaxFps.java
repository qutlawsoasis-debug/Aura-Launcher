package net.auramc.glass.client.quickmenu;

import net.auramc.glass.common.style.GlassQuality;
import net.auramc.glass.common.style.GlassStyles;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.CloudRenderMode;
import net.minecraft.client.option.GameOptions;
import net.minecraft.client.option.GraphicsMode;
import net.minecraft.client.option.ParticlesMode;

import java.io.File;
import java.lang.reflect.Method;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.Map;

public final class QuickMenuMaxFps {
    private QuickMenuMaxFps() {}

    private static Path optionsPathOverride = null;

    public static void setOptionsPath(Path path) {
        optionsPathOverride = path;
    }

    public static Path getOptionsPath(MinecraftClient client, GameOptions opt) {
        if (optionsPathOverride != null) return optionsPathOverride;
        try {
            var f = GameOptions.class.getDeclaredField("optionsFile");
            f.setAccessible(true);
            File file = (File) f.get(opt);
            if (file != null) return file.toPath();
        } catch (Throwable ignored) {}
        if (client != null && client.runDirectory != null) {
            return client.runDirectory.toPath().resolve("options.txt");
        }
        return Path.of("options.txt");
    }

    public static class SavedState {
        // Iris Shaders
        public boolean hasIrisShaders = false;
        public boolean irisShadersEnabled;

        // Distant Horizons
        public boolean hasDhRendering = false;
        public boolean dhRenderingEnabled;
        public boolean hasDhChunkDistance = false;
        public int dhChunkDistance;

        // Vanilla GameOptions
        public boolean hasViewDistance = false;
        public int viewDistance;

        public boolean hasSimulationDistance = false;
        public int simulationDistance;

        public boolean hasParticles = false;
        public String particles;

        public boolean hasCloudRenderMode = false;
        public String cloudRenderMode;

        public boolean hasEntityShadows = false;
        public boolean entityShadows;

        public boolean hasEntityDistanceScaling = false;
        public double entityDistanceScaling;

        public boolean hasGraphicsMode = false;
        public String graphicsMode;

        public boolean hasAo = false;
        public boolean ao;

        public boolean hasMipmapLevels = false;
        public int mipmapLevels;

        public boolean hasBiomeBlendRadius = false;
        public int biomeBlendRadius;

        public boolean hasEnableVsync = false;
        public boolean enableVsync;

        // Glass Quality
        public boolean hasGlassQuality = false;
        public String glassQuality;
    }

    public static boolean isEnabled() {
        return QuickMenuConfig.get().maxFps;
    }

    public static boolean setEnabled(boolean enable) {
        if (enable == isEnabled()) return true;
        return toggle();
    }

    public static boolean isModLoaded(String modId) {
        try {
            var loader = FabricLoader.getInstance();
            return loader != null && loader.isModLoaded(modId);
        } catch (Throwable ignored) {
            return false;
        }
    }

    public static boolean toggle() {
        var config = QuickMenuConfig.get();
        boolean enable = !config.maxFps;
        MinecraftClient client = null;
        try {
            client = MinecraftClient.getInstance();
        } catch (Throwable ignored) {}

        if (enable) {
            SavedState state = new SavedState();

            // 1. Capture Iris Shaders
            captureIris(state);

            // 2. Capture Distant Horizons
            captureDistantHorizons(state);

            // 3. Capture Vanilla GameOptions
            if (client != null && client.options != null) {
                captureVanillaOptions(client.options, state);
            }

            // 4. Capture Glass Quality
            try {
                state.glassQuality = GlassStyles.quality().name();
                state.hasGlassQuality = true;
            } catch (Throwable t) {
                System.err.println("[AuraMaxFps] Glass capture failed: " + t);
            }

            // ATOMIC DISK SAVE BEFORE APPLYING ANY MODIFICATIONS
            config.maxFpsSavedState = state;
            config.maxFps = true;
            boolean saved = QuickMenuConfig.save();
            if (!saved) {
                config.maxFps = false;
                config.maxFpsSavedState = null;
                System.err.println("[AuraMaxFps] Failed to write recovery snapshot to disk! Max FPS preset will not be enabled.");
                return false;
            }

            // Snapshot verified on disk. Now apply changes
            applyIris(state);
            applyDistantHorizons(state);
            if (client != null && client.options != null) {
                applyVanillaOptions(client.options, state);
                if (client.worldRenderer != null) {
                    try { client.worldRenderer.reload(); } catch (Throwable ignored) {}
                }
            }
            try {
                GlassStyles.setQuality(GlassQuality.LOW);
                GlassStyles.save();
            } catch (Throwable t) {
                System.err.println("[AuraMaxFps] Glass apply failed: " + t);
            }

            return true;
        } else {
            // ROLLBACK
            SavedState state = config.maxFpsSavedState;
            if (state == null) {
                config.maxFps = false;
                QuickMenuConfig.save();
                return true;
            }

            boolean allSuccess = true;

            // 1. Iris Shaders
            if (state.hasIrisShaders) {
                if (!rollbackIris(state)) {
                    allSuccess = false;
                }
            }

            // 2. Distant Horizons
            if (state.hasDhRendering || state.hasDhChunkDistance) {
                if (!rollbackDistantHorizons(state)) {
                    allSuccess = false;
                }
            }

            // 3. Vanilla GameOptions
            if (hasAnyVanillaOptions(state)) {
                if (client != null && client.options != null) {
                    if (!rollbackVanillaOptions(client, client.options, state)) {
                        allSuccess = false;
                    }
                    if (client.worldRenderer != null) {
                        try { client.worldRenderer.reload(); } catch (Throwable ignored) {}
                    }
                } else {
                    allSuccess = false;
                }
            }

            // 4. Glass Quality
            if (state.hasGlassQuality) {
                if (!rollbackGlass(state)) {
                    allSuccess = false;
                }
            }

            if (allSuccess) {
                config.maxFps = false;
                config.maxFpsSavedState = null;
                QuickMenuConfig.save();
                return true;
            } else {
                // Partial rollback failure: do NOT delete snapshot! Retain remaining fields
                QuickMenuConfig.save();
                System.err.println("[AuraMaxFps] Rollback partially failed; retaining recovery snapshot on disk.");
                return false;
            }
        }
    }

    public static boolean hasAnyVanillaOptions(SavedState state) {
        if (state == null) return false;
        return state.hasViewDistance || state.hasSimulationDistance || state.hasParticles ||
               state.hasCloudRenderMode || state.hasEntityShadows || state.hasEntityDistanceScaling ||
               state.hasGraphicsMode || state.hasAo || state.hasMipmapLevels ||
               state.hasBiomeBlendRadius || state.hasEnableVsync;
    }

    public static void captureVanillaOptions(GameOptions opt, SavedState state) {
        try {
            state.viewDistance = opt.getViewDistance().getValue();
            state.hasViewDistance = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture viewDistance failed: " + t); }

        try {
            state.simulationDistance = opt.getSimulationDistance().getValue();
            state.hasSimulationDistance = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture simulationDistance failed: " + t); }

        try {
            state.particles = opt.getParticles().getValue().name();
            state.hasParticles = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture particles failed: " + t); }

        try {
            state.cloudRenderMode = opt.getCloudRenderMode().getValue().name();
            state.hasCloudRenderMode = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture cloudRenderMode failed: " + t); }

        try {
            state.entityShadows = opt.getEntityShadows().getValue();
            state.hasEntityShadows = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture entityShadows failed: " + t); }

        try {
            state.entityDistanceScaling = opt.getEntityDistanceScaling().getValue();
            state.hasEntityDistanceScaling = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture entityDistanceScaling failed: " + t); }

        try {
            state.graphicsMode = opt.getGraphicsMode().getValue().name();
            state.hasGraphicsMode = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture graphicsMode failed: " + t); }

        try {
            state.ao = opt.getAo().getValue();
            state.hasAo = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture ao failed: " + t); }

        try {
            state.mipmapLevels = opt.getMipmapLevels().getValue();
            state.hasMipmapLevels = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture mipmapLevels failed: " + t); }

        try {
            state.biomeBlendRadius = opt.getBiomeBlendRadius().getValue();
            state.hasBiomeBlendRadius = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture biomeBlendRadius failed: " + t); }

        try {
            state.enableVsync = opt.getEnableVsync().getValue();
            state.hasEnableVsync = true;
        } catch (Throwable t) { System.err.println("[AuraMaxFps] Capture enableVsync failed: " + t); }
    }

    public static void applyVanillaOptions(GameOptions opt, SavedState state) {
        try {
            if (state.hasViewDistance) opt.getViewDistance().setValue(8);
            if (state.hasSimulationDistance) opt.getSimulationDistance().setValue(6);
            if (state.hasParticles) opt.getParticles().setValue(ParticlesMode.MINIMAL);
            if (state.hasCloudRenderMode) opt.getCloudRenderMode().setValue(CloudRenderMode.OFF);
            if (state.hasEntityShadows) opt.getEntityShadows().setValue(false);
            if (state.hasEntityDistanceScaling) opt.getEntityDistanceScaling().setValue(0.75);
            if (state.hasGraphicsMode) opt.getGraphicsMode().setValue(GraphicsMode.FAST);
            if (state.hasAo) opt.getAo().setValue(false);
            if (state.hasMipmapLevels) opt.getMipmapLevels().setValue(2);
            if (state.hasBiomeBlendRadius) opt.getBiomeBlendRadius().setValue(1);
            if (state.hasEnableVsync) opt.getEnableVsync().setValue(false);
            opt.write();
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] GameOptions apply failed: " + t);
        }
    }

    public static boolean rollbackVanillaOptions(GameOptions opt, SavedState state) {
        return rollbackVanillaOptions(null, opt, state);
    }

    public static boolean rollbackVanillaOptions(MinecraftClient client, GameOptions opt, SavedState state) {
        boolean inMemorySuccess = true;

        if (state.hasViewDistance) {
            try {
                opt.getViewDistance().setValue(state.viewDistance);
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback viewDistance failed: " + t);
            }
        }
        if (state.hasSimulationDistance) {
            try {
                opt.getSimulationDistance().setValue(state.simulationDistance);
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback simulationDistance failed: " + t);
            }
        }
        if (state.hasParticles) {
            try {
                opt.getParticles().setValue(ParticlesMode.valueOf(state.particles));
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback particles failed: " + t);
            }
        }
        if (state.hasCloudRenderMode) {
            try {
                opt.getCloudRenderMode().setValue(CloudRenderMode.valueOf(state.cloudRenderMode));
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback cloudRenderMode failed: " + t);
            }
        }
        if (state.hasEntityShadows) {
            try {
                opt.getEntityShadows().setValue(state.entityShadows);
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback entityShadows failed: " + t);
            }
        }
        if (state.hasEntityDistanceScaling) {
            try {
                opt.getEntityDistanceScaling().setValue(state.entityDistanceScaling);
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback entityDistanceScaling failed: " + t);
            }
        }
        if (state.hasGraphicsMode) {
            try {
                opt.getGraphicsMode().setValue(GraphicsMode.valueOf(state.graphicsMode));
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback graphicsMode failed: " + t);
            }
        }
        if (state.hasAo) {
            try {
                opt.getAo().setValue(state.ao);
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback ao failed: " + t);
            }
        }
        if (state.hasMipmapLevels) {
            try {
                opt.getMipmapLevels().setValue(state.mipmapLevels);
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback mipmapLevels failed: " + t);
            }
        }
        if (state.hasBiomeBlendRadius) {
            try {
                opt.getBiomeBlendRadius().setValue(state.biomeBlendRadius);
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback biomeBlendRadius failed: " + t);
            }
        }
        if (state.hasEnableVsync) {
            try {
                opt.getEnableVsync().setValue(state.enableVsync);
            } catch (Throwable t) {
                inMemorySuccess = false;
                System.err.println("[AuraMaxFps] Rollback enableVsync failed: " + t);
            }
        }

        try {
            opt.write();
        } catch (Throwable t) {
            inMemorySuccess = false;
            System.err.println("[AuraMaxFps] opt.write failed: " + t);
        }

        // QA 4: GameOptions.write() catches and swallows errors internally.
        // Verify disk contents and clear only verified fields.
        Path optionsPath = getOptionsPath(client, opt);
        boolean diskSuccess = verifyAndClearDiskOptions(optionsPath, state);

        return inMemorySuccess && diskSuccess;
    }

    public static boolean verifyAndClearDiskOptions(Path optionsPath, SavedState state) {
        if (optionsPath == null || !Files.exists(optionsPath)) {
            System.err.println("[AuraMaxFps] options.txt not found at " + optionsPath);
            return false;
        }

        Map<String, String> disk = new HashMap<>();
        try {
            for (String line : Files.readAllLines(optionsPath, StandardCharsets.UTF_8)) {
                int colon = line.indexOf(':');
                if (colon > 0) {
                    disk.put(line.substring(0, colon).trim(), line.substring(colon + 1).trim().replace("\"", ""));
                }
            }
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Failed to read options.txt: " + t);
            return false;
        }

        boolean allMatched = true;

        if (state.hasViewDistance) {
            String val = disk.get("renderDistance");
            if (val == null) val = disk.get("viewDistance");
            if (val != null && parseIntSafe(val) == state.viewDistance) {
                state.hasViewDistance = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk viewDistance mismatch: expected " + state.viewDistance + ", found " + val);
            }
        }

        if (state.hasSimulationDistance) {
            String val = disk.get("simulationDistance");
            if (val != null && parseIntSafe(val) == state.simulationDistance) {
                state.hasSimulationDistance = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk simulationDistance mismatch: expected " + state.simulationDistance + ", found " + val);
            }
        }

        if (state.hasParticles) {
            String val = disk.get("particles");
            int expectedOrdinal = -1;
            if ("ALL".equalsIgnoreCase(state.particles)) expectedOrdinal = 0;
            else if ("DECREASED".equalsIgnoreCase(state.particles)) expectedOrdinal = 1;
            else if ("MINIMAL".equalsIgnoreCase(state.particles)) expectedOrdinal = 2;

            if (val != null && (val.equalsIgnoreCase(state.particles) || (expectedOrdinal >= 0 && parseIntSafe(val) == expectedOrdinal))) {
                state.hasParticles = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk particles mismatch: expected " + state.particles + ", found " + val);
            }
        }

        if (state.hasCloudRenderMode) {
            String val = disk.get("renderClouds");
            if (val == null) val = disk.get("cloudRenderMode");
            boolean match = false;
            if (val != null) {
                if ("OFF".equalsIgnoreCase(state.cloudRenderMode) && "false".equalsIgnoreCase(val)) match = true;
                else if ("FAST".equalsIgnoreCase(state.cloudRenderMode) && "fast".equalsIgnoreCase(val)) match = true;
                else if ("FANCY".equalsIgnoreCase(state.cloudRenderMode) && ("fancy".equalsIgnoreCase(val) || "true".equalsIgnoreCase(val))) match = true;
                else if (val.equalsIgnoreCase(state.cloudRenderMode)) match = true;
            }
            if (match) {
                state.hasCloudRenderMode = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk cloudRenderMode mismatch: expected " + state.cloudRenderMode + ", found " + val);
            }
        }

        if (state.hasEntityShadows) {
            String val = disk.get("entityShadows");
            if (val != null && Boolean.parseBoolean(val) == state.entityShadows) {
                state.hasEntityShadows = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk entityShadows mismatch: expected " + state.entityShadows + ", found " + val);
            }
        }

        if (state.hasEntityDistanceScaling) {
            String val = disk.get("entityDistanceScaling");
            if (val != null && Math.abs(parseDoubleSafe(val) - state.entityDistanceScaling) < 0.05) {
                state.hasEntityDistanceScaling = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk entityDistanceScaling mismatch: expected " + state.entityDistanceScaling + ", found " + val);
            }
        }

        if (state.hasGraphicsMode) {
            String val = disk.get("graphicsMode");
            int expectedOrdinal = -1;
            if ("FAST".equalsIgnoreCase(state.graphicsMode)) expectedOrdinal = 0;
            else if ("FANCY".equalsIgnoreCase(state.graphicsMode)) expectedOrdinal = 1;
            else if ("FABULOUS".equalsIgnoreCase(state.graphicsMode)) expectedOrdinal = 2;

            if (val != null && (val.equalsIgnoreCase(state.graphicsMode) || (expectedOrdinal >= 0 && parseIntSafe(val) == expectedOrdinal))) {
                state.hasGraphicsMode = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk graphicsMode mismatch: expected " + state.graphicsMode + ", found " + val);
            }
        }

        if (state.hasAo) {
            String val = disk.get("ao");
            if (val != null && ("true".equalsIgnoreCase(val) == state.ao || (state.ao && ("1".equals(val) || "2".equals(val))))) {
                state.hasAo = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk ao mismatch: expected " + state.ao + ", found " + val);
            }
        }

        if (state.hasMipmapLevels) {
            String val = disk.get("mipmapLevels");
            if (val != null && parseIntSafe(val) == state.mipmapLevels) {
                state.hasMipmapLevels = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk mipmapLevels mismatch: expected " + state.mipmapLevels + ", found " + val);
            }
        }

        if (state.hasBiomeBlendRadius) {
            String val = disk.get("biomeBlendRadius");
            if (val != null && parseIntSafe(val) == state.biomeBlendRadius) {
                state.hasBiomeBlendRadius = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk biomeBlendRadius mismatch: expected " + state.biomeBlendRadius + ", found " + val);
            }
        }

        if (state.hasEnableVsync) {
            String val = disk.get("enableVsync");
            if (val != null && Boolean.parseBoolean(val) == state.enableVsync) {
                state.hasEnableVsync = false;
            } else {
                allMatched = false;
                System.err.println("[AuraMaxFps] Disk enableVsync mismatch: expected " + state.enableVsync + ", found " + val);
            }
        }

        return allMatched;
    }

    private static int parseIntSafe(String s) {
        try { return Integer.parseInt(s.trim()); } catch (Throwable t) { return Integer.MIN_VALUE; }
    }

    private static double parseDoubleSafe(String s) {
        try { return Double.parseDouble(s.trim()); } catch (Throwable t) { return Double.NaN; }
    }

    private static void captureIris(SavedState state) {
        if (!isModLoaded("iris")) return;
        try {
            Class<?> apiClass = Class.forName("net.irisshaders.iris.api.v0.IrisApi");
            Object api = apiClass.getMethod("getInstance").invoke(null);
            Object irisConfig = apiClass.getMethod("getConfig").invoke(api);
            Method areShadersEnabled = irisConfig.getClass().getMethod("areShadersEnabled");
            Object enabledVal = areShadersEnabled.invoke(irisConfig);
            if (enabledVal instanceof Boolean b) {
                state.irisShadersEnabled = b;
                state.hasIrisShaders = true;
            }
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Iris capture failed: " + t);
        }
    }

    private static void applyIris(SavedState state) {
        if (!state.hasIrisShaders || !isModLoaded("iris")) return;
        try {
            Class<?> apiClass = Class.forName("net.irisshaders.iris.api.v0.IrisApi");
            Object api = apiClass.getMethod("getInstance").invoke(null);
            Object irisConfig = apiClass.getMethod("getConfig").invoke(api);
            if (state.irisShadersEnabled) {
                Method setShadersEnabledAndApply = irisConfig.getClass().getMethod("setShadersEnabledAndApply", boolean.class);
                setShadersEnabledAndApply.invoke(irisConfig, false);
            }
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Iris apply failed: " + t);
        }
    }

    private static boolean rollbackIris(SavedState state) {
        if (!state.hasIrisShaders) return true;
        if (!isModLoaded("iris")) {
            System.err.println("[AuraMaxFps] Iris is not loaded; retaining iris state for future rollback.");
            return false;
        }
        try {
            Class<?> apiClass = Class.forName("net.irisshaders.iris.api.v0.IrisApi");
            Object api = apiClass.getMethod("getInstance").invoke(null);
            Object irisConfig = apiClass.getMethod("getConfig").invoke(api);
            Method setShadersEnabledAndApply = irisConfig.getClass().getMethod("setShadersEnabledAndApply", boolean.class);
            setShadersEnabledAndApply.invoke(irisConfig, state.irisShadersEnabled);
            state.hasIrisShaders = false;
            return true;
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Iris rollback failed: " + t);
            return false;
        }
    }

    private static void captureDistantHorizons(SavedState state) {
        if (!isModLoaded("distanthorizons")) return;
        try {
            Class<?> dhConfigClass = Class.forName("com.seibel.distanthorizons.core.api.external.methods.config.DhApiConfig");
            Object instance = dhConfigClass.getField("INSTANCE").get(null);
            Object graphics = dhConfigClass.getMethod("graphics").invoke(instance);

            try {
                Method renderingEnabledMethod = graphics.getClass().getMethod("renderingEnabled");
                Object renderingEnabled = renderingEnabledMethod.invoke(graphics);
                Method getVal = renderingEnabled.getClass().getMethod("getValue");
                Object val = getVal.invoke(renderingEnabled);
                if (val instanceof Boolean b) {
                    state.dhRenderingEnabled = b;
                    state.hasDhRendering = true;
                }
            } catch (Throwable t) {
                System.err.println("[AuraMaxFps] Failed reading DH renderingEnabled: " + t);
            }

            try {
                Method chunkDistanceMethod = graphics.getClass().getMethod("chunkRenderDistance");
                Object chunkDistance = chunkDistanceMethod.invoke(graphics);
                Method getVal = chunkDistance.getClass().getMethod("getValue");
                Object distVal = getVal.invoke(chunkDistance);
                if (distVal instanceof Number n) {
                    state.dhChunkDistance = n.intValue();
                    state.hasDhChunkDistance = true;
                }
            } catch (Throwable t) {
                System.err.println("[AuraMaxFps] Failed reading DH chunkRenderDistance: " + t);
            }
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] DH API initialization failed: " + t);
        }
    }

    private static void applyDistantHorizons(SavedState state) {
        if (!isModLoaded("distanthorizons")) return;
        try {
            Class<?> dhConfigClass = Class.forName("com.seibel.distanthorizons.core.api.external.methods.config.DhApiConfig");
            Object instance = dhConfigClass.getField("INSTANCE").get(null);
            Object graphics = dhConfigClass.getMethod("graphics").invoke(instance);

            if (state.hasDhRendering) {
                Method renderingEnabledMethod = graphics.getClass().getMethod("renderingEnabled");
                Object renderingEnabled = renderingEnabledMethod.invoke(graphics);
                setDhConfigValue(renderingEnabled, false);
            }

            if (state.hasDhChunkDistance) {
                Method chunkDistanceMethod = graphics.getClass().getMethod("chunkRenderDistance");
                Object chunkDistance = chunkDistanceMethod.invoke(graphics);
                setDhConfigValue(chunkDistance, 64);
            }
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Distant Horizons apply failed: " + t);
        }
    }

    private static boolean rollbackDistantHorizons(SavedState state) {
        if (!state.hasDhRendering && !state.hasDhChunkDistance) return true;
        if (!isModLoaded("distanthorizons")) {
            System.err.println("[AuraMaxFps] Distant Horizons is not loaded; retaining DH state for future rollback.");
            return false;
        }
        boolean allSuccess = true;
        try {
            Class<?> dhConfigClass = Class.forName("com.seibel.distanthorizons.core.api.external.methods.config.DhApiConfig");
            Object instance = dhConfigClass.getField("INSTANCE").get(null);
            Object graphics = dhConfigClass.getMethod("graphics").invoke(instance);

            if (state.hasDhRendering) {
                try {
                    Method renderingEnabledMethod = graphics.getClass().getMethod("renderingEnabled");
                    Object renderingEnabled = renderingEnabledMethod.invoke(graphics);
                    if (setDhConfigValue(renderingEnabled, state.dhRenderingEnabled)) {
                        state.hasDhRendering = false;
                    } else {
                        allSuccess = false;
                    }
                } catch (Throwable t) {
                    allSuccess = false;
                }
            }

            if (state.hasDhChunkDistance) {
                try {
                    Method chunkDistanceMethod = graphics.getClass().getMethod("chunkRenderDistance");
                    Object chunkDistance = chunkDistanceMethod.invoke(graphics);
                    if (setDhConfigValue(chunkDistance, state.dhChunkDistance)) {
                        state.hasDhChunkDistance = false;
                    } else {
                        allSuccess = false;
                    }
                } catch (Throwable t) {
                    allSuccess = false;
                }
            }
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Distant Horizons rollback failed: " + t);
            return false;
        }
        return allSuccess;
    }

    private static boolean setDhConfigValue(Object configValueObj, Object newValue) {
        if (configValueObj == null) return false;
        for (Method m : configValueObj.getClass().getMethods()) {
            if (m.getName().equals("setValue") && m.getParameterCount() == 1) {
                try {
                    m.invoke(configValueObj, newValue);
                    return true;
                } catch (Throwable t) {
                    System.err.println("[AuraMaxFps] Failed to set DH value: " + t);
                    return false;
                }
            }
        }
        return false;
    }

    private static boolean rollbackGlass(SavedState state) {
        if (!state.hasGlassQuality) return true;
        try {
            GlassQuality restoredQuality = GlassQuality.HIGH;
            if (state.glassQuality != null) {
                try { restoredQuality = GlassQuality.valueOf(state.glassQuality); } catch (Throwable ignored) {}
            }
            GlassStyles.setQuality(restoredQuality);
            if (!GlassStyles.save()) {
                System.err.println("[AuraMaxFps] GlassStyles.save() failed during rollback.");
                return false;
            }
            state.hasGlassQuality = false;
            return true;
        } catch (Throwable t) {
            System.err.println("[AuraMaxFps] Glass rollback failed: " + t);
            return false;
        }
    }
}

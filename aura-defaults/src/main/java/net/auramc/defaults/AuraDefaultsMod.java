package net.auramc.defaults;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.loader.api.FabricLoader;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;

public class AuraDefaultsMod implements ModInitializer {
    public static final String MOD_ID = "aura-defaults";
    public static final Logger LOGGER = LoggerFactory.getLogger(MOD_ID);

    @Override
    public void onInitialize() {
        AuraDefaultsConfig.load();
        LOGGER.info("[aura-defaults] Initialized (keepInventoryByDefault={}, lockCheats={})",
                AuraDefaultsConfig.get().keepInventoryByDefault,
                AuraDefaultsConfig.get().lockCheats);

        setupEmotes();
        setupHostCommands();
        setupMcaVillagerConversion();
    }

    private void setupMcaVillagerConversion() {
        if (!FabricLoader.getInstance().isModLoaded("mca")) {
            return;
        }
        net.fabricmc.fabric.api.event.lifecycle.v1.ServerEntityEvents.ENTITY_LOAD.register((entity, world) -> {
            String className = entity.getClass().getName();
            if (entity.getClass() == net.minecraft.entity.passive.VillagerEntity.class) {
                try {
                    Class<?> sqClass = Class.forName("fabric.net.conczin.mca.server.SpawnQueue");
                    Object sqInstance = sqClass.getMethod("getInstance").invoke(null);
                    sqClass.getMethod("convert", net.minecraft.entity.passive.VillagerEntity.class)
                            .invoke(sqInstance, (net.minecraft.entity.passive.VillagerEntity) entity);
                } catch (Throwable t) {
                    LOGGER.debug("[aura-defaults] MCA villager convert skipped: {}", t.getMessage());
                }
            } else if ("fabric.net.conczin.mca.entity.VillagerEntityMCA".equals(className)) {
                try {
                    Object genetics = entity.getClass().getMethod("getGenetics").invoke(entity);
                    Class<?> geneticsClass = Class.forName("fabric.net.conczin.mca.entity.ai.Genetics");
                    Object voiceGene = geneticsClass.getField("VOICE").get(null);
                    for (java.lang.reflect.Method m : geneticsClass.getMethods()) {
                        if ("setGene".equals(m.getName()) && m.getParameterCount() == 2) {
                            m.invoke(genetics, voiceGene, 0.5f);
                            break;
                        }
                    }
                } catch (Throwable t) {
                    LOGGER.debug("[aura-defaults] MCA voice gene normalize skipped: {}", t.getMessage());
                }
            }
        });
    }

    private void setupHostCommands() {
        net.fabricmc.fabric.api.command.v2.CommandRegistrationCallback.EVENT.register((dispatcher, registryAccess, environment) -> {
            net.auramc.defaults.command.HostBanCommands.register(dispatcher);
        });

        net.fabricmc.fabric.api.event.lifecycle.v1.ServerLifecycleEvents.SERVER_STARTED.register(server -> {
            try {
                server.getPlayerManager().getUserBanList().load();
                LOGGER.info("[aura-defaults] Loaded user ban list ({} entries)",
                        server.getPlayerManager().getUserBanList().values().size());
            } catch (Throwable t) {
                LOGGER.warn("[aura-defaults] Could not load ban list: {}", t.getMessage());
            }
        });
    }

    private void setupEmotes() {
        try {
            Path gameDir = FabricLoader.getInstance().getGameDir();
            Path emotesDir = gameDir.resolve("emotes");
            Path targetFile = emotesDir.resolve("salute.json");
            if (!Files.exists(targetFile)) {
                Files.createDirectories(emotesDir);
                Path configEmote = gameDir.resolve("config").resolve("emotes").resolve("salute.json");
                if (Files.exists(configEmote)) {
                    Files.copy(configEmote, targetFile, StandardCopyOption.REPLACE_EXISTING);
                } else {
                    try (InputStream is = AuraDefaultsMod.class.getResourceAsStream("/salute.json")) {
                        if (is != null) {
                            Files.copy(is, targetFile, StandardCopyOption.REPLACE_EXISTING);
                        }
                    }
                }
            }
        } catch (Throwable t) {
            LOGGER.warn("[aura-defaults] Could not extract default emotes: {}", t.getMessage());
        }
    }
}

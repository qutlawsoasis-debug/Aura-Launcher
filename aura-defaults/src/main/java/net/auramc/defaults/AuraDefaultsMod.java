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

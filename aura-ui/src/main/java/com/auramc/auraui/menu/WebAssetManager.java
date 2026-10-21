package com.auramc.auraui.menu;

import com.auramc.auraui.AuraUi;
import java.io.File;
import java.io.InputStream;
import java.net.URI;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import net.fabricmc.loader.api.FabricLoader;
import net.fabricmc.loader.api.ModContainer;

public final class WebAssetManager {
    private static final String ASSETS_ROOT = "assets/aura-ui/web/";
    private static final String[] ASSET_FILES = new String[] {
        "menu.html",
        "pause.html",
        "game.css",
        "fonts/Onest.woff2",
        "images/bg_1.jpg",
        "images/bg_5.jpg"
    };

    private static Path targetDir;
    private static boolean extracted = false;

    private WebAssetManager() {}

    public static synchronized Path getWebDir() {
        if (!extracted) {
            extractAssets();
            extracted = true;
        }
        return targetDir;
    }

    public static synchronized String getFileUrl(String relativeName) {
        Path dir = getWebDir();
        Path filePath = dir.resolve(relativeName);
        URI uri = filePath.toUri();
        return uri.toString();
    }

    private static void extractAssets() {
        Path configDir = FabricLoader.getInstance().getConfigDir();
        targetDir = configDir.resolve("aura-ui").resolve("web");

        String currentVersion = FabricLoader.getInstance()
            .getModContainer("aura-ui")
            .map(ModContainer::getMetadata)
            .map(meta -> meta.getVersion().getFriendlyString())
            .orElse("unknown");

        Path versionFile = targetDir.resolve(".version");

        try {
            Files.createDirectories(targetDir);
            boolean needExtract = true;
            if (Files.exists(versionFile)) {
                String existing = Files.readString(versionFile).trim();
                if (existing.equals(currentVersion)) {
                    needExtract = false;
                }
            }

            if (!needExtract) {
                // Verify all files exist
                for (String file : ASSET_FILES) {
                    if (!Files.exists(targetDir.resolve(file))) {
                        needExtract = true;
                        break;
                    }
                }
            }

            if (needExtract) {
                AuraUi.LOGGER.info("[aura-ui] Extracting web assets (v{}) to {}", currentVersion, targetDir);
                for (String file : ASSET_FILES) {
                    Path dest = targetDir.resolve(file);
                    Files.createDirectories(dest.getParent());
                    try (InputStream in = WebAssetManager.class.getClassLoader().getResourceAsStream(ASSETS_ROOT + file)) {
                        if (in != null) {
                            Files.copy(in, dest, StandardCopyOption.REPLACE_EXISTING);
                        } else {
                            AuraUi.LOGGER.warn("[aura-ui] Resource not found: {}{}", ASSETS_ROOT, file);
                        }
                    }
                }
                Files.writeString(versionFile, currentVersion);
            }
        } catch (Exception e) {
            AuraUi.LOGGER.error("[aura-ui] Failed to extract web assets", e);
        }
    }
}

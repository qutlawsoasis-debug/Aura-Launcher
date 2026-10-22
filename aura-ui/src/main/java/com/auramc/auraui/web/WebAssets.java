package com.auramc.auraui.web;

import com.auramc.auraui.AuraUi;
import net.fabricmc.loader.api.FabricLoader;
import net.fabricmc.loader.api.ModContainer;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Optional;
import java.util.stream.Stream;

public final class WebAssets {
    public static final String DIR_NAME = "aura-ui-web";
    private static volatile String cachedUrl;

    private WebAssets() {}

    public static String getUrl() {
        String override = System.getProperty("aura.html.url");
        if (override != null && !override.isBlank()) {
            return override;
        }
        if (cachedUrl != null) {
            return cachedUrl;
        }
        Path dir = extract();
        Path index = dir.resolve("index.html");
        String url = index.toUri().toString() + "?view=esc&ingame=1";
        cachedUrl = url;
        return url;
    }

    public static synchronized Path extract() {
        Path gameDir = FabricLoader.getInstance().getGameDir();
        Path targetDir = gameDir.resolve(DIR_NAME);

        ModContainer mod = FabricLoader.getInstance().getModContainer("aura-ui").orElse(null);
        String version = mod != null ? mod.getMetadata().getVersion().getFriendlyString() : "dev";

        Path versionFile = targetDir.resolve(".version");
        boolean needsExtract = true;

        if (Files.exists(targetDir) && Files.exists(versionFile)) {
            try {
                String existingVersion = Files.readString(versionFile).trim();
                if (version.equals(existingVersion) && Files.exists(targetDir.resolve("index.html"))) {
                    needsExtract = false;
                }
            } catch (Exception ignored) {
            }
        }

        if (!needsExtract) {
            return targetDir;
        }

        try {
            Files.createDirectories(targetDir);
            Optional<Path> sourceRoot = mod != null ? mod.findPath("assets/aura-ui/web") : Optional.empty();
            if (sourceRoot.isPresent()) {
                Path src = sourceRoot.get();
                try (Stream<Path> stream = Files.walk(src)) {
                    for (Path path : (Iterable<Path>) stream::iterator) {
                        Path rel = src.relativize(path);
                        Path dest = targetDir;
                        for (Path part : rel) {
                            dest = dest.resolve(part.toString());
                        }
                        if (Files.isDirectory(path)) {
                            Files.createDirectories(dest);
                        } else {
                            if (dest.getParent() != null) {
                                Files.createDirectories(dest.getParent());
                            }
                            Files.copy(path, dest, StandardCopyOption.REPLACE_EXISTING);
                        }
                    }
                }
            }
            Files.writeString(versionFile, version);
            AuraUi.LOGGER.info("[aura-ui] Unpacked web assets (v{}) to {}", version, targetDir);
        } catch (IOException e) {
            AuraUi.LOGGER.error("[aura-ui] Failed to extract web assets", e);
        }

        return targetDir;
    }
}

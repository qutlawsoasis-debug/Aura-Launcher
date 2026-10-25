package net.auramc.defaults.config;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.fabricmc.loader.api.FabricLoader;

import java.io.Reader;
import java.io.Writer;
import java.nio.file.Files;
import java.nio.file.Path;

public class AuraDefaultsConfig {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();
    private static final Path CONFIG_PATH = FabricLoader.getInstance().getConfigDir().resolve("aura-defaults.json");
    private static AuraDefaultsConfig instance = new AuraDefaultsConfig();

    public boolean keepInventoryByDefault = true;
    public boolean lockCheats = true;

    public static AuraDefaultsConfig get() {
        if (instance == null) {
            load();
        }
        return instance;
    }

    public static void load() {
        if (Files.exists(CONFIG_PATH)) {
            try (Reader reader = Files.newBufferedReader(CONFIG_PATH)) {
                AuraDefaultsConfig loaded = GSON.fromJson(reader, AuraDefaultsConfig.class);
                if (loaded != null) {
                    instance = loaded;
                    return;
                }
            } catch (Exception e) {
                e.printStackTrace();
            }
        }

        instance = new AuraDefaultsConfig();
        save();
    }

    public static void save() {
        try {
            if (!Files.exists(CONFIG_PATH.getParent())) {
                Files.createDirectories(CONFIG_PATH.getParent());
            }
            try (Writer writer = Files.newBufferedWriter(CONFIG_PATH)) {
                GSON.toJson(instance, writer);
            }
        } catch (Exception e) {
            e.printStackTrace();
        }
    }
}

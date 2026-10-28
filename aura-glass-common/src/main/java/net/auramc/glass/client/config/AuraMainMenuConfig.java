package net.auramc.glass.client.config;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import net.fabricmc.loader.api.FabricLoader;

import java.io.Reader;
import java.io.Writer;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.HashMap;
import java.util.Map;

public class AuraMainMenuConfig {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();
    private static final Path CONFIG_PATH = FabricLoader.getInstance().getConfigDir().resolve("aura-main-menu.json");
    private static AuraMainMenuConfig instance;

    public int version = 2;
    public Map<String, ButtonPos> buttons = new HashMap<>();

    public static class ButtonPos {
        public double xPercent = 0.5;
        public double yPercent = 0.38;
        public int offsetX;
        public int offsetY;
        public int width = 240;
        public int height = 20;

        public ButtonPos() {}

        public ButtonPos(double xPercent, double yPercent, int offsetX, int offsetY, int width, int height) {
            this.xPercent = xPercent;
            this.yPercent = yPercent;
            this.offsetX = offsetX;
            this.offsetY = offsetY;
            this.width = width;
            this.height = height;
        }

        public boolean isValid() {
            return !Double.isNaN(xPercent) && !Double.isInfinite(xPercent)
                    && !Double.isNaN(yPercent) && !Double.isInfinite(yPercent)
                    && width > 0 && height > 0;
        }
    }

    public static AuraMainMenuConfig get() {
        if (instance == null) {
            load();
        }
        return instance;
    }

    public static void load() {
        if (Files.exists(CONFIG_PATH)) {
            try (Reader reader = Files.newBufferedReader(CONFIG_PATH, StandardCharsets.UTF_8)) {
                AuraMainMenuConfig loaded = GSON.fromJson(reader, AuraMainMenuConfig.class);
                if (loaded != null && loaded.buttons != null) {
                    // Sanitize against null, NaN or infinite values
                    loaded.buttons.entrySet().removeIf(e -> e.getKey() == null || e.getValue() == null || !e.getValue().isValid());
                    // Schema migration: set default width/height if migrating from v1
                    for (Map.Entry<String, ButtonPos> entry : loaded.buttons.entrySet()) {
                        ButtonPos pos = entry.getValue();
                        if (pos.width <= 0) pos.width = getDefaultWidth(entry.getKey());
                        if (pos.height <= 0) pos.height = getDefaultHeight(entry.getKey());
                    }
                    if (!loaded.buttons.containsKey("aura_brand")) {
                        loaded.buttons.put("aura_brand", new ButtonPos(0.5, 0.38, -60, -44, 120, 30));
                    }
                    loaded.buttons.putIfAbsent("aura_edit_toggle", new ButtonPos(1, 0, -104, 8, 96, 18));
                    loaded.buttons.putIfAbsent("aura_edit_toolbar", new ButtonPos(0.5, 0, -80, 8, 160, 20));
                    instance = loaded;
                    return;
                }
            } catch (Exception e) {
                System.err.println("[Aura] Warning: Failed to parse aura-main-menu.json, reverting to defaults: " + e.getMessage());
            }
        }

        instance = new AuraMainMenuConfig();
        instance.populateDefaults();
        instance.save();
    }

    public void populateDefaults() {
        buttons.clear();
        buttons.put("aura_brand", new ButtonPos(0.06, 0.38, 0, -44, 120, 30));
        buttons.put("singleplayer", new ButtonPos(0.06, 0.38, 0, 0, 240, 20));
        buttons.put("multiplayer", new ButtonPos(0.06, 0.38, 0, 24, 240, 20));
        buttons.put("modmenu", new ButtonPos(0.06, 0.38, 0, 48, 240, 20));
        buttons.put("options", new ButtonPos(0.06, 0.38, 0, 72, 118, 20));
        buttons.put("quit", new ButtonPos(0.06, 0.38, 122, 72, 118, 20));
        buttons.put("language", new ButtonPos(0.06, 0.38, -24, 72, 20, 20));
        buttons.put("accessibility", new ButtonPos(0.06, 0.38, 244, 72, 20, 20));
        buttons.put("aura_edit_toggle", new ButtonPos(1.0, 0.0, -104, 8, 96, 18));
        buttons.put("aura_edit_toolbar", new ButtonPos(0.5, 0.0, -80, 8, 160, 20));
    }

    public void resetToDefaults() {
        populateDefaults();
        save();
    }

    public ButtonPos getPosition(String id) {
        if ("branding".equals(id) && buttons.containsKey("aura_brand")) {
            return buttons.get("aura_brand");
        }
        ButtonPos pos = buttons.get(id);
        if (pos == null || !pos.isValid()) {
            pos = createFallbackPos(id);
            buttons.put(id, pos);
        }
        return pos;
    }

    public void setBounds(String id, int screenWidth, int screenHeight, int newX, int newY, int newW, int newH) {
        ButtonPos pos = buttons.computeIfAbsent(id, k -> new ButtonPos());
        pos.xPercent = screenWidth > 0 ? (double) newX / screenWidth : 0;
        pos.yPercent = screenHeight > 0 ? (double) newY / screenHeight : 0;
        pos.offsetX = 0;
        pos.offsetY = 0;
        pos.width = Math.max(20, newW);
        pos.height = Math.max(14, newH);
    }

    private ButtonPos createFallbackPos(String id) {
        int index = buttons.size();
        int defaultW = getDefaultWidth(id);
        int defaultH = getDefaultHeight(id);
        return new ButtonPos(0.5, 0.38, -120, 96 + (index * 24), defaultW, defaultH);
    }

    public static int getDefaultWidth(String id) {
        if ("aura_brand".equals(id) || "branding".equals(id)) return 120;
        if ("language".equals(id) || "accessibility".equals(id)) return 20;
        if ("options".equals(id) || "quit".equals(id)) return 118;
        if ("aura_edit_toggle".equals(id)) return 96;
        if ("aura_edit_toolbar".equals(id)) return 160;
        return 240;
    }

    public static int getDefaultHeight(String id) {
        if ("aura_brand".equals(id) || "branding".equals(id)) return 30;
        if ("aura_edit_toggle".equals(id)) return 18;
        return 20;
    }

    public void save() {
        try {
            Path parent = CONFIG_PATH.getParent();
            if (parent != null && !Files.exists(parent)) {
                Files.createDirectories(parent);
            }
            Path tempPath = CONFIG_PATH.resolveSibling("aura-main-menu.json.tmp");
            try (Writer writer = Files.newBufferedWriter(tempPath, StandardCharsets.UTF_8)) {
                GSON.toJson(this, writer);
                writer.flush();
            }
            try {
                Files.move(tempPath, CONFIG_PATH, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
            } catch (Exception moveEx) {
                Files.move(tempPath, CONFIG_PATH, StandardCopyOption.REPLACE_EXISTING);
            }
        } catch (Exception e) {
            System.err.println("[Aura] Error: Failed to save aura-main-menu.json atomically: " + e.getMessage());
        }
    }
}

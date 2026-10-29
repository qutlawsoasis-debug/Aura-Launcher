package net.auramc.glass.client.music;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

public record BridgeConfig(int port, String token) {
    public BridgeConfig {
        token = (token == null) ? "" : token.trim();
    }

    public boolean isValid() {
        return port > 0 && port <= 65535 && !token.isEmpty();
    }

    public static BridgeConfig parse(String json) {
        if (json == null || json.isBlank()) {
            return null;
        }
        try {
            JsonObject obj = JsonParser.parseString(json).getAsJsonObject();
            int port = 0;
            if (obj.has("port")) {
                port = obj.get("port").getAsInt();
            } else if (obj.has("Port")) {
                port = obj.get("Port").getAsInt();
            }

            String token = "";
            if (obj.has("token")) {
                token = obj.get("token").getAsString();
            } else if (obj.has("Token")) {
                token = obj.get("Token").getAsString();
            } else if (obj.has("authToken")) {
                token = obj.get("authToken").getAsString();
            }

            BridgeConfig config = new BridgeConfig(port, token);
            return config.isValid() ? config : null;
        } catch (Exception ignored) {
            return null;
        }
    }

    public static BridgeConfig load(Path file) {
        if (file == null || !Files.isRegularFile(file)) {
            return null;
        }
        try {
            String content = Files.readString(file, StandardCharsets.UTF_8);
            return parse(content);
        } catch (IOException ignored) {
            return null;
        }
    }
}

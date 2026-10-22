package com.auramc.auraui.web;

import com.auramc.auraui.AuraUi;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.MessageScreen;
import net.minecraft.client.gui.screen.OpenToLanScreen;
import net.minecraft.client.gui.screen.StatsScreen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.screen.advancement.AdvancementsScreen;
import net.minecraft.client.option.*;
import net.minecraft.sound.SoundCategory;
import net.minecraft.text.Text;
import net.minecraft.util.Util;
import org.cef.callback.CefQueryCallback;

import java.io.File;

public final class HtmlBridge {
    private HtmlBridge() {}

    public static void handle(MinecraftClient client, String request, CefQueryCallback callback) {
        if (client == null) {
            callback.success("{\"ok\":false,\"error\":\"client_null\"}");
            return;
        }

        client.execute(() -> {
            try {
                process(client, request, callback);
            } catch (Exception e) {
                AuraUi.LOGGER.error("[aura-ui] BRIDGE error processing request: " + request, e);
                callback.success("{\"ok\":false,\"error\":\"internal_error\"}");
            }
        });
    }

    private static void process(MinecraftClient client, String request, CefQueryCallback callback) {
        if (request == null || request.isBlank()) {
            callback.success("{\"ok\":false,\"error\":\"empty_request\"}");
            return;
        }

        // Поддержка простого строкового действия ("resume", "main_menu") для обратной совместимости
        if (!request.trim().startsWith("{")) {
            handleAction(client, request.trim(), callback);
            return;
        }

        JsonObject json;
        try {
            json = JsonParser.parseString(request).getAsJsonObject();
        } catch (Exception e) {
            callback.success("{\"ok\":false,\"error\":\"invalid_json\"}");
            return;
        }

        String op = json.has("op") ? json.get("op").getAsString() : "";
        switch (op) {
            case "get" -> handleGet(client, callback);
            case "set" -> {
                String id = json.has("id") ? json.get("id").getAsString() : "";
                JsonElement val = json.get("value");
                handleSet(client, id, val, callback);
            }
            case "action" -> {
                String name = json.has("name") ? json.get("name").getAsString() : "";
                handleAction(client, name, callback);
            }
            default -> {
                AuraUi.LOGGER.info("[aura-ui] BRIDGE op={} id=null value=null ok=false", op);
                callback.success("{\"ok\":false,\"error\":\"unknown_op\"}");
            }
        }
    }

    private static void handleGet(MinecraftClient client, CefQueryCallback callback) {
        GameOptions o = client.options;
        JsonObject res = new JsonObject();
        JsonObject vals = new JsonObject();

        vals.addProperty("render", o.getViewDistance().getValue());
        vals.addProperty("sim", o.getSimulationDistance().getValue());

        GraphicsMode gm = o.getGraphicsMode().getValue();
        vals.addProperty("quality", gm == GraphicsMode.FABULOUS ? "fabulous" : (gm == GraphicsMode.FAST ? "fast" : "fancy"));

        int maxFps = o.getMaxFps().getValue();
        vals.addProperty("fps", maxFps == 260 ? "0" : String.valueOf(maxFps));

        vals.addProperty("vsync", o.getEnableVsync().getValue());
        vals.addProperty("fov", o.getFov().getValue());
        vals.addProperty("bright", (int) Math.round(o.getGamma().getValue() * 100.0));

        ParticlesMode pm = o.getParticles().getValue();
        vals.addProperty("particles", pm == ParticlesMode.MINIMAL ? "min" : (pm == ParticlesMode.DECREASED ? "dec" : "all"));

        CloudRenderMode cm = o.getCloudRenderMode().getValue();
        vals.addProperty("clouds", cm == CloudRenderMode.OFF ? "off" : (cm == CloudRenderMode.FAST ? "fast" : "fancy"));

        vals.addProperty("master", (int) Math.round(o.getSoundVolumeOption(SoundCategory.MASTER).getValue() * 100.0));
        vals.addProperty("music", (int) Math.round(o.getSoundVolumeOption(SoundCategory.MUSIC).getValue() * 100.0));
        vals.addProperty("blocks", (int) Math.round(o.getSoundVolumeOption(SoundCategory.BLOCKS).getValue() * 100.0));
        vals.addProperty("mobs", (int) Math.round(o.getSoundVolumeOption(SoundCategory.HOSTILE).getValue() * 100.0));
        vals.addProperty("players", (int) Math.round(o.getSoundVolumeOption(SoundCategory.PLAYERS).getValue() * 100.0));

        int sens = (int) Math.round(o.getMouseSensitivity().getValue() * 200.0);
        vals.addProperty("sens", clamp(sens, 1, 200));

        vals.addProperty("toggleSprint", o.getSprintToggled().getValue());
        vals.addProperty("raw", o.getRawMouseInput().getValue());
        vals.addProperty("chatOpacity", (int) Math.round(o.getChatOpacity().getValue() * 100.0));

        String lang = o.language != null ? o.language.toLowerCase() : "en_us";
        String shortLang = lang.startsWith("ru") ? "ru" : (lang.startsWith("uk") ? "uk" : "en");
        vals.addProperty("language", shortLang);

        res.add("values", vals);

        JsonArray keys = new JsonArray();
        addKey(keys, "Вперёд", o.forwardKey);
        addKey(keys, "Назад", o.backKey);
        addKey(keys, "Влево", o.leftKey);
        addKey(keys, "Вправо", o.rightKey);
        addKey(keys, "Прыжок", o.jumpKey);
        addKey(keys, "Присесть", o.sneakKey);
        addKey(keys, "Бег", o.sprintKey);
        addKey(keys, "Инвентарь", o.inventoryKey);
        addKey(keys, "Чат", o.chatKey);
        addKey(keys, "Список игроков", o.playerListKey);
        addKey(keys, "Скриншот", o.screenshotKey);
        res.add("keys", keys);

        String player = client.getSession() != null ? client.getSession().getUsername() : "Player";
        res.addProperty("player", player);

        String world = "Одиночный мир";
        if (client.isInSingleplayer()) {
            if (client.getServer() != null) {
                world = client.getServer().getSaveProperties().getLevelName();
            }
        } else if (client.getCurrentServerEntry() != null) {
            world = client.getCurrentServerEntry().name;
        }
        res.addProperty("world", world);

        res.addProperty("fps", client.getCurrentFps());

        AuraUi.LOGGER.info("[aura-ui] BRIDGE op=get id=null value=null ok=true");
        callback.success(res.toString());
    }

    private static void addKey(JsonArray array, String name, KeyBinding binding) {
        JsonObject obj = new JsonObject();
        obj.addProperty("name", name);
        obj.addProperty("key", binding != null ? binding.getBoundKeyLocalizedText().getString() : "—");
        array.add(obj);
    }

    private static void handleSet(MinecraftClient client, String id, JsonElement val, CefQueryCallback callback) {
        if (id == null || id.isBlank() || val == null) {
            AuraUi.LOGGER.info("[aura-ui] BRIDGE op=set id={} value={} ok=false", id, val);
            callback.success("{\"ok\":false,\"error\":\"missing_params\"}");
            return;
        }

        GameOptions o = client.options;
        boolean ok = true;
        String err = null;

        try {
            switch (id) {
                case "render" -> o.getViewDistance().setValue(clamp(val.getAsInt(), 2, 32));
                case "sim" -> o.getSimulationDistance().setValue(clamp(val.getAsInt(), 5, 32));
                case "quality" -> {
                    String s = val.getAsString();
                    GraphicsMode gm = "fabulous".equalsIgnoreCase(s) ? GraphicsMode.FABULOUS :
                            ("fast".equalsIgnoreCase(s) ? GraphicsMode.FAST : GraphicsMode.FANCY);
                    o.getGraphicsMode().setValue(gm);
                }
                case "fps" -> {
                    String s = val.getAsString();
                    int limit = "0".equals(s) ? 260 : Integer.parseInt(s);
                    o.getMaxFps().setValue(limit);
                }
                case "vsync" -> o.getEnableVsync().setValue(val.getAsBoolean());
                case "fov" -> o.getFov().setValue(clamp(val.getAsInt(), 30, 110));
                case "bright" -> o.getGamma().setValue(clamp(val.getAsInt(), 0, 100) / 100.0);
                case "particles" -> {
                    String s = val.getAsString();
                    ParticlesMode pm = "min".equalsIgnoreCase(s) ? ParticlesMode.MINIMAL :
                            ("dec".equalsIgnoreCase(s) ? ParticlesMode.DECREASED : ParticlesMode.ALL);
                    o.getParticles().setValue(pm);
                }
                case "clouds" -> {
                    String s = val.getAsString();
                    CloudRenderMode cm = "off".equalsIgnoreCase(s) ? CloudRenderMode.OFF :
                            ("fast".equalsIgnoreCase(s) ? CloudRenderMode.FAST : CloudRenderMode.FANCY);
                    o.getCloudRenderMode().setValue(cm);
                }
                case "master" -> o.getSoundVolumeOption(SoundCategory.MASTER).setValue(clamp(val.getAsInt(), 0, 100) / 100.0);
                case "music" -> o.getSoundVolumeOption(SoundCategory.MUSIC).setValue(clamp(val.getAsInt(), 0, 100) / 100.0);
                case "blocks" -> o.getSoundVolumeOption(SoundCategory.BLOCKS).setValue(clamp(val.getAsInt(), 0, 100) / 100.0);
                case "mobs" -> o.getSoundVolumeOption(SoundCategory.HOSTILE).setValue(clamp(val.getAsInt(), 0, 100) / 100.0);
                case "players" -> o.getSoundVolumeOption(SoundCategory.PLAYERS).setValue(clamp(val.getAsInt(), 0, 100) / 100.0);
                case "sens" -> o.getMouseSensitivity().setValue(clamp(val.getAsInt(), 1, 200) / 200.0);
                case "toggleSprint" -> o.getSprintToggled().setValue(val.getAsBoolean());
                case "raw" -> o.getRawMouseInput().setValue(val.getAsBoolean());
                case "chatOpacity" -> o.getChatOpacity().setValue(clamp(val.getAsInt(), 0, 100) / 100.0);
                case "language" -> {
                    String s = val.getAsString();
                    String lang = "uk".equalsIgnoreCase(s) ? "uk_ua" : ("en".equalsIgnoreCase(s) ? "en_us" : "ru_ru");
                    o.language = lang;
                    client.getLanguageManager().setLanguage(lang);
                    client.reloadResources();
                }
                default -> {
                    ok = false;
                    err = "unknown_id";
                }
            }
        } catch (Exception e) {
            ok = false;
            err = e.getMessage();
        }

        if (ok) {
            o.write();
            AuraUi.LOGGER.info("[aura-ui] BRIDGE op=set id={} value={} ok=true", id, val);
            callback.success("{\"ok\":true}");
        } else {
            AuraUi.LOGGER.info("[aura-ui] BRIDGE op=set id={} value={} ok=false", id, val);
            callback.success("{\"ok\":false,\"error\":\"" + (err != null ? err : "unknown_error") + "\"}");
        }
    }

    private static void handleAction(MinecraftClient client, String name, CefQueryCallback callback) {
        boolean ok = true;
        String err = null;

        switch (name) {
            case "resume" -> client.setScreen(null);
            case "main_menu" -> {
                boolean single = client.isInSingleplayer();
                if (client.world != null) client.world.disconnect();
                if (single) client.disconnect(new MessageScreen(Text.translatable("menu.savingLevel")));
                else client.disconnect();
                client.setScreen(new TitleScreen());
            }
            case "open_lan" -> client.setScreen(new OpenToLanScreen(null));
            case "advancements" -> {
                if (client.player != null) {
                    client.setScreen(new AdvancementsScreen(client.player.networkHandler.getAdvancementHandler()));
                }
            }
            case "stats" -> {
                if (client.player != null) {
                    client.setScreen(new StatsScreen(null, client.player.getStatHandler()));
                }
            }
            case "screenshots_folder" -> {
                File dir = new File(client.runDirectory, "screenshots");
                if (!dir.exists()) dir.mkdirs();
                Util.getOperatingSystem().open(dir);
            }
            case "invite" -> {
                ok = false;
                err = "not_ready";
            }
            default -> {
                ok = false;
                err = "unknown_action";
            }
        }

        if (ok) {
            AuraUi.LOGGER.info("[aura-ui] BRIDGE op=action id={} value=null ok=true", name);
            callback.success("{\"ok\":true}");
        } else {
            AuraUi.LOGGER.info("[aura-ui] BRIDGE op=action id={} value=null ok=false", name);
            callback.success("{\"ok\":false,\"error\":\"" + (err != null ? err : "action_error") + "\"}");
        }
    }

    private static int clamp(int val, int min, int max) {
        return Math.max(min, Math.min(max, val));
    }
}

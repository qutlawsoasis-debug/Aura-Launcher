package net.auramc.defaults.client;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents;
import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.sound.PositionedSoundInstance;
import net.minecraft.sound.SoundEvents;
import net.minecraft.text.Text;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;

public class AuraUpdateNoticeHandler {
    private static final Logger LOGGER = LoggerFactory.getLogger("aura-update-notice");
    private static final String NOTICE_FILE_NAME = "update-notice.json";
    private static final String PACK_STATE_FILE_NAME = "pack-state.json";
    private static final String MANIFEST_URL = "https://raw.githubusercontent.com/qutlawsoasis-debug/Aura-Pack/main/manifest.json";

    private static String activeVersion = "";
    private static String activeType = "";
    private static String activeMessage = "";
    private static boolean activeForce = false;
    private static long activeTimestamp = 0;

    private static boolean hasShownChat = false;
    private static long lastChatReminderTick = 0;
    private static long currentTick = 0;

    private static int countdownSeconds = -1;
    private static int countdownTickCounter = 0;
    private static boolean isScheduledToStop = false;

    public static void init() {
        LOGGER.info("[aura-update-notice] Initializing update notice handler");

        ClientPlayConnectionEvents.JOIN.register((handler, sender, client) -> {
            hasShownChat = false;
            lastChatReminderTick = 0;
        });

        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            currentTick++;
            handleClientTick(client);
        });

        startBackgroundManifestWatcher();
    }

    private static void handleClientTick(MinecraftClient client) {
        if (currentTick % 60 == 0) {
            checkNoticeFile();
        }

        if (countdownSeconds > 0) {
            countdownTickCounter++;
            if (countdownTickCounter >= 20) {
                countdownTickCounter = 0;
                countdownSeconds--;
                if (client.player != null) {
                    client.player.sendMessage(
                            Text.literal("§c[Aura] Перезапуск игры через " + countdownSeconds + " сек для применения обновления..."),
                            true);
                }
                if (countdownSeconds <= 0 && !isScheduledToStop) {
                    isScheduledToStop = true;
                    LOGGER.info("[aura-update-notice] Forced restart triggered. Calling scheduleStop().");
                    client.execute(client::scheduleStop);
                }
            }
        }

        if (!activeVersion.isEmpty() && client.player != null) {
            if (!hasShownChat) {
                hasShownChat = true;
                lastChatReminderTick = currentTick;
                displayUpdateNotification(client, activeType, activeVersion, activeMessage);
            } else if (currentTick - lastChatReminderTick >= 6000) { // Каждые ~5 минут
                lastChatReminderTick = currentTick;
                String typeStr = "launcher".equalsIgnoreCase(activeType) ? "лаунчера" : "сборки";
                client.inGameHud.getChatHud().addMessage(
                        Text.literal("§6[Aura] §eНапоминание: Доступно обновление " + typeStr + " (§f" + activeVersion + "§e). Перезапустите игру для применения."));
            }
        }
    }

    private static void checkNoticeFile() {
        try {
            Path gameDir = FabricLoader.getInstance().getGameDir();
            Path noticePath = gameDir.resolve(NOTICE_FILE_NAME);

            if (Files.isRegularFile(noticePath)) {
                String content = Files.readString(noticePath, StandardCharsets.UTF_8).trim();
                if (!content.isEmpty()) {
                    JsonObject obj = JsonParser.parseString(content).getAsJsonObject();
                    String version = obj.has("version") ? obj.get("version").getAsString() : "";
                    String type = obj.has("type") ? obj.get("type").getAsString() : "pack";
                    String message = obj.has("message") ? obj.get("message").getAsString() : "";
                    boolean force = obj.has("forceRestart") && obj.get("forceRestart").getAsBoolean();
                    long timestamp = obj.has("timestamp") ? obj.get("timestamp").getAsLong() : 0;
                    int restartSeconds = obj.has("restartCountdownSeconds") ? obj.get("restartCountdownSeconds").getAsInt() : 0;

                    if (!version.equals(activeVersion) || timestamp != activeTimestamp) {
                        activeVersion = version;
                        activeType = type;
                        activeMessage = message;
                        activeForce = force;
                        activeTimestamp = timestamp;
                        hasShownChat = false;

                        if (force) {
                            countdownSeconds = restartSeconds > 0 ? restartSeconds : 5;
                            countdownTickCounter = 0;
                            isScheduledToStop = false;
                        } else {
                            countdownSeconds = -1;
                        }
                    }
                    return;
                }
            }

            if (!activeVersion.isEmpty() && countdownSeconds < 0) {
                // Файл уведомления был удален лаунчером (обновление применено или сброшено)
                activeVersion = "";
                activeType = "";
                activeMessage = "";
                activeForce = false;
                activeTimestamp = 0;
                hasShownChat = false;
            }
        } catch (Throwable t) {
            // Ошибки чтения файла не должны ломать игровой цикл
        }
    }

    private static void displayUpdateNotification(MinecraftClient client, String type, String version, String message) {
        String typeLabel = "launcher".equalsIgnoreCase(type) ? "лаунчера" : "сборки";
        String mainText = message != null && !message.trim().isEmpty()
                ? message.trim()
                : "Пожалуйста, сохраните игру и перезапустите для применения обновления.";

        client.inGameHud.getChatHud().addMessage(
                Text.literal("§6[Aura] §cВнимание! §eВышло обновление " + typeLabel + " (§f" + version + "§e)"));
        client.inGameHud.getChatHud().addMessage(
                Text.literal("§6[Aura] §f" + mainText));

        try {
            client.getSoundManager().play(
                    PositionedSoundInstance.master(SoundEvents.BLOCK_NOTE_BLOCK_PLING.value(), 1.0f));
        } catch (Throwable ignored) {
        }
    }

    private static void startBackgroundManifestWatcher() {
        Thread thread = new Thread(() -> {
            try {
                // Начальная задержка 25 секунд после запуска игры
                Thread.sleep(25000);
            } catch (InterruptedException e) {
                return;
            }

            while (true) {
                try {
                    checkRemoteManifest();
                } catch (Throwable t) {
                    LOGGER.debug("[aura-update-notice] Remote check error: {}", t.getMessage());
                }

                try {
                    // Проверка каждые 8 минут
                    Thread.sleep(8 * 60 * 1000);
                } catch (InterruptedException e) {
                    return;
                }
            }
        }, "Aura-UpdateCheck-Thread");

        thread.setDaemon(true);
        thread.start();
    }

    private static void checkRemoteManifest() {
        if (!activeVersion.isEmpty()) {
            return; // Уведомление уже активно
        }

        try {
            Path gameDir = FabricLoader.getInstance().getGameDir();
            Path stateFile = gameDir.resolve(PACK_STATE_FILE_NAME);
            if (!Files.isRegularFile(stateFile)) {
                return;
            }

            String stateContent = Files.readString(stateFile, StandardCharsets.UTF_8);
            JsonObject stateObj = JsonParser.parseString(stateContent).getAsJsonObject();
            if (!stateObj.has("packVersion")) {
                return;
            }
            String localPackVersion = stateObj.get("packVersion").getAsString();

            URL url = new URL(MANIFEST_URL + "?t=" + System.currentTimeMillis());
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("User-Agent", "AuraClient/1.0");
            conn.setConnectTimeout(6000);
            conn.setReadTimeout(6000);

            if (conn.getResponseCode() == 200) {
                try (InputStreamReader reader = new InputStreamReader(conn.getInputStream(), StandardCharsets.UTF_8)) {
                    JsonObject manifestObj = JsonParser.parseReader(reader).getAsJsonObject();
                    if (manifestObj.has("packVersion")) {
                        String remotePackVersion = manifestObj.get("packVersion").getAsString();
                        if (!remotePackVersion.isEmpty() && !remotePackVersion.equals(localPackVersion)) {
                            LOGGER.info("[aura-update-notice] Detected new remote pack version: {} (local: {})",
                                    remotePackVersion, localPackVersion);

                            activeVersion = remotePackVersion;
                            activeType = "pack";
                            activeMessage = "Вышло обновление сборки Aura (" + remotePackVersion + "). Перезапустите игру для применения.";
                            activeForce = false;
                            activeTimestamp = System.currentTimeMillis();
                            hasShownChat = false;
                        }
                    }
                }
            }
        } catch (Throwable ignored) {
        }
    }
}

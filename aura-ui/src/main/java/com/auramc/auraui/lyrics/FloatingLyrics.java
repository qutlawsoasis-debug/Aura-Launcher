package com.auramc.auraui.lyrics;

import com.auramc.auraui.AuraUi;
import net.minecraft.block.BlockState;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.render.Camera;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.util.ScreenshotRecorder;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.server.integrated.IntegratedServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.text.Text;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.GameRules;
import net.minecraft.world.Heightmap;
import org.joml.Matrix4f;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Iterator;
import java.util.List;
import java.util.Random;
import java.util.concurrent.ConcurrentLinkedQueue;

/**
 * 3D летающие строки песен (Floating Lyrics) в Minecraft.
 * Отрисовка по образцу ванильной таблички с ником (EntityRenderer.renderLabelIfPresent):
 * - translate(pos - camera.getPos())
 * - multiply(camera.getRotation())
 * - scale(-s, -s, s) где s = 0.025 * k (высота ~0.7 блока на 8 блоках, k = 3.15)
 * - textRenderer.drawWithOutline(text.asOrderedText(), x, y, 0xFFFFFFFF, 0xFF0A1420, matrix, consumers, 0xF000F0)
 * - проверка непересечения экранных прямоугольников (до 8 попыток, затем сдвиг вверх на строку)
 * - consumers.draw() в конце
 */
public final class FloatingLyrics {
    private static final ConcurrentLinkedQueue<String> QUEUE = new ConcurrentLinkedQueue<>();
    private static final List<LyricWord> ACTIVE_WORDS = new ArrayList<>();
    private static final Random RANDOM = new Random(42L);

    private static final String[] DEMO_LINES = {
        "Bright horizon shining through the silent mist",
        "We are wandering beneath the golden glow",
        "Gentle echoes dancing in the open breeze",
        "Endless dreams awake within the starlit sky"
    };
    private static int demoIndex = 0;
    private static long lastDemoTime = 0L;

    // Автоматическая телепортация на сушу и фиксация 3 кадров в демо-режиме
    private static boolean landTeleported = false;
    private static int idleTicks = 0;
    private static boolean shotAppearTaken = false;
    private static boolean shotHoldTaken = false;
    private static boolean shotFadeTaken = false;
    public static int fpsWithDemo = 0;
    public static int fpsWithoutDemo = 0;

    private static final class LyricWord {
        final String text;
        final Vec3d worldPos;
        final long spawnTime;
        final double hMin, hMax, vMin, vMax;
        long fadeStartTime = -1L;

        LyricWord(String text, Vec3d worldPos, long spawnTime, double hMin, double hMax, double vMin, double vMax) {
            this.text = text;
            this.worldPos = worldPos;
            this.spawnTime = spawnTime;
            this.hMin = hMin;
            this.hMax = hMax;
            this.vMin = vMin;
            this.vMax = vMax;
        }

        boolean overlaps(double ohMin, double ohMax, double ovMin, double ovMax) {
            return this.hMin < ohMax && this.hMax > ohMin && this.vMin < ovMax && this.vMax > ovMin;
        }
    }

    private FloatingLyrics() {
    }

    public static void show(String line) {
        if (line != null && !line.isBlank()) {
            QUEUE.add(line);
        }
    }

    public static boolean isDemoEnabled() {
        if (Boolean.getBoolean("aura.lyrics.demo")) {
            return true;
        }
        try {
            if (Files.exists(Path.of("run/config/aura-lyrics-demo.txt"))
                    || Files.exists(Path.of("config/aura-lyrics-demo.txt"))) {
                return true;
            }
        } catch (Throwable ignored) {
        }
        return false;
    }

    public static void render(MatrixStack matrices, Camera camera, float tickDelta) {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client == null || client.world == null || camera == null) {
            return;
        }

        long now = System.currentTimeMillis();
        boolean demo = isDemoEnabled();

        // В демо-режиме: устанавливаем полдень, сушу и запускаем строки
        if (demo) {
            setupDemoEnvironment(client);

            if (fpsWithoutDemo == 0 && ACTIVE_WORDS.isEmpty() && QUEUE.isEmpty()) {
                if (++idleTicks >= 25) {
                    fpsWithoutDemo = client.getCurrentFps();
                    AuraUi.LOGGER.info("[lyrics3d] Idle FPS on surface (without demo): {}", fpsWithoutDemo);
                }
            }

            if (idleTicks >= 25 && now - lastDemoTime >= 3500L) {
                lastDemoTime = now;
                show(DEMO_LINES[demoIndex % DEMO_LINES.length]);
                demoIndex++;
            }
        }

        // Если нет активных слов и нет входящих строк - выход
        if (ACTIVE_WORDS.isEmpty() && QUEUE.isEmpty()) {
            return;
        }

        TextRenderer textRenderer = client.textRenderer;
        if (textRenderer == null) {
            return;
        }

        // Высота слова ~0.7 блока на 8 блоках: k = 3.15, baseS = 0.025 * 3.15 = 0.07875 (9 * 0.07875 ≈ 0.708 блока)
        float k = 3.15f;
        float baseS = 0.025f * k;

        // Обработка очереди входящих строк
        String newLine;
        while ((newLine = QUEUE.poll()) != null) {
            String trimmed = newLine.trim();
            if (trimmed.isEmpty()) {
                continue;
            }

            // Старые слова плавно вытесняются
            for (LyricWord w : ACTIVE_WORDS) {
                if (w.fadeStartTime < 0) {
                    w.fadeStartTime = now;
                }
            }

            String[] rawWords = trimmed.split("\\s+");
            int wordCount = Math.min(10, rawWords.length);

            Vec3d camPos = camera.getPos();
            Vec3d forward = Vec3d.fromPolar(camera.getPitch(), camera.getYaw()).normalize();
            Vec3d right = forward.crossProduct(new Vec3d(0, 1, 0));
            if (right.lengthSquared() < 1e-4) {
                right = new Vec3d(1, 0, 0);
            } else {
                right = right.normalize();
            }
            Vec3d up = right.crossProduct(forward).normalize();

            // Размещение без наложения:
            // Проверка пересечения экранных прямоугольников (до 8 попыток, затем сдвиг вверх на строку)
            for (int i = 0; i < wordCount; i++) {
                String wordText = rawWords[i];
                float wWidth = textRenderer.getWidth(wordText);
                double wordBlockWidth = wWidth * baseS;
                double wordBlockHeight = 9.0 * baseS;
                double padH = 0.4;
                double padV = 0.3;

                boolean isLeft = (i % 2 == 0);
                double sideSign = isLeft ? -1.0 : 1.0;

                double bestD = 8.0;
                double bestH = sideSign * 2.5;
                double bestV = 0.0;
                boolean placed = false;

                // До 8 попыток случайного размещения без пересечений
                for (int attempt = 0; attempt < 8; attempt++) {
                    double d = 6.0 + (RANDOM.nextDouble() * 4.0);
                    // Горизонталь: пусто в центре [-1.5, +1.5], слова в [1.5, 4.0]
                    double h = sideSign * (1.5 + (RANDOM.nextDouble() * 2.5));
                    double v = -0.6 + (RANDOM.nextDouble() * 1.6); // +-0.8 по высоте

                    double curHMin = h - (wordBlockWidth / 2.0) - padH;
                    double curHMax = h + (wordBlockWidth / 2.0) + padH;
                    double curVMin = v - padV;
                    double curVMax = v + wordBlockHeight + padV;

                    boolean overlaps = false;
                    for (LyricWord existing : ACTIVE_WORDS) {
                        if (existing.overlaps(curHMin, curHMax, curVMin, curVMax)) {
                            overlaps = true;
                            break;
                        }
                    }

                    if (!overlaps) {
                        bestD = d;
                        bestH = h;
                        bestV = v;
                        placed = true;
                        break;
                    }
                }

                // Если за 8 попыток не нашли свободное место: сдвиг вверх на строку
                if (!placed) {
                    bestD = 7.0 + (RANDOM.nextDouble() * 2.0);
                    bestH = sideSign * (1.8 + (RANDOM.nextDouble() * 1.8));
                    bestV = 0.0;

                    while (true) {
                        double curHMin = bestH - (wordBlockWidth / 2.0) - padH;
                        double curHMax = bestH + (wordBlockWidth / 2.0) + padH;
                        double curVMin = bestV - padV;
                        double curVMax = bestV + wordBlockHeight + padV;

                        boolean overlaps = false;
                        for (LyricWord existing : ACTIVE_WORDS) {
                            if (existing.overlaps(curHMin, curHMax, curVMin, curVMax)) {
                                overlaps = true;
                                break;
                            }
                        }
                        if (!overlaps || bestV > 3.0) {
                            break;
                        }
                        bestV += (wordBlockHeight + 0.4); // шаг вверх на строку
                    }
                }

                double finalHMin = bestH - (wordBlockWidth / 2.0) - padH;
                double finalHMax = bestH + (wordBlockWidth / 2.0) + padH;
                double finalVMin = bestV - padV;
                double finalVMax = bestV + wordBlockHeight + padV;

                Vec3d worldPos = camPos.add(forward.multiply(bestD)).add(right.multiply(bestH)).add(up.multiply(bestV));
                long spawnTime = now + (i * 90L);

                ACTIVE_WORDS.add(new LyricWord(wordText, worldPos, spawnTime, finalHMin, finalHMax, finalVMin, finalVMax));
            }
        }

        if (ACTIVE_WORDS.isEmpty()) {
            return;
        }

        VertexConsumerProvider.Immediate immediate = client.getBufferBuilders().getEntityVertexConsumers();
        boolean renderedAny = false;

        Iterator<LyricWord> it = ACTIVE_WORDS.iterator();
        while (it.hasNext()) {
            LyricWord w = it.next();
            if (now < w.spawnTime) {
                continue;
            }

            long age = now - w.spawnTime;
            float scale;
            float alpha;
            float yRise;

            if (w.fadeStartTime > 0) {
                long fadeElapsed = now - w.fadeStartTime;
                if (fadeElapsed >= 400L) {
                    it.remove();
                    continue;
                }
                float p = (float) fadeElapsed / 400.0f;
                scale = 1.0f;
                alpha = Math.max(0.0f, 1.0f - p);
                yRise = 0.4f * p;
            } else if (age < 250L) {
                // Появление: scale 0.6 -> 1, alpha 0 -> 1 за 250 мс (ease-out)
                float p = (float) age / 250.0f;
                float ease = 1.0f - (1.0f - p) * (1.0f - p);
                scale = 0.6f + 0.4f * ease;
                alpha = ease;
                yRise = 0.0f;
            } else if (age < 250L + 2500L) {
                // Жизнь 2.5 с
                scale = 1.0f;
                alpha = 1.0f;
                yRise = 0.0f;
            } else if (age < 250L + 2500L + 400L) {
                // Уход: затухание alpha -> 0 и подъем за 400 мс
                float p = (float) (age - 2750L) / 400.0f;
                scale = 1.0f;
                alpha = Math.max(0.0f, 1.0f - p);
                yRise = 0.4f * p;
            } else {
                it.remove();
                continue;
            }

            int a = (int) (alpha * 255.0f);
            if (a <= 4) {
                continue;
            }

            renderedAny = true;
            float s = baseS * scale;

            matrices.push();
            // Образец EntityRenderer.renderLabelIfPresent:
            matrices.translate(
                    w.worldPos.x - camera.getPos().x,
                    w.worldPos.y - camera.getPos().y + yRise,
                    w.worldPos.z - camera.getPos().z
            );
            matrices.multiply(camera.getRotation());
            matrices.scale(-s, -s, s);

            Matrix4f matrix = matrices.peek().getPositionMatrix();
            float textWidth = textRenderer.getWidth(w.text);
            float x = -textWidth / 2.0f;
            float y = 0.0f;

            // Читаемость на светлом небе: белое слово с темной обводкой без ряби
            // drawWithOutline(OrderedText, x, y, color, outlineColor, matrix, consumers, light)
            int textColor = (a << 24) | 0xFFFFFF;
            int outlineColor = (a << 24) | 0x0A1420;

            textRenderer.drawWithOutline(
                    Text.literal(w.text).asOrderedText(),
                    x,
                    y,
                    textColor,
                    outlineColor,
                    matrix,
                    immediate,
                    0xF000F0
            );

            matrices.pop();
        }

        // consumers.draw() один раз в конце
        if (renderedAny) {
            immediate.draw();
        }

        // Авто-фиксация 3 кадров в демо-режиме (появление / удержание с F3 / уход)
        if (demo && !ACTIVE_WORDS.isEmpty()) {
            LyricWord lead = ACTIVE_WORDS.get(0);
            long leadAge = now - lead.spawnTime;

            if (!shotAppearTaken && leadAge >= 140L && leadAge <= 230L) {
                shotAppearTaken = true;
                client.options.debugEnabled = false;
                ScreenshotRecorder.saveScreenshot(client.runDirectory, "lyrics3d-appear.png", client.getFramebuffer(), msg -> {});
                AuraUi.LOGGER.info("[lyrics3d] Captured lyrics3d-appear.png (scale/alpha rising)");
            } else if (shotAppearTaken && !shotHoldTaken && leadAge >= 1200L && leadAge <= 1800L) {
                shotHoldTaken = true;
                client.options.debugEnabled = true;
                fpsWithDemo = client.getCurrentFps();
                ScreenshotRecorder.saveScreenshot(client.runDirectory, "lyrics3d-hold.png", client.getFramebuffer(), msg -> {});
                AuraUi.LOGGER.info("[lyrics3d] Captured lyrics3d-hold.png (F3 active, FPS: {})", fpsWithDemo);
            } else if (shotHoldTaken && !shotFadeTaken && leadAge >= 2800L && leadAge <= 3050L) {
                shotFadeTaken = true;
                client.options.debugEnabled = false;
                ScreenshotRecorder.saveScreenshot(client.runDirectory, "lyrics3d-fade.png", client.getFramebuffer(), msg -> {});
                AuraUi.LOGGER.info("[lyrics3d] Captured lyrics3d-fade.png (fade-out, rising)");
                AuraUi.LOGGER.info("[lyrics3d] ALL SHOTS COMPLETED. FPS with demo: {}, without demo: {}", fpsWithDemo, fpsWithoutDemo);
            }
        }
    }

    private static void setupDemoEnvironment(MinecraftClient client) {
        if (client.world == null || client.player == null) {
            return;
        }

        IntegratedServer server = client.getServer();
        if (server != null) {
            server.getOverworld().setTimeOfDay(6000L); // Полдень
            server.getOverworld().getGameRules().get(GameRules.DO_DAYLIGHT_CYCLE).set(false, server);
            server.getOverworld().getGameRules().get(GameRules.DO_WEATHER_CYCLE).set(false, server);
        }

        if (!landTeleported) {
            landTeleported = true;
            int px = (int) client.player.getX();
            int pz = (int) client.player.getZ();
            int targetX = px;
            int targetZ = pz;
            int targetY = 72;
            boolean found = false;

            // Поиск суши без воды в радиусе 200 блоков
            for (int r = 0; r <= 200; r += 16) {
                for (int dx = -r; dx <= r; dx += 16) {
                    for (int dz = -r; dz <= r; dz += 16) {
                        int tx = px + dx;
                        int tz = pz + dz;
                        int top = client.world.getTopY(Heightmap.Type.MOTION_BLOCKING, tx, tz);
                        BlockPos pos = new BlockPos(tx, top - 1, tz);
                        BlockState state = client.world.getBlockState(pos);
                        if (!state.isAir() && state.getFluidState().isEmpty()) {
                            targetX = tx;
                            targetZ = tz;
                            targetY = top + 1;
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }
                if (found) break;
            }

            client.player.refreshPositionAndAngles(targetX + 0.5, targetY, targetZ + 0.5, 0.0f, 0.0f);
            if (server != null) {
                ServerPlayerEntity sPlayer = server.getPlayerManager().getPlayer(client.player.getUuid());
                if (sPlayer != null) {
                    sPlayer.teleport(server.getOverworld(), targetX + 0.5, targetY, targetZ + 0.5, 0.0f, 0.0f);
                }
            }
            AuraUi.LOGGER.info("[lyrics3d] Teleported player to surface land at ({}, {}, {})", targetX, targetY, targetZ);
        }
    }
}

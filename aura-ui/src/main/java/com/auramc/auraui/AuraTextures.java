package com.auramc.auraui;

import java.io.InputStream;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.texture.NativeImageBackedTexture;
import net.minecraft.util.Identifier;

/**
 * Текстуры экрана загрузки. Грузятся прямо из jar мода, а не через ресурс-паки:
 * экран загрузки рисуется, пока ресурсы ещё только загружаются (так же делает логотип Mojang).
 */
public final class AuraTextures {
    public static final Identifier BACKGROUND = id("background");
    public static final Identifier CAPSULE = id("capsule");
    public static final Identifier RING = id("ring");
    public static final Identifier GLOW = id("glow");
    public static final Identifier SHADOW = id("shadow");

    private static boolean registered;

    private AuraTextures() {
    }

    private static Identifier id(String name) {
        return new Identifier("aura-ui", "textures/gui/" + name + ".png");
    }

    /** Регистрирует все текстуры; вызывается из SplashOverlay.init на потоке рендера. */
    public static void register(MinecraftClient client) {
        if (registered) return;
        registered = true;
        for (Identifier tex : new Identifier[] { BACKGROUND, CAPSULE, RING, GLOW, SHADOW }) {
            try (InputStream in = AuraTextures.class.getResourceAsStream("/assets/" + tex.getNamespace() + "/" + tex.getPath())) {
                if (in == null) {
                    AuraUi.LOGGER.warn("[aura-ui] texture not found: {}", tex);
                    continue;
                }
                NativeImageBackedTexture texture = new NativeImageBackedTexture(NativeImage.read(in));
                // Сглаживание: текстуры масштабируются под любое разрешение окна
                texture.setFilter(true, false);
                client.getTextureManager().registerTexture(tex, texture);
            } catch (Exception e) {
                AuraUi.LOGGER.warn("[aura-ui] failed to load {}: {}", tex, e.toString());
            }
        }
    }
}

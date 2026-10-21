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
    public static final Identifier VIGNETTE = id("vignette");
    public static final Identifier WORDMARK = id("wordmark");
    public static final Identifier SUBTITLE = id("subtitle");
    public static final Identifier SHINE = id("shine");
    public static final Identifier LABEL = id("label");
    public static final Identifier TIP1 = id("tip1");
    public static final Identifier TIP2 = id("tip2");
    public static final Identifier TIP3 = id("tip3");
    public static final Identifier TIP4 = id("tip4");

    public static final Identifier[] TIPS = new Identifier[] { TIP1, TIP2, TIP3, TIP4 };

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
        for (Identifier tex : new Identifier[] {
            BACKGROUND, VIGNETTE, WORDMARK, SUBTITLE, SHINE, LABEL, TIP1, TIP2, TIP3, TIP4
        }) {
            try (InputStream in = AuraTextures.class.getResourceAsStream("/assets/" + tex.getNamespace() + "/" + tex.getPath())) {
                if (in == null) {
                    AuraUi.LOGGER.warn("[aura-ui] texture not found: {}", tex);
                    continue;
                }
                NativeImageBackedTexture texture = new NativeImageBackedTexture(NativeImage.read(in));
                texture.setFilter(true, false);
                client.getTextureManager().registerTexture(tex, texture);
            } catch (Exception e) {
                AuraUi.LOGGER.warn("[aura-ui] failed to load {}: {}", tex, e.toString());
            }
        }
    }
}

package com.auramc.auraui.screen;

import net.fabricmc.loader.api.FabricLoader;
import net.minecraft.client.gui.screen.Screen;

/**
 * Прототип HTML-меню: включается флагом -Daura.html=true и только при установленном MCEF.
 * Классы MCEF здесь не упоминаются, чтобы мод грузился и без него.
 */
public final class HtmlScreen {
    public static final String DEFAULT_URL = "http://localhost:5180/ingame/index.html?view=esc&ingame=1";

    private HtmlScreen() {}

    public static boolean isEnabled() {
        return Boolean.getBoolean("aura.html") && FabricLoader.getInstance().isModLoaded("mcef");
    }

    public static Screen pause() {
        return new McefHtmlScreen(System.getProperty("aura.html.url", DEFAULT_URL));
    }
}

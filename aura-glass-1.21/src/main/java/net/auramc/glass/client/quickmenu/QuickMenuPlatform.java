package net.auramc.glass.client.quickmenu;

import net.minecraft.client.MinecraftClient;
import net.minecraft.util.Identifier;

public final class QuickMenuPlatform {
    private QuickMenuPlatform() {}
    public static Identifier skin(MinecraftClient client) {
        return client.player == null ? null : client.player.getSkinTextures().texture();
    }
}

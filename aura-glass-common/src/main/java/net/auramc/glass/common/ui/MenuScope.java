package net.auramc.glass.common.ui;

public final class MenuScope {
    private MenuScope() {}

    public static boolean applies(String className, boolean handled, boolean chat) {
        if (handled || chat || !className.startsWith("net.minecraft.client.gui.screen.")) return false;
        return !className.startsWith("net.minecraft.client.gui.screen.ingame.");
    }
}

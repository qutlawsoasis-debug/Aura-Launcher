package net.auramc.glass.common;

import net.auramc.glass.common.ui.MenuScope;

public final class MenuScopeTest {
    public static void main(String[] args) {
        String named = "net.minecraft.client.gui.screen";
        check(MenuScope.applies(named + ".TitleScreen", named, false, false, false), "named title");
        check(MenuScope.applies(named + ".GameMenuScreen", named, false, false, false), "named pause");
        check(MenuScope.applies(named + ".option.ChatOptionsScreen", named, false, false, false), "chat options remain menu");
        check(!MenuScope.applies(named + ".ChatScreen", named, false, false, true), "chat excluded");
        check(!MenuScope.applies(named + ".ingame.CreativeInventoryScreen", named, false, true, false), "handled excluded");
        check(!MenuScope.applies(named + ".ingame.BookScreen", named, true, false, false), "item book excluded");
        check(!MenuScope.applies("com.terraformersmc.modmenu.gui.ModsScreen", named, false, false, false), "named mod screen excluded");
        check(!MenuScope.applies(named + "mod.CustomScreen", named, false, false, false), "namespace boundary");

        String production = "net.minecraft";
        check(MenuScope.applies("net.minecraft.class_442", production, false, false, false), "production title");
        check(MenuScope.applies("net.minecraft.class_429", production, false, false, false), "production options");
        check(MenuScope.applies("net.minecraft.class_433", production, false, false, false), "production pause");
        check(!MenuScope.applies("net.minecraft.class_3872", production, true, false, false), "production book excluded");
        check(!MenuScope.applies("net.minecraft.class_490", production, false, true, false), "production inventory excluded");
        check(!MenuScope.applies("net.minecraft.class_408", production, false, false, true), "production chat excluded");
        check(!MenuScope.applies("com.terraformersmc.modmenu.gui.ModsScreen", production, false, false, false), "production mod screen excluded");
        check(!MenuScope.applies("net.minecraftmod.CustomScreen", production, false, false, false), "production namespace boundary");
        System.out.println("Menu scope: named and production vanilla menus, ingame/chat/mod boundaries PASS");
    }
    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}

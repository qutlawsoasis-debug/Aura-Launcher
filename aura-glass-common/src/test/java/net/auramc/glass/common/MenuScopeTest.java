package net.auramc.glass.common;

import net.auramc.glass.common.ui.MenuScope;

public final class MenuScopeTest {
    public static void main(String[] args){
        check(MenuScope.applies("net.minecraft.client.gui.screen.GameMenuScreen",false,false),"pause");
        check(MenuScope.applies("net.minecraft.client.gui.screen.option.ChatOptionsScreen",false,false),"chat options remain menu");
        check(!MenuScope.applies("net.minecraft.client.gui.screen.ChatScreen",false,true),"chat excluded");
        check(!MenuScope.applies("net.minecraft.client.gui.screen.ingame.CreativeInventoryScreen",true,false),"all handled excluded");
        check(!MenuScope.applies("net.minecraft.client.gui.screen.ingame.BookScreen",false,false),"item book excluded");
        check(!MenuScope.applies("com.terraformersmc.modmenu.gui.ModsScreen",false,false),"mod screen excluded");
        check(!MenuScope.applies("net.minecraft.client.gui.screenmod.CustomScreen",false,false),"namespace boundary");
        System.out.println("Menu scope: vanilla menus, chat options, handled/chat/items/mod boundaries PASS");
    }
    private static void check(boolean condition,String message){if(!condition)throw new AssertionError(message);}
}

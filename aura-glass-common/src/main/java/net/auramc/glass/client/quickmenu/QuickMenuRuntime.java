package net.auramc.glass.client.quickmenu;

import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;

public final class QuickMenuRuntime {
    private static KeyBinding menu;
    public static long totalDrawCalls, totalCaptureRequests;
    private QuickMenuRuntime() {}
    public static void initialize() {
        QuickMenuConfig.initialize();
        menu = KeyBindingHelper.registerKeyBinding(new KeyBinding("Меню Aura", InputUtil.Type.KEYSYM, GLFW.GLFW_KEY_F8, "category.aura"));
        QuickMenuEffects.initialize();
        net.auramc.glass.client.music.MusicSource.get().volume((float) QuickMenuConfig.get().musicVolume);
        net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientLifecycleEvents.CLIENT_STOPPING.register(client -> {
            net.auramc.glass.client.quickmenu.music.MusicArtwork.close();
        });
        ClientTickEvents.END_CLIENT_TICK.register(client -> { while (menu.wasPressed()) toggle(); });
    }
    public static KeyBinding menuKey() { return menu; }
    public static void toggle() {
        var client = MinecraftClient.getInstance();
        if (client.currentScreen instanceof QuickMenuScreen quick) { quick.toggle(); return; }
        if (client.currentScreen != null || client.world == null || client.player == null) return;
        client.setScreen(new QuickMenuScreen());
    }
}
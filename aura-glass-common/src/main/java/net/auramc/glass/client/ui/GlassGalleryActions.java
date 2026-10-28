package net.auramc.glass.client.ui;

import net.auramc.glass.common.render.BackdropRenderer;
import net.fabricmc.fabric.api.client.keybinding.v1.KeyBindingHelper;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.option.KeyBinding;
import net.minecraft.client.util.InputUtil;
import org.lwjgl.glfw.GLFW;
import java.util.function.Function;

public final class GlassGalleryActions {
    private static KeyBinding key;
    private static Function<Screen,Screen> factory;
    private GlassGalleryActions() {}
    public static void initialize(Function<Screen,Screen> screenFactory) {
        factory=screenFactory;
        key=KeyBindingHelper.registerKeyBinding(new KeyBinding("key.aura.glass_gallery",InputUtil.Type.KEYSYM,GLFW.GLFW_KEY_F8,"category.aura"));
    }
    public static boolean handleKey(int code,int scan,int action) {
        if(key==null || !key.matchesKey(code,scan))return false;
        if(action==GLFW.GLFW_PRESS)toggle();return true;
    }
    public static boolean handleMouse(int button,int action) {
        if(key==null || !key.matchesMouse(button))return false;
        if(action==GLFW.GLFW_PRESS)toggle();return true;
    }
    private static void toggle() {
        var c=MinecraftClient.getInstance();
        if(c.currentScreen instanceof GlassGalleryScreen gallery){gallery.close();return;}
        if(c.world!=null)BackdropRenderer.requestWorldImage();
        c.setScreen(factory.apply(c.currentScreen));
    }
    public static void afterFrame(){if(MinecraftClient.getInstance().currentScreen instanceof GlassGalleryScreen gallery)gallery.afterFrame();}
}

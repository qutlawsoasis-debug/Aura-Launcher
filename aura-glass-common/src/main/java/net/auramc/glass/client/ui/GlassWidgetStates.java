package net.auramc.glass.client.ui;

import net.auramc.glass.common.style.GlassState;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.widget.ClickableWidget;
import org.lwjgl.glfw.GLFW;

public final class GlassWidgetStates {
    private GlassWidgetStates() {}
    public static GlassState state(ClickableWidget widget){
        if(!widget.active)return GlassState.DISABLED;
        long window=MinecraftClient.getInstance().getWindow().getHandle();
        if(widget.isHovered() && GLFW.glfwGetMouseButton(window,0)==GLFW.GLFW_PRESS
                || widget.isFocused() && (GLFW.glfwGetKey(window,GLFW.GLFW_KEY_ENTER)==GLFW.GLFW_PRESS
                || GLFW.glfwGetKey(window,GLFW.GLFW_KEY_SPACE)==GLFW.GLFW_PRESS))return GlassState.PRESS;
        return widget.isFocused()?GlassState.FOCUSED:widget.isHovered()?GlassState.HOVER:GlassState.NORMAL;
    }
}

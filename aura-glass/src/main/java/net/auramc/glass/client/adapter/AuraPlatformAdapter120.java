package net.auramc.glass.client.adapter;

import net.auramc.glass.common.adapter.AuraPlatformAdapter;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.sound.PositionedSoundInstance;
import net.minecraft.sound.SoundEvents;

/**
 * Minecraft 1.20.1 implementation of the Aura platform adapter.
 * Uses vanilla Yarn mappings for 1.20.1 Fabric.
 */
public class AuraPlatformAdapter120 implements AuraPlatformAdapter {

    @Override
    public int getCurrentFps() {
        MinecraftClient client = MinecraftClient.getInstance();
        return client != null ? client.getCurrentFps() : 60;
    }

    @Override
    public void playClickSound() {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client != null && client.getSoundManager() != null) {
            client.getSoundManager().play(PositionedSoundInstance.master(SoundEvents.UI_BUTTON_CLICK, 1.0F));
        }
    }

    @Override
    public void setClipboardString(String text) {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client != null && client.keyboard != null) {
            client.keyboard.setClipboard(text != null ? text : "");
        }
    }

    @Override
    public String getClipboardString() {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client != null && client.keyboard != null) {
            return client.keyboard.getClipboard();
        }
        return "";
    }

    @Override
    public double getGuiScale() {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client != null && client.getWindow() != null) {
            return client.getWindow().getScaleFactor();
        }
        return 2.0;
    }

    @Override
    public int getScaledWidth() {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client != null && client.getWindow() != null) {
            return client.getWindow().getScaledWidth();
        }
        return 854;
    }

    @Override
    public int getScaledHeight() {
        MinecraftClient client = MinecraftClient.getInstance();
        if (client != null && client.getWindow() != null) {
            return client.getWindow().getScaledHeight();
        }
        return 480;
    }
}

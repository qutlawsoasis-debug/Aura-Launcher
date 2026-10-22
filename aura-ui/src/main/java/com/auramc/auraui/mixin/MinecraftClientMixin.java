package com.auramc.auraui.mixin;

import com.auramc.auraui.AuraUi;
import net.minecraft.client.MinecraftClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Автопроверка HTML-паузы (-Daura.html.autoshot=true): через 15 с в мире открывает паузу. */
@Mixin(MinecraftClient.class)
public class MinecraftClientMixin {
    @Unique private int aura$worldTicks;
    @Unique private boolean aura$opened;
    @Unique private int aura$diagTicks;

    @Inject(method = "tick", at = @At("TAIL"))
    private void aura$autoshotTick(CallbackInfo ci) {
        if (!Boolean.getBoolean("aura.html.autoshot") || aura$opened) return;
        MinecraftClient mc = (MinecraftClient) (Object) this;
        if (++aura$diagTicks % 200 == 0) {
            AuraUi.LOGGER.info("[aura-ui] AUTOSHOT screen={} world={}",
                mc.currentScreen == null ? "none" : mc.currentScreen.getClass().getName(), mc.world != null);
        }
        // Тестовый мир иногда открывается с вопросом о бэкапе — в автопроверке пропускаем его
        if (mc.currentScreen instanceof net.minecraft.client.gui.screen.BackupPromptScreen prompt) {
            String skip = net.minecraft.text.Text.translatable("selectWorld.backupJoinSkipButton").getString();
            for (var child : prompt.children()) {
                if (child instanceof net.minecraft.client.gui.widget.ButtonWidget b && b.getMessage().getString().equals(skip)) {
                    b.onPress();
                    return;
                }
            }
        }
        if (mc.world == null || mc.player == null || mc.currentScreen != null) return;
        if (++aura$worldTicks == 300) {
            AuraUi.LOGGER.info("[aura-ui] AUTOSHOT world fps={}", mc.getCurrentFps());
            aura$opened = true;
            mc.openPauseMenu(false);
        }
    }
}

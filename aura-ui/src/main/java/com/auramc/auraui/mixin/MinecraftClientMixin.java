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

    @Inject(method = "tick", at = @At("TAIL"))
    private void aura$autoshotTick(CallbackInfo ci) {
        if (!Boolean.getBoolean("aura.html.autoshot") || aura$opened) return;
        MinecraftClient mc = (MinecraftClient) (Object) this;
        if (mc.world == null || mc.player == null || mc.currentScreen != null) return;
        if (++aura$worldTicks == 300) {
            AuraUi.LOGGER.info("[aura-ui] AUTOSHOT world fps={}", mc.getCurrentFps());
            aura$opened = true;
            mc.openPauseMenu(false);
        }
    }
}

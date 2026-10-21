package com.auramc.auraui.mixin;

import com.auramc.auraui.AuraUi;
import com.auramc.auraui.menu.AuraWebScreen;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.GameMenuScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(GameMenuScreen.class)
public class GameMenuScreenMixin {
    @Inject(method = "init", at = @At("HEAD"), cancellable = true)
    private void aura$replaceWithPauseWebScreen(CallbackInfo ci) {
        if (AuraWebScreen.isSupported()) {
            MinecraftClient mc = MinecraftClient.getInstance();
            if (mc != null) {
                mc.setScreen(new AuraWebScreen("pause.html", true));
                ci.cancel();
            }
        } else {
            AuraUi.LOGGER.warn("[aura-ui] MCEF is not initialized, keeping vanilla GameMenuScreen");
        }
    }
}

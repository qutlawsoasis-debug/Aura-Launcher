package com.auramc.auraui.mixin;

import com.auramc.auraui.AuraUi;
import com.auramc.auraui.screen.AuraTitleScreen;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.TitleScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TitleScreen.class)
public class TitleScreenMixin {
    @Inject(method = "init", at = @At("HEAD"), cancellable = true)
    private void aura$replaceWithAuraTitleScreen(CallbackInfo ci) {
        AuraUi.LOGGER.info("[aura-ui] TitleScreenMixin triggered, opening AuraTitleScreen");
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc != null) {
            mc.setScreen(new AuraTitleScreen());
            ci.cancel();
        }
    }
}

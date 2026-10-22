package com.auramc.auraui.mixin;

import com.auramc.auraui.screen.AuraPauseScreen;
import com.auramc.auraui.screen.HtmlScreen;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.screen.GameMenuScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(GameMenuScreen.class)
public class GameMenuScreenMixin {
    @Inject(method = "init", at = @At("HEAD"), cancellable = true)
    private void aura$replaceWithAuraPauseScreen(CallbackInfo ci) {
        MinecraftClient mc = MinecraftClient.getInstance();
        if (mc != null) {
            mc.setScreen(HtmlScreen.isEnabled() ? HtmlScreen.pause() : new AuraPauseScreen());
            ci.cancel();
        }
    }
}

package net.auramc.glass.client.quickmenu.mixin;

import net.auramc.glass.client.quickmenu.*;

import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.hud.InGameHud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(InGameHud.class)
public abstract class QuickMenuCrosshairMixin {
    @Inject(method = "renderCrosshair", at = @At("HEAD"), cancellable = true, require = 1)
    private void auraQuickMenuCrosshair(DrawContext context, CallbackInfo ci) {
        if (QuickMenuEffects.crosshair(context)) ci.cancel();
    }
}

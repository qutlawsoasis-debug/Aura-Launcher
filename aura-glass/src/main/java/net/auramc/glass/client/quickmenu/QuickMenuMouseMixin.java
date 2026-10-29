package net.auramc.glass.client.quickmenu;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.Mouse;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Mouse.class)
public abstract class QuickMenuMouseMixin {
    @Inject(method = "onMouseScroll", at = @At("HEAD"), cancellable = true, require = 1)
    private void auraQuickMenuZoomScroll(long window, double horizontal, double vertical, CallbackInfo ci) {
        if (window == MinecraftClient.getInstance().getWindow().getHandle() && QuickMenuEffects.scroll(vertical)) ci.cancel();
    }
}

package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.GlassDebugOverlay;
import net.minecraft.client.Keyboard;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Keyboard.class)
public abstract class KeyboardMixin {
    @Inject(method = "onKey(JIIII)V", at = @At("HEAD"), cancellable = true)
    private void aura_debugKey(long window, int key, int scanCode, int action, int modifiers, CallbackInfo ci) {
        if (GlassDebugOverlay.handleKey(window, key, scanCode, action)) ci.cancel();
    }
}

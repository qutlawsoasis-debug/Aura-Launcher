package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.GlassDebugOverlay;
import net.minecraft.client.Mouse;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Mouse.class)
public abstract class MouseMixin {
    @Inject(method = "onMouseButton(JIII)V", at = @At("HEAD"), cancellable = true)
    private void aura_debugMouse(long window, int button, int action, int modifiers, CallbackInfo ci) {
        if (GlassDebugOverlay.handleMouse(window, button, action)) ci.cancel();
    }
}

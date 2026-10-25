package net.auramc.defaults.client.mixin;

import net.minecraft.client.realms.gui.screen.RealmsNotificationsScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(RealmsNotificationsScreen.class)
public abstract class RealmsNotificationsScreenMixin {
    @Inject(method = "init", at = @At("HEAD"), cancellable = true)
    private void aura_cancelInit(CallbackInfo ci) {
        ci.cancel();
    }

    @Inject(method = "tick", at = @At("HEAD"), cancellable = true)
    private void aura_cancelTick(CallbackInfo ci) {
        ci.cancel();
    }

    @Inject(method = "checkIfMcoEnabled", at = @At("HEAD"), cancellable = true)
    private void aura_cancelCheckIfMcoEnabled(CallbackInfo ci) {
        ci.cancel();
    }
}

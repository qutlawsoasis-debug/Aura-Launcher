package net.auramc.defaults.client.mixin;

import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.realms.util.PeriodicRunnerFactory$PeriodicRunner")
public abstract class PeriodicRunnerMixin {
    @Inject(method = "run", at = @At("HEAD"), cancellable = true)
    private void aura_cancelRealmsPeriodicRun(long time, CallbackInfo ci) {
        ci.cancel();
    }
}

package net.auramc.defaults.client.mixin;

import net.minecraft.client.realms.RealmsAvailability;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

import java.util.concurrent.CompletableFuture;

@Mixin(RealmsAvailability.class)
public abstract class RealmsAvailabilityMixin {
    @Inject(method = "check", at = @At("HEAD"), cancellable = true)
    private static void aura_disableRealmsAvailabilityCheck(CallbackInfoReturnable<CompletableFuture<RealmsAvailability.Info>> cir) {
        cir.setReturnValue(CompletableFuture.completedFuture(new RealmsAvailability.Info(RealmsAvailability.Type.INCOMPATIBLE_CLIENT)));
    }
}

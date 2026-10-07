package net.auramc.defaults.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.server.PlayerManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(PlayerManager.class)
public abstract class PlayerManagerMixin {
    @Inject(method = "areCheatsAllowed", at = @At("HEAD"), cancellable = true)
    private void aura_lockCheats(CallbackInfoReturnable<Boolean> cir) {
        if (AuraDefaultsConfig.get().lockCheats) {
            cir.setReturnValue(false);
        }
    }
}

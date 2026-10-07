package net.auramc.defaults.client.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.client.gui.screen.world.WorldCreator;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyVariable;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(WorldCreator.class)
public abstract class WorldCreatorMixin {
    @ModifyVariable(method = "setCheatsEnabled", at = @At("HEAD"), argsOnly = true, ordinal = 0)
    private boolean aura_forceCheatsDisabled(boolean cheatsEnabled) {
        if (AuraDefaultsConfig.get().lockCheats) {
            return false;
        }
        return cheatsEnabled;
    }

    @Inject(method = "areCheatsEnabled", at = @At("HEAD"), cancellable = true)
    private void aura_forceAreCheatsEnabledFalse(CallbackInfoReturnable<Boolean> cir) {
        if (AuraDefaultsConfig.get().lockCheats) {
            cir.setReturnValue(false);
        }
    }
}

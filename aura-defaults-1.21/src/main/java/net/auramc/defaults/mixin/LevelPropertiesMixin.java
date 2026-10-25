package net.auramc.defaults.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.world.level.LevelProperties;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(LevelProperties.class)
public abstract class LevelPropertiesMixin {
    @Inject(method = "areCommandsAllowed", at = @At("HEAD"), cancellable = true)
    private void aura_lockCommands(CallbackInfoReturnable<Boolean> cir) {
        if (AuraDefaultsConfig.get().lockCheats) {
            cir.setReturnValue(false);
        }
    }
}

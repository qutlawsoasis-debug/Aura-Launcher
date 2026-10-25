package net.auramc.defaults.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.server.integrated.IntegratedServer;
import net.minecraft.world.GameMode;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

@Mixin(IntegratedServer.class)
public abstract class IntegratedServerMixin {
    @ModifyVariable(method = "openToLan", at = @At("HEAD"), argsOnly = true, ordinal = 0)
    private boolean aura_forceLanCheatsFalse(boolean cheatsAllowed) {
        if (AuraDefaultsConfig.get().lockCheats) {
            return false;
        }
        return cheatsAllowed;
    }
}

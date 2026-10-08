package net.auramc.defaults.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.world.GameRules;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(GameRules.class)
public abstract class GameRulesMixin {
    @Shadow
    public abstract <T extends GameRules.Rule<T>> T get(GameRules.Key<T> key);

    @Inject(method = "<init>()V", at = @At("TAIL"))
    private void aura_setDefaultKeepInventory(CallbackInfo ci) {
        if (AuraDefaultsConfig.get().keepInventoryByDefault) {
            GameRules.BooleanRule rule = this.get(GameRules.KEEP_INVENTORY);
            if (rule != null) {
                rule.set(true, null);
            }
        }
    }
}

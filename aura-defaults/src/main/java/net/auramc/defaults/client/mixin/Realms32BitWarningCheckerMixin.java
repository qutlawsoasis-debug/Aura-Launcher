package net.auramc.defaults.client.mixin;

import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.realms.util.Realms32BitWarningChecker;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Realms32BitWarningChecker.class)
public abstract class Realms32BitWarningCheckerMixin {
    @Inject(method = "showWarningIfNeeded", at = @At("HEAD"), cancellable = true)
    private void aura_cancelRealms32BitWarning(Screen parent, CallbackInfo ci) {
        ci.cancel();
    }
}

package net.auramc.glass.client.mixin;

import net.minecraft.client.Mouse;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Mouse.class)
public abstract class KaraokeQaNoCursorMixin {
    @Inject(method = "lockCursor", at = @At("HEAD"), cancellable = true, require = 0)
    private void aura_noCapture(CallbackInfo ci) {
        if (Boolean.getBoolean("aura.karaoke.demo") || Boolean.getBoolean("aura.qa.headless")) {
            ci.cancel();
        }
    }
}

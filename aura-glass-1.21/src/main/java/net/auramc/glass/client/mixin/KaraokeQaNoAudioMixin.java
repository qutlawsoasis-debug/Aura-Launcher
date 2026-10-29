package net.auramc.glass.client.mixin;

import net.minecraft.client.sound.SoundSystem;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(SoundSystem.class)
public abstract class KaraokeQaNoAudioMixin {
    @Inject(method = "start", at = @At("HEAD"), cancellable = true, require = 0)
    private void aura_noAudio(CallbackInfo ci) {
        if (Boolean.getBoolean("aura.karaoke.demo") || Boolean.getBoolean("aura.qa.headless")) {
            ci.cancel();
        }
    }
}

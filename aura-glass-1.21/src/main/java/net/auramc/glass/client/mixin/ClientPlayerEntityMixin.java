package net.auramc.glass.client.mixin;

import net.auramc.glass.client.karaoke.KaraokeEngine;
import net.minecraft.client.network.ClientPlayerEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ClientPlayerEntity.class)
public abstract class ClientPlayerEntityMixin {
    @Inject(method = "updateHealth", at = @At("HEAD"))
    private void aura_karaokeOnUpdateHealth(float health, CallbackInfo ci) {
        ClientPlayerEntity self = (ClientPlayerEntity) (Object) this;
        if (health < self.getHealth()) {
            KaraokeEngine.getInstance().onPlayerDamage();
        }
    }
}

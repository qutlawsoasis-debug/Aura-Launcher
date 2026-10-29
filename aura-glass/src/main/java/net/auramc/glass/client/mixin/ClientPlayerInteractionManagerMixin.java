package net.auramc.glass.client.mixin;

import net.auramc.glass.client.karaoke.KaraokeEngine;
import net.minecraft.client.network.ClientPlayerInteractionManager;
import net.minecraft.entity.Entity;
import net.minecraft.entity.player.PlayerEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ClientPlayerInteractionManager.class)
public abstract class ClientPlayerInteractionManagerMixin {
    @Inject(method = "attackEntity", at = @At("HEAD"))
    private void aura_karaokeOnAttackEntity(PlayerEntity player, Entity target, CallbackInfo ci) {
        KaraokeEngine.getInstance().onPlayerAttack();
    }
}

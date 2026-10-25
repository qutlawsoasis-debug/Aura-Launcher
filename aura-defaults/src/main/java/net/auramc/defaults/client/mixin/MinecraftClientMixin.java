package net.auramc.defaults.client.mixin;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.RunArgs;
import net.minecraft.client.option.GameOptions;
import net.minecraft.client.realms.RealmsClient;
import net.minecraft.resource.ResourceReload;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(MinecraftClient.class)
public abstract class MinecraftClientMixin {
    @Shadow
    @Final
    public GameOptions options;

    @Inject(method = "onInitFinished", at = @At("HEAD"))
    private void aura_disableOnboarding(RealmsClient realms, ResourceReload reload, RunArgs.QuickPlay quickPlay, CallbackInfo ci) {
        if (this.options.onboardAccessibility) {
            this.options.onboardAccessibility = false;
            this.options.write();
        }
        if (!this.options.skipMultiplayerWarning) {
            this.options.skipMultiplayerWarning = true;
            this.options.write();
        }
    }
}

package net.auramc.defaults.client.mixin;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.GameOptions;
import org.objectweb.asm.Opcodes;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

import java.util.List;

@Mixin(MinecraftClient.class)
public abstract class MinecraftClientMixin {
    @Shadow
    @Final
    public GameOptions options;

    @Inject(method = "createInitScreens", at = @At("HEAD"))
    private void aura_disableAccessibilityOnboarding(List<?> list, CallbackInfo ci) {
        if (this.options != null && this.options.onboardAccessibility) {
            this.options.onboardAccessibility = false;
            this.options.setAccessibilityOnboarded();
        }
    }

    @WrapOperation(
        method = "createInitScreens",
        at = @At(value = "FIELD", target = "Lnet/minecraft/client/option/GameOptions;onboardAccessibility:Z", opcode = Opcodes.GETFIELD)
    )
    private boolean aura_redirectOnboardAccessibility(GameOptions gameOptions,Operation<Boolean> original) {
        return false;
    }
}

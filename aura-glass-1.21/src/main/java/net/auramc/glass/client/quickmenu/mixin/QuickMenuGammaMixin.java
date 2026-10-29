package net.auramc.glass.client.quickmenu.mixin;

import net.auramc.glass.client.quickmenu.*;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.option.SimpleOption;
import net.minecraft.client.render.LightmapTextureManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(LightmapTextureManager.class)
public abstract class QuickMenuGammaMixin {
    @Shadow private boolean dirty;

    @Inject(method = "update", at = @At("HEAD"))
    private void auraQuickMenuForceDirty(float delta, CallbackInfo ci) {
        if (QuickMenuEffects.needsLightmapUpdate()) {
            this.dirty = true;
        }
    }

    @WrapOperation(method = "update", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/option/SimpleOption;getValue()Ljava/lang/Object;"), require = 1)
    private Object auraQuickMenuGamma(SimpleOption<?> option, Operation<Object> original) {
        Object value = original.call(option);
        if (option == MinecraftClient.getInstance().options.getGamma()) return QuickMenuEffects.appliedGamma((Double) value);
        return value;
    }
}

package net.auramc.glass.client.quickmenu;

import net.minecraft.client.render.GameRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(GameRenderer.class)
public abstract class QuickMenuFovMixin {
    @Inject(method = "render", at = @At("HEAD"), require = 1)
    private void auraQuickMenuFrame(CallbackInfo ci) { QuickMenuEffects.beginFrame(); }
    @Inject(method = "getFov", at = @At("RETURN"), cancellable = true, require = 1)
    private void auraQuickMenuFov(CallbackInfoReturnable<Double> cir) {
        cir.setReturnValue(QuickMenuEffects.fov(cir.getReturnValue()));
    }
}

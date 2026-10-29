package net.auramc.glass.client.quickmenu.mixin;

import net.auramc.glass.client.quickmenu.*;

import net.minecraft.client.option.KeyBinding;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Pseudo
@Mixin(targets = "dev.isxander.zoomify.Zoomify", remap = false)
public abstract class QuickMenuZoomifyMixin {
    @Shadow(remap = false) @Final private static KeyBinding zoomKey;
    private static boolean auraOwnsZoom() {
        KeyBinding auraKey = QuickMenuEffects.zoomKey();
        return auraKey != null && QuickMenuConfig.get().zoomEnabled && auraKey.equals(zoomKey);
    }
    @Inject(method = "getZoomDivisor", at = @At("HEAD"), cancellable = true, require = 1, remap = false)
    private static void auraQuickMenuSingleZoom(float delta, CallbackInfoReturnable<Float> cir) {
        if (auraOwnsZoom()) cir.setReturnValue(1f);
    }
    @Inject(method = "getZooming", at = @At("HEAD"), cancellable = true, require = 1, remap = false)
    private void auraQuickMenuNoDuplicateSensitivity(CallbackInfoReturnable<Boolean> cir) {
        if (auraOwnsZoom()) cir.setReturnValue(false);
    }
    @Inject(method = "getPreviousZoomDivisor", at = @At("HEAD"), cancellable = true, require = 1, remap = false)
    private void auraQuickMenuNoDuplicateBobbing(CallbackInfoReturnable<Double> cir) {
        if (auraOwnsZoom()) cir.setReturnValue(1d);
    }
    @Inject(method = "unbindConflicting", at = @At("HEAD"), cancellable = true, require = 1, remap = false)
    private void auraQuickMenuRetainZoomBinding(CallbackInfoReturnable<Boolean> cir) {
        if (auraOwnsZoom()) cir.setReturnValue(false);
    }
    @Inject(method = "detectConflictingToast", at = @At("HEAD"), cancellable = true, require = 1, remap = false)
    private void auraQuickMenuNoDuplicateKeyToast(CallbackInfo ci) {
        if (auraOwnsZoom()) ci.cancel();
    }
}

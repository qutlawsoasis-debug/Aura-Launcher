package net.auramc.glass.client.mixin;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;

import net.auramc.glass.client.ui.GlassDebugOverlay;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.render.GameRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(GameRenderer.class)
public abstract class GameRendererMixin {
    @Inject(method = "render", at = @At("HEAD"))
    private void aura_beginDiagnosticsFrame(CallbackInfo ci) {
        GlassDebugOverlay.beginFrame();
    }

    // This flush occurs before GameRenderer restores the GUI model-view matrix.
    @WrapOperation(method = "render", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/gui/DrawContext;draw()V"))
    private void aura_finishDiagnosticsFrame(DrawContext context,Operation<Void> original) {
        original.call(context);
        net.auramc.glass.client.ui.GlassFrameHooks.finish(context);
        GlassDebugOverlay.render(context);
        context.draw();
        net.auramc.glass.client.ui.GlassGalleryActions.afterFrame();
    }
}

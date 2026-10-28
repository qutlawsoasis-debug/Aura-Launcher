package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.AuraUiRenderer;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Overlay;
import net.minecraft.client.gui.screen.SplashOverlay;
import net.minecraft.resource.ResourceReload;
import net.minecraft.util.Util;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

import java.util.Optional;
import java.util.function.Consumer;

@Mixin(SplashOverlay.class)
public abstract class SplashOverlayMixin extends Overlay {
    @Inject(method = "init", at = @At("TAIL"))
    private static void aura_prewarmNativeRenderer(MinecraftClient client, CallbackInfo ci) {
        net.auramc.glass.common.render.MenuResources.start(client);
        AuraUiRenderer.prepareMenuPanorama();
    }

    @Shadow
    @Final
    private MinecraftClient client;

    @Shadow
    @Final
    private ResourceReload reload;

    @Shadow
    @Final
    private Consumer<Optional<Throwable>> exceptionHandler;

    @Shadow
    private long reloadCompleteTime;

    /**
     * Native compact Aura loading screen with clean non-pixelated stage text and real progress.
     * Transitions instantly once ResourceReload completes while preserving single-shot
     * exception propagation and recovery screens.
     */
    @Inject(method = "render", at = @At("HEAD"), cancellable = true)
    private void aura_renderLightweightLoading(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        if (this.reload.isComplete()) {
            if (this.reloadCompleteTime == -1L) {
                this.reloadCompleteTime = Util.getMeasuringTimeMs();
                try {
                    this.reload.throwException();
                    this.exceptionHandler.accept(Optional.empty());
                } catch (Throwable t) {
                    this.exceptionHandler.accept(Optional.of(t));
                }
            }

            if (this.client.getOverlay() == this) {
                this.client.setOverlay(null);
            }

            ci.cancel();
            return;
        }

        int width = context.getScaledWindowWidth();
        int height = context.getScaledWindowHeight();

        AuraUiRenderer.renderLoadingScreen(context, this.client, this.reload, width, height);

        ci.cancel();
    }
}

package net.auramc.defaults.client.mixin;

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
     * Completely bypass the visual Mojang splash screen (background fill, logo, progress bar, fade delay)
     * while preserving underlying screen rendering, full ResourceReload execution, single-shot exception handling,
     * error recovery screens/overlays, and post-load screen initialization.
     */
    @Inject(method = "render", at = @At("HEAD"), cancellable = true)
    private void aura_suppressMojangSplash(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        ci.cancel();

        // 1. Render the underlying screen if present (e.g. MessageScreen on initial start, or pause menu during F3+T)
        if (this.client.currentScreen != null) {
            this.client.currentScreen.render(context, mouseX, mouseY, delta);
        }

        // 2. Do not proceed or close early until ResourceReload is genuinely complete
        if (this.reload.isComplete()) {
            // Strictly single-shot exceptionHandler invocation
            if (this.reloadCompleteTime == -1L) {
                this.reloadCompleteTime = Util.getMeasuringTimeMs();
                try {
                    this.reload.throwException();
                    this.exceptionHandler.accept(Optional.empty());
                } catch (Throwable t) {
                    this.exceptionHandler.accept(Optional.of(t));
                }
            }

            // Only remove this overlay if it hasn't been replaced by a recovery/error overlay
            if (this.client.getOverlay() == this) {
                this.client.setOverlay(null);
            }

            // Properly initialize the target screen to window dimensions
            if (this.client.currentScreen != null) {
                this.client.currentScreen.init(this.client, context.getScaledWindowWidth(), context.getScaledWindowHeight());
            }
        }
    }
}

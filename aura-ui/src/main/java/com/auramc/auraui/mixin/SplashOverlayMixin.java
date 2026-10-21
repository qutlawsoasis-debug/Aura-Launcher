package com.auramc.auraui.mixin;

import com.auramc.auraui.AuraSplash;
import com.auramc.auraui.AuraTextures;
import java.util.Optional;
import java.util.function.Consumer;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.SplashOverlay;
import net.minecraft.resource.ResourceReload;
import net.minecraft.util.Util;
import net.minecraft.util.math.MathHelper;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Экран Mojang → экран загрузки «Остров».
 * Тайминги, уход оверлея и обработка ошибок перезагрузки — те же, что в SplashOverlay.render;
 * меняется только отрисовка.
 */
@Mixin(SplashOverlay.class)
public abstract class SplashOverlayMixin {
    @Shadow @Final private MinecraftClient client;
    @Shadow @Final private ResourceReload reload;
    @Shadow @Final private Consumer<Optional<Throwable>> exceptionHandler;
    @Shadow @Final private boolean reloading;
    @Shadow private float progress;
    @Shadow private long reloadCompleteTime;
    @Shadow private long reloadStartTime;

    @Inject(method = "init", at = @At("TAIL"))
    private static void auraui$registerTextures(MinecraftClient client, CallbackInfo ci) {
        AuraTextures.register(client);
    }

    @Inject(method = "render", at = @At("HEAD"), cancellable = true)
    private void auraui$render(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        ci.cancel();
        int width = context.getScaledWindowWidth();
        int height = context.getScaledWindowHeight();
        long now = Util.getMeasuringTimeMs();
        if (this.reloading && this.reloadStartTime == -1L) {
            this.reloadStartTime = now;
        }
        float fadeOut = this.reloadCompleteTime > -1L ? (float) (now - this.reloadCompleteTime) / 1000.0f : -1.0f;
        float fadeIn = this.reloadStartTime > -1L ? (float) (now - this.reloadStartTime) / 500.0f : -1.0f;

        float alpha;
        if (fadeOut >= 1.0f) {
            // Загрузка закончилась: под экраном уже рисуется меню, экран тает
            if (this.client.currentScreen != null) {
                this.client.currentScreen.render(context, 0, 0, delta);
            }
            alpha = 1.0f - MathHelper.clamp(fadeOut - 1.0f, 0.0f, 1.0f);
        } else if (this.reloading) {
            // Перезагрузка ресурсов (F3+T, смена пака): экран проявляется поверх текущего меню
            if (this.client.currentScreen != null && fadeIn < 1.0f) {
                this.client.currentScreen.render(context, mouseX, mouseY, delta);
            }
            alpha = MathHelper.clamp(fadeIn, 0.0f, 1.0f);
        } else {
            alpha = 1.0f;
        }

        this.progress = MathHelper.clamp(this.progress * 0.95f + this.reload.getProgress() * 0.050000012f, 0.0f, 1.0f);
        float capsuleAlpha = fadeOut < 0.0f ? 1.0f : 1.0f - MathHelper.clamp(fadeOut, 0.0f, 1.0f);
        float shownProgress = fadeOut >= 0.0f ? 1.0f : this.progress;
        AuraSplash.render(context, alpha, capsuleAlpha, shownProgress);

        if (fadeOut >= 2.0f) {
            this.client.setOverlay(null);
        }
        if (this.reloadCompleteTime == -1L && this.reload.isComplete() && (!this.reloading || fadeIn >= 2.0f)) {
            try {
                this.reload.throwException();
                this.exceptionHandler.accept(Optional.empty());
            } catch (Throwable throwable) {
                this.exceptionHandler.accept(Optional.of(throwable));
            }
            this.reloadCompleteTime = Util.getMeasuringTimeMs();
            if (this.client.currentScreen != null) {
                this.client.currentScreen.init(this.client, width, height);
            }
        }
    }
}

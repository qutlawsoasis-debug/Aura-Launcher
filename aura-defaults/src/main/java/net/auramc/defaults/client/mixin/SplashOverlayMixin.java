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
    @Final
    private boolean reloading;

    @Shadow
    private long reloadCompleteTime;

    @Inject(method = "render", at = @At("HEAD"), cancellable = true)
    private void aura_suppressMojangVisualOverlay(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        ci.cancel();

        // При перезагрузке ресурсов внутри запущенной игры/интерфейса продолжаем отрисовывать текущий экран
        if (this.reloading && this.client.currentScreen != null) {
            this.client.currentScreen.render(context, mouseX, mouseY, delta);
        }

        // Проверка фактического завершения загрузки ресурсов
        if (this.reload.isComplete()) {
            if (this.reloadCompleteTime == -1L) {
                this.reloadCompleteTime = Util.getMeasuringTimeMs();
                try {
                    this.reload.throwException();
                    this.exceptionHandler.accept(Optional.empty());
                } catch (Throwable throwable) {
                    this.exceptionHandler.accept(Optional.of(throwable));
                }
                if (this.client.currentScreen != null) {
                    this.client.currentScreen.init(this.client, context.getScaledWindowWidth(), context.getScaledWindowHeight());
                }
            }
            // Безопасное снятие оверлея только если callback ошибки не назначил другой восстановительный оверлей
            if (this.client.getOverlay() == (Overlay) (Object) this) {
                this.client.setOverlay(null);
            }
        }
    }
}

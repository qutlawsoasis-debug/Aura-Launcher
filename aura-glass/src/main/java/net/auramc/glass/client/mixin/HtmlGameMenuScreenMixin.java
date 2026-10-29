package net.auramc.glass.client.mixin;

import net.auramc.glass.client.html.HtmlPauseBenchmark;
import net.auramc.glass.client.html.HtmlPauseScreen;
import net.auramc.glass.client.html.UltralightNative;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.GameMenuScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(GameMenuScreen.class)
public abstract class HtmlGameMenuScreenMixin {
    @Inject(method = "init", at = @At("HEAD"), cancellable = true)
    private void aura$replaceWithHtmlPause(CallbackInfo ci) {
        if (HtmlPauseBenchmark.allowVanillaMenu) return;
        if (Boolean.getBoolean("aura.htmlPause") && UltralightNative.isAvailable()) {
            MinecraftClient.getInstance().setScreen(new HtmlPauseScreen());
            ci.cancel();
        }
    }

    @Inject(method = "render", at = @At("TAIL"))
    private void aura$benchmarkFrame(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        HtmlPauseBenchmark.onRenderFrame(MinecraftClient.getInstance());
    }
}

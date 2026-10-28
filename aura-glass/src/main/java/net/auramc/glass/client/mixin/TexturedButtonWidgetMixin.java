package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.AuraNativeSurfaces;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.client.gui.widget.TexturedButtonWidget;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TexturedButtonWidget.class)
public abstract class TexturedButtonWidgetMixin extends ClickableWidget {
    protected TexturedButtonWidgetMixin(int x, int y, int w, int h, Text text) { super(x, y, w, h, text); }

    @Inject(method = "renderButton", at = @At("HEAD"), cancellable = true)
    private void aura_renderMenuIcon(DrawContext context, int x, int y, float delta, CallbackInfo ci) {
        if (MinecraftClient.getInstance().currentScreen instanceof TitleScreen
                && AuraNativeSurfaces.menuIcon(context, this, this.isHovered(), this.alpha)) ci.cancel();
    }
}

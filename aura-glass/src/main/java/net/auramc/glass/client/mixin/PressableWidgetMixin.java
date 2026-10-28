package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.AuraUiRenderer;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.client.gui.widget.PressableWidget;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(PressableWidget.class)
public abstract class PressableWidgetMixin extends ClickableWidget {
    public PressableWidgetMixin(int x, int y, int width, int height, Text message) {
        super(x, y, width, height, message);
    }

    @Shadow
    public abstract void drawMessage(DrawContext context, TextRenderer textRenderer, int color);

    @Inject(method = "renderButton", at = @At("HEAD"), cancellable = true)
    private void aura_renderButton(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        if (!net.auramc.glass.client.ui.GlassFrameHooks.styledScreen()) return;
        MinecraftClient client = MinecraftClient.getInstance();
        int textColor = AuraUiRenderer.renderButton(context, this, this.isHovered(), this.alpha);
        if ((Object)this instanceof net.minecraft.client.gui.widget.IconButtonWidget || !net.auramc.glass.client.ui.AuraNativeSurfaces.label(context, this, textColor)) {
            net.auramc.glass.common.render.GlassRenderer.foreground(() -> this.drawMessage(context, client.textRenderer, textColor));
        }
        ci.cancel();
    }
}

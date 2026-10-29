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
    @org.spongepowered.asm.mixin.Unique private DrawContext aura_labelContext;
    @org.spongepowered.asm.mixin.Unique private int aura_labelColor;
    @org.spongepowered.asm.mixin.Unique private final Runnable aura_labelPaint=()->drawMessage(aura_labelContext,MinecraftClient.getInstance().textRenderer,aura_labelColor);

    public PressableWidgetMixin(int x, int y, int width, int height, Text message) {
        super(x, y, width, height, message);
    }

    @Shadow
    public abstract void drawMessage(DrawContext context, TextRenderer textRenderer, int color);

    @Inject(method = "renderButton", at = @At("HEAD"), cancellable = true)
    private void aura_renderButton(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        if (!net.auramc.glass.client.ui.GlassFrameHooks.styledScreen()) return;
        if ((Object)this instanceof net.minecraft.client.gui.widget.PressableTextWidget) return;
        String className = this.getClass().getName();
        if (className.contains("ConfigButton") || className.contains("supplementaries")) return;
        MinecraftClient client = MinecraftClient.getInstance();
        int textColor = AuraUiRenderer.renderButton(context, this, this.isHovered(), this.alpha);
        if ((Object)this instanceof net.minecraft.client.gui.widget.IconButtonWidget || !net.auramc.glass.client.ui.AuraNativeSurfaces.label(context, this, textColor)) {
            aura_labelContext=context;aura_labelColor=textColor;net.auramc.glass.common.render.GlassRenderer.foreground(aura_labelPaint);
        }
        ci.cancel();
    }
}

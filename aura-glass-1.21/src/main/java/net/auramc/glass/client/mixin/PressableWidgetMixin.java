package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.AuraNativeSurfaces;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.TitleScreen;
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

    @Shadow public abstract void drawMessage(DrawContext context, net.minecraft.client.font.TextRenderer textRenderer, int color);
    protected PressableWidgetMixin(int x, int y, int w, int h, Text text) { super(x, y, w, h, text); }

    @Inject(method = "renderWidget", at = @At("HEAD"), cancellable = true)
    private void aura_renderButton(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        if (!net.auramc.glass.client.ui.GlassFrameHooks.styledScreen()) return;
        if ((Object)this instanceof net.minecraft.client.gui.widget.PressableTextWidget) return;
        if (MinecraftClient.getInstance().currentScreen instanceof TitleScreen
                && AuraNativeSurfaces.menuIcon(context, this, this.isHovered(), this.alpha)) {
            ci.cancel();
            return;
        }
        int color = AuraNativeSurfaces.button(context, this, this.isHovered(), this.alpha);
        if ((Object)this instanceof net.minecraft.client.gui.widget.TextIconButtonWidget || !AuraNativeSurfaces.label(context, this, color)) {
            aura_labelContext=context;aura_labelColor=color;net.auramc.glass.common.render.GlassRenderer.foreground(aura_labelPaint);
        }
        ci.cancel();
    }
}

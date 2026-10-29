package net.auramc.glass.client.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.auramc.glass.client.ui.AuraNativeSurfaces;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin({net.minecraft.client.gui.widget.TextIconButtonWidget.WithText.class, net.minecraft.client.gui.widget.TextIconButtonWidget.IconOnly.class})
public abstract class IconButtonWidgetMixin extends ClickableWidget implements Runnable {
    @Unique private Operation<Void> aura_original;
    @Unique private DrawContext aura_context;
    @Unique private Identifier aura_texture;
    @Unique private int aura_x, aura_y, aura_w, aura_h;

    protected IconButtonWidgetMixin(int x, int y, int width, int height, Text message) {
        super(x, y, width, height, message);
    }

    @Inject(method = "renderWidget", at = @At("HEAD"), cancellable = true)
    private void aura_renderTitleIcon(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        if (MinecraftClient.getInstance().currentScreen instanceof TitleScreen
                && AuraNativeSurfaces.menuIcon(context, this, this.isHovered(), this.alpha)) {
            ci.cancel();
        }
    }

    @WrapOperation(method = "renderWidget", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/gui/DrawContext;drawGuiTexture(Lnet/minecraft/util/Identifier;IIII)V"))
    private void aura_queueIcon(DrawContext c, Identifier texture, int x, int y, int w, int h, Operation<Void> original) {
        if (MinecraftClient.getInstance().currentScreen instanceof TitleScreen) {
            return;
        }
        if (!GlassFrameHooks.styledScreen()) {
            original.call(c, texture, x, y, w, h);
            return;
        }
        aura_original = original;
        aura_context = c;
        aura_texture = texture;
        aura_x = x;
        aura_y = y;
        aura_w = w;
        aura_h = h;
        GlassRenderer.foreground(this);
    }

    @Override
    public void run() {
        try {
            if (aura_original != null) {
                aura_original.call(aura_context, aura_texture, aura_x, aura_y, aura_w, aura_h);
            }
        } finally {
            aura_original = null;
            aura_context = null;
        }
    }
}

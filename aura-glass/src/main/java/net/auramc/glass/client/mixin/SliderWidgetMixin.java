package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.*;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.SliderWidget;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(SliderWidget.class)
public abstract class SliderWidgetMixin {
    @Shadow protected double value;
    @Inject(method="renderButton",at=@At("HEAD"),cancellable=true)
    private void aura_slider(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(!GlassFrameHooks.styledScreen())return;
        var self=(SliderWidget)(Object)this;
        net.auramc.glass.client.ui.GlassSlider.paint(c,self,value);
        ci.cancel();
    }
}

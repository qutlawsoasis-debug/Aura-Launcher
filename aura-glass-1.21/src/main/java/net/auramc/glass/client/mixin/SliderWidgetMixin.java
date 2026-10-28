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
    @Inject(method="renderWidget",at=@At("HEAD"),cancellable=true)
    private void aura_slider(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(!GlassFrameHooks.styledScreen())return;
        var self=(SliderWidget)(Object)this;
        int color=AuraNativeSurfaces.button(c,self,self.isHovered(),1);
        var knob=GlassStyles.resolve(GlassPreset.SELECTED_DARK,self.active?self.isFocused()?GlassState.FOCUSED:GlassState.NORMAL:GlassState.DISABLED,1);
        knob.radius=4;knob.light*=.25f;knob.rimOuter*=.35f;knob.gloss*=.5f;
        GlassRenderer.draw(self.getX()+(float)value*(self.getWidth()-8),self.getY()+1,8,self.getHeight()-2,knob,1);
        AuraNativeSurfaces.label(c,self,color);ci.cancel();
    }
}

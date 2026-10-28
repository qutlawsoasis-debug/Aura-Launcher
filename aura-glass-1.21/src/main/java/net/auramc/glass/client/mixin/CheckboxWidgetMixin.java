package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.*;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.CheckboxWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(CheckboxWidget.class)
public abstract class CheckboxWidgetMixin {
    @Inject(method="renderWidget",at=@At("HEAD"),cancellable=true)
    private void aura_check(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(!GlassFrameHooks.styledScreen())return;
        var self=(CheckboxWidget)(Object)this;
        var track=GlassStyles.resolve(self.isChecked()?GlassPreset.SELECTED_DARK:GlassPreset.PANEL,!self.active?GlassState.DISABLED:self.isFocused()?GlassState.FOCUSED:self.isHovered()?GlassState.HOVER:GlassState.NORMAL,1);
        track.radius=6;float y=self.getY()+(self.getHeight()-12)*.5f;
        GlassRenderer.draw(self.getX(),y,20,12,track,1);
        var knob=GlassStyles.get(GlassPreset.BUTTON_FILLED);knob.radius=5;
        GlassRenderer.draw(self.getX()+(self.isChecked()?9:1),y+1,10,10,knob,self.active?1:.5f);
        AuraNativeSurfaces.text(c,self.getMessage().getString(),self.getX()+24,y+1,10,self.active?0xFFFFFFFF:0x80FFFFFF);ci.cancel();
    }
}

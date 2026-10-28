package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.ScrollableWidget;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(ScrollableWidget.class)
public abstract class ScrollableWidgetMixin {
    @Shadow protected abstract double getScrollY();
    @Shadow protected abstract int getMaxScrollY();
    @Shadow protected abstract void renderContents(DrawContext c,int mx,int my,float delta);
    @Inject(method="drawBox(Lnet/minecraft/client/gui/DrawContext;IIII)V",at=@At("HEAD"),cancellable=true)
    private void aura_box(DrawContext c,int x,int y,int w,int h,CallbackInfo ci){
        if(!GlassFrameHooks.styledScreen())return;
        GlassRenderer.draw(x,y,w,h,GlassStyles.frame(GlassPreset.PANEL),1);ci.cancel();
    }
    @Redirect(method="renderButton",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/widget/ScrollableWidget;renderContents(Lnet/minecraft/client/gui/DrawContext;IIF)V"))
    private void aura_contents(ScrollableWidget owner,DrawContext c,int mx,int my,float delta){
        if(!GlassFrameHooks.styledScreen()){renderContents(c,mx,my,delta);return;}
        float[] saved=GlassRenderer.pushClip(owner.getX()+1,owner.getY()+1,owner.getX()+owner.getWidth()-1,owner.getY()+owner.getHeight()-1);
        try{renderContents(c,mx,my,delta);}finally{GlassRenderer.popClip(saved);}
    }
    @Inject(method="drawScrollbar",at=@At("HEAD"),cancellable=true)
    private void aura_scrollbar(DrawContext c,CallbackInfo ci){
        if(!GlassFrameHooks.styledScreen())return;
        var self=(ScrollableWidget)(Object)this;int max=getMaxScrollY();
        if(max>0){float h=Math.max(16,self.getHeight()*self.getHeight()/(float)(self.getHeight()+max));float y=self.getY()+(self.getHeight()-h)*(float)getScrollY()/max;GlassRenderer.draw(self.getX()+self.getWidth()-3,y,2,h,GlassStyles.frame(GlassPreset.BUTTON_FILLED),.85f);}ci.cancel();
    }
}

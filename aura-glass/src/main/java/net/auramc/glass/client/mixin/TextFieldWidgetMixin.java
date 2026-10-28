package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.auramc.glass.common.style.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.TextFieldWidget;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(TextFieldWidget.class)
public abstract class TextFieldWidgetMixin {
    @Unique private boolean aura_replay;
    @Unique private DrawContext aura_context;
    @Unique private int aura_mouseX,aura_mouseY;
    @Unique private float aura_delta;
    @Unique private final Runnable aura_paint=this::aura_paintField;
    @Unique private void aura_paintField(){
        DrawContext c=aura_context;var self=(TextFieldWidget)(Object)this;
        float[] bounds=GlassRenderer.clipBounds();
        if(bounds!=null)c.enableScissor((int)bounds[0],(int)bounds[1],(int)bounds[2],(int)bounds[3]);
        float[] saved=GlassRenderer.pushClip(self.getX(),self.getY(),self.getX()+self.getWidth(),self.getY()+self.getHeight());
        aura_replay=true;
        try{self.renderButton(c,aura_mouseX,aura_mouseY,aura_delta);}
        finally{aura_replay=false;GlassRenderer.popClip(saved);if(bounds!=null)c.disableScissor();aura_context=null;}
    }
    @Inject(method="renderButton",at=@At("HEAD"),cancellable=true)
    private void aura_field(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(aura_replay || !GlassFrameHooks.styledScreen())return;
        var self=(TextFieldWidget)(Object)this;
        var style=GlassStyles.frameResolve(GlassPreset.PANEL,net.auramc.glass.client.ui.GlassWidgetStates.state(self),1);
        style.depth=Math.max(style.depth,.25f);
        GlassRenderer.draw(self.getX(),self.getY(),self.getWidth(),self.getHeight(),style,1);
        aura_context=c;aura_mouseX=mx;aura_mouseY=my;aura_delta=delta;
        GlassRenderer.foreground(aura_paint);ci.cancel();
    }
    @Redirect(method="renderButton",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;fill(IIIII)V",ordinal=0))
    private void aura_noBorder(DrawContext c,int x,int y,int r,int b,int color){if(!aura_replay || !GlassFrameHooks.styledScreen())c.fill(x,y,r,b,color);}
    @Redirect(method="renderButton",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;fill(IIIII)V",ordinal=1))
    private void aura_noBox(DrawContext c,int x,int y,int r,int b,int color){if(!aura_replay || !GlassFrameHooks.styledScreen())c.fill(x,y,r,b,color);}
}

package net.auramc.glass.client.mixin;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.auramc.glass.client.ui.GlassActiveField;
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
        GlassActiveField.set(self,self.getText().isEmpty());
        try{self.renderWidget(c,aura_mouseX,aura_mouseY,aura_delta);}
        finally{GlassActiveField.clear();aura_replay=false;GlassRenderer.popClip(saved);if(bounds!=null)c.disableScissor();aura_context=null;}
    }
    @Inject(method="renderWidget",at=@At("HEAD"),cancellable=true)
    private void aura_field(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(aura_replay || !GlassFrameHooks.styledScreen())return;
        var self=(TextFieldWidget)(Object)this;
        GlassStyle style;
        if(GlassStyles.regular()){
            style=GlassStyles.frame(GlassPreset.PANEL);
            style.asFill();
            style.radius=6;
            style.tintR=118/255f;style.tintG=118/255f;style.tintB=128/255f;style.tintA=0.24f;
            style.rimOuter=0.10f;
            var g=net.auramc.glass.client.ui.GlassGroups.groupAt(self.getX(),self.getY());
            style.rimInner=(g!=null&&g.isLight)?-1.0f:1.0f;
        }else{
            style=GlassStyles.frameResolve(GlassPreset.PANEL,net.auramc.glass.client.ui.GlassWidgetStates.state(self),1);
            style.depth=Math.max(style.depth,.25f);
        }
        GlassRenderer.draw(self.getX(),self.getY(),self.getWidth(),self.getHeight(),style,1);
        aura_context=c;aura_mouseX=mx;aura_mouseY=my;aura_delta=delta;
        GlassRenderer.foreground(aura_paint);ci.cancel();
    }
    @WrapOperation(method="renderWidget",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;drawGuiTexture(Lnet/minecraft/util/Identifier;IIII)V"))
    private void aura_noBox(DrawContext c,net.minecraft.util.Identifier texture,int x,int y,int w,int h,Operation<Void> original){if(!aura_replay || !GlassFrameHooks.styledScreen())original.call(c,texture,x,y,w,h);}
    @WrapOperation(method="renderWidget",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;fill(Lnet/minecraft/client/render/RenderLayer;IIIII)V"))
    private void aura_cursor(DrawContext c,net.minecraft.client.render.RenderLayer layer,int x1,int y1,int x2,int y2,int color,Operation<Void> original){
        if(!aura_replay || !GlassFrameHooks.styledScreen() || !GlassStyles.regular()){
            original.call(c,layer,x1,y1,x2,y2,color);
        } else {
            original.call(c,layer,x1,y1,x2,y2,0xFF64D2FF);
        }
    }
}

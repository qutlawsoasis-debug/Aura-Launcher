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
    @Inject(method="renderWidget",at=@At("HEAD"),cancellable=true)
    private void aura_field(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(aura_replay || !GlassFrameHooks.styledScreen())return;
        var self=(TextFieldWidget)(Object)this;
        var style=GlassStyles.resolve(GlassPreset.PANEL,self.isFocused()?GlassState.FOCUSED:GlassState.NORMAL,1);
        style.depth=Math.max(style.depth,.25f);
        GlassRenderer.draw(self.getX(),self.getY(),self.getWidth(),self.getHeight(),style,1);
        GlassRenderer.foreground(()->{
            float[] clip=GlassRenderer.clipBounds();
            if(clip!=null)c.enableScissor((int)clip[0],(int)clip[1],(int)clip[2],(int)clip[3]);
            aura_replay=true;
            try {GlassRenderer.withClip(self.getX(),self.getY(),self.getX()+self.getWidth(),self.getY()+self.getHeight(),()->self.renderWidget(c,mx,my,delta));}finally{aura_replay=false;if(clip!=null)c.disableScissor();}
        });ci.cancel();
    }
    @Redirect(method="renderWidget",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;drawGuiTexture(Lnet/minecraft/util/Identifier;IIII)V"))
    private void aura_noBox(DrawContext c,net.minecraft.util.Identifier texture,int x,int y,int w,int h){if(!aura_replay || !GlassFrameHooks.styledScreen())c.drawGuiTexture(texture,x,y,w,h);}
}

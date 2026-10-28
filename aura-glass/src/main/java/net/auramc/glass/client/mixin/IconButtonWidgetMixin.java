package net.auramc.glass.client.mixin;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.IconButtonWidget;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.*;

@Mixin(IconButtonWidget.class)
public abstract class IconButtonWidgetMixin implements Runnable {
    @Unique private Operation<Void> aura_original;
    @Unique private DrawContext aura_context;
    @Unique private Identifier aura_texture;
    @Unique private int aura_x,aura_y,aura_u,aura_v,aura_hover,aura_w,aura_h,aura_tw,aura_th;
    @WrapOperation(method="renderButton",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/widget/IconButtonWidget;drawTexture(Lnet/minecraft/client/gui/DrawContext;Lnet/minecraft/util/Identifier;IIIIIIIII)V"))
    private void aura_queueIcon(IconButtonWidget widget,DrawContext c,Identifier texture,int x,int y,int u,int v,int hover,int w,int h,int tw,int th,Operation<Void> original){
        if(!GlassFrameHooks.styledScreen()){original.call(widget,c,texture,x,y,u,v,hover,w,h,tw,th);return;}
        aura_original=original;aura_context=c;aura_texture=texture;aura_x=x;aura_y=y;aura_u=u;aura_v=v;aura_hover=hover;aura_w=w;aura_h=h;aura_tw=tw;aura_th=th;
        GlassRenderer.foreground(this);
    }
    @Override public void run(){try{aura_original.call((IconButtonWidget)(Object)this,aura_context,aura_texture,aura_x,aura_y,aura_u,aura_v,aura_hover,aura_w,aura_h,aura_tw,aura_th);}finally{aura_original=null;aura_context=null;}}
}

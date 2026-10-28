package net.auramc.glass.client.mixin;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.*;

@Mixin({net.minecraft.client.gui.widget.TextIconButtonWidget.WithText.class,net.minecraft.client.gui.widget.TextIconButtonWidget.IconOnly.class})
public abstract class IconButtonWidgetMixin implements Runnable {
    @Unique private Operation<Void> aura_original;
    @Unique private DrawContext aura_context;
    @Unique private Identifier aura_texture;
    @Unique private int aura_x,aura_y,aura_w,aura_h;
    @WrapOperation(method="renderWidget",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;drawGuiTexture(Lnet/minecraft/util/Identifier;IIII)V"))
    private void aura_queueIcon(DrawContext c,Identifier texture,int x,int y,int w,int h,Operation<Void> original){
        if(!GlassFrameHooks.styledScreen()){original.call(c,texture,x,y,w,h);return;}
        aura_original=original;aura_context=c;aura_texture=texture;aura_x=x;aura_y=y;aura_w=w;aura_h=h;GlassRenderer.foreground(this);
    }
    @Override public void run(){try{aura_original.call(aura_context,aura_texture,aura_x,aura_y,aura_w,aura_h);}finally{aura_original=null;aura_context=null;}}
}

package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.common.render.GlassRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.*;

@Mixin({net.minecraft.client.gui.widget.TextIconButtonWidget.WithText.class,net.minecraft.client.gui.widget.TextIconButtonWidget.IconOnly.class})
public abstract class IconButtonWidgetMixin implements Runnable {
    @Unique private DrawContext aura_context;
    @Unique private Identifier aura_texture;
    @Unique private int aura_x,aura_y,aura_w,aura_h;
    @Redirect(method="renderWidget",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;drawGuiTexture(Lnet/minecraft/util/Identifier;IIII)V"))
    private void aura_queueIcon(DrawContext c,Identifier texture,int x,int y,int w,int h){
        if(!GlassFrameHooks.styledScreen()){c.drawGuiTexture(texture,x,y,w,h);return;}
        aura_context=c;aura_texture=texture;aura_x=x;aura_y=y;aura_w=w;aura_h=h;GlassRenderer.foreground(this);
    }
    @Override public void run(){aura_context.drawGuiTexture(aura_texture,aura_x,aura_y,aura_w,aura_h);aura_context=null;}
}

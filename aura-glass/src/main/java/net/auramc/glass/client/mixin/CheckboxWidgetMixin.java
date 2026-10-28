package net.auramc.glass.client.mixin;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.auramc.glass.client.ui.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.CheckboxWidget;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
@Mixin(CheckboxWidget.class)
public abstract class CheckboxWidgetMixin {
    @WrapOperation(method="renderButton",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;drawTexture(Lnet/minecraft/util/Identifier;IIFFIIII)V"))
    private void aura_check(DrawContext c,Identifier texture,int x,int y,float u,float v,int w,int h,int tw,int th,Operation<Void> original){
        if(!GlassFrameHooks.styledScreen()){original.call(c,texture,x,y,u,v,w,h,tw,th);return;}
        GlassChecks.draw((CheckboxWidget)(Object)this,x,y,w,h);
    }
}

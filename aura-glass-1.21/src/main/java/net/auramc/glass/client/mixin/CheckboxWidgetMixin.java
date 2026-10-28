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
    @WrapOperation(method="renderWidget",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;drawGuiTexture(Lnet/minecraft/util/Identifier;IIII)V"))
    private void aura_check(DrawContext c,Identifier texture,int x,int y,int w,int h,Operation<Void> original){
        if(!GlassFrameHooks.styledScreen()){original.call(c,texture,x,y,w,h);return;}
        GlassChecks.draw((CheckboxWidget)(Object)this,x,y,w,h);
    }
}

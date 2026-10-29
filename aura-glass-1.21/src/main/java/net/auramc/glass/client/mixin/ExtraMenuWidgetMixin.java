package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.client.gui.widget.ToggleButtonWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ToggleButtonWidget.class)
public abstract class ExtraMenuWidgetMixin {
    @Inject(method="renderWidget",at=@At("HEAD"),cancellable=true)
    private void aura_menuWidget(DrawContext context,int mx,int my,float delta,CallbackInfo ci){
        if(!GlassFrameHooks.styledScreen())return;
        var widget=(ClickableWidget)(Object)this;
        AuraNativeSurfaces.label(context,widget,AuraNativeSurfaces.button(context,widget,widget.isHovered(),1));
        ci.cancel();
    }
}

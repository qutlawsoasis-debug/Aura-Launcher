package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.*;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.TabButtonWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(TabButtonWidget.class)
public abstract class TabButtonWidgetMixin {
    @Inject(method="renderButton",at=@At("HEAD"),cancellable=true)
    private void aura_tab(DrawContext c,int mx,int my,float delta,CallbackInfo ci) {
        if(!GlassFrameHooks.styledScreen())return;
        var self=(TabButtonWidget)(Object)this;
        AuraNativeSurfaces.label(c,self,AuraNativeSurfaces.button(c,self,self.isHovered(),1));ci.cancel();
    }
}

package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.TabNavigationWidget;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
@Mixin(TabNavigationWidget.class)
public abstract class TabNavigationWidgetMixin {
    @Redirect(method="render",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;fill(IIIII)V"))
    private void aura_noBlackHeader(DrawContext c,int x,int y,int r,int b,int color){if(!GlassFrameHooks.styledScreen())c.fill(x,y,r,b,color);}
}

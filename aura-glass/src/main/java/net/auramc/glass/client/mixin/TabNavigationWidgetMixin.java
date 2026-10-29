package net.auramc.glass.client.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.GridWidget;
import net.minecraft.client.gui.widget.TabNavigationWidget;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TabNavigationWidget.class)
public abstract class TabNavigationWidgetMixin {
    @Shadow @Final private GridWidget grid;

    @Inject(method="init", at=@At("TAIL"))
    private void aura_initTabs(CallbackInfo ci) {
        if (GlassFrameHooks.styledScreen()) {
            this.grid.setY(6);
        }
    }

    @WrapOperation(method="render",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/DrawContext;fill(IIIII)V"))
    private void aura_noBlackHeader(DrawContext c,int x,int y,int r,int b,int color,Operation<Void> original){
        if(!GlassFrameHooks.styledScreen())original.call(c,x,y,r,b,color);
    }
}

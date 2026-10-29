package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.GlassFrameHooks;
import net.minecraft.client.gui.widget.DirectionalLayoutWidget;
import net.minecraft.client.gui.widget.TabNavigationWidget;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TabNavigationWidget.class)
public abstract class TabNavigationWidgetMixin {
    @Shadow @Final private DirectionalLayoutWidget grid;

    @Inject(method="init", at=@At("TAIL"))
    private void aura_initTabs(CallbackInfo ci) {
        if (GlassFrameHooks.styledScreen()) {
            this.grid.setY(6);
        }
    }
}

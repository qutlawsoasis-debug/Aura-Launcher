package net.auramc.glass.client.mixin;

import com.google.common.collect.ImmutableList;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.auramc.glass.client.ui.TabSegmentControl;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.widget.GridWidget;
import net.minecraft.client.gui.widget.TabButtonWidget;
import net.minecraft.client.gui.widget.TabNavigationWidget;
import org.spongepowered.asm.mixin.*;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TabNavigationWidget.class)
public abstract class TabNavigationWidgetMixin {
    @Shadow @Final private GridWidget grid;
    @Shadow @Final private ImmutableList<TabButtonWidget> tabButtons;

    @Inject(method="init", at=@At("TAIL"))
    private void aura_initTabs(CallbackInfo ci) {
        if (GlassFrameHooks.styledScreen()) {
            this.grid.setY(6);
            this.grid.refreshPositions();
        }
    }

    @Inject(method="render", at=@At("HEAD"), cancellable=true)
    private void aura_renderSegmentControl(DrawContext context, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        if (GlassFrameHooks.styledScreen()) {
            TabSegmentControl.render(context, this.tabButtons, mouseX, mouseY, delta);
            ci.cancel();
        }
    }
}

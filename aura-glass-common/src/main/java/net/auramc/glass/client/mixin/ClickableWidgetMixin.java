package net.auramc.glass.client.mixin;

import net.auramc.glass.client.ui.AuraResizableWidget;
import net.auramc.glass.client.ui.AuraMotionWidget;
import net.auramc.glass.common.render.GlassSpring;
import org.spongepowered.asm.mixin.Unique;
import net.minecraft.client.gui.widget.ClickableWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(ClickableWidget.class)
public abstract class ClickableWidgetMixin implements AuraResizableWidget, AuraMotionWidget {
    @Unique private final GlassSpring aura_hoverAnimation = new GlassSpring();
    @Unique private final GlassSpring aura_glassHover = new GlassSpring();
    @Unique private final GlassSpring aura_glassPress = new GlassSpring();

    @Override public float aura_glassHover(float target) { return aura_glassHover.approach(target, System.nanoTime()); }
    @Override public float aura_glassPress(float target) { return aura_glassPress.approach(target, System.nanoTime()); }

    @Override
    public float aura_hover(float target) {
        return aura_hoverAnimation.approach(target, System.nanoTime());
    }
    @Shadow
    public int width;

    @Shadow
    protected int height;

    @Override
    public void aura_setDimensions(int width, int height) {
        this.width = width;
        this.height = height;
    }
}

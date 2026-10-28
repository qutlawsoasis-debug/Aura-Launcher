package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(Screen.class)
public abstract class ScreenMixin {
    @Inject(method="renderWithTooltip",at=@At("HEAD"))
    private void aura_background(DrawContext c,int mx,int my,float delta,CallbackInfo ci){GlassFrameHooks.beforeScreen(c);}
    @Inject(method="renderBackground",at=@At("HEAD"),cancellable=true)
    private void aura_replaceBackground(DrawContext c, int mouseX, int mouseY, float delta,CallbackInfo ci){if(GlassFrameHooks.styledScreen())ci.cancel();}
    @Inject(method="renderInGameBackground",at=@At("HEAD"),cancellable=true)
    private void aura_noDirt(DrawContext c,CallbackInfo ci){if(GlassFrameHooks.styledScreen())ci.cancel();}
}

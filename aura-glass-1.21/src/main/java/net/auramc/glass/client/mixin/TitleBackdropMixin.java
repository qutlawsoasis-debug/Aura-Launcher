package net.auramc.glass.client.mixin;
import net.auramc.glass.client.ui.GlassFrameHooks;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.TitleScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TitleScreen.class)
public abstract class TitleBackdropMixin {
    @Inject(method="render",at=@At(value="INVOKE",target="Lnet/minecraft/client/gui/screen/TitleScreen;renderPanoramaBackground(Lnet/minecraft/client/gui/DrawContext;F)V",shift=At.Shift.AFTER))
    private void aura_capturePanorama(DrawContext context,int mx,int my,float delta,CallbackInfo ci){GlassFrameHooks.titleBackdrop(context);}
}

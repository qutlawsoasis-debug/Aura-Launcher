package net.auramc.glass.client.mixin;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.hud.InGameHud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(InGameHud.class)
public abstract class AuraHudMixin {
    @Inject(method="render",at=@At("TAIL"))
    private void aura_hud(DrawContext context, float delta,CallbackInfo ci){net.auramc.glass.common.hud.HudRuntime.render(context);}
}

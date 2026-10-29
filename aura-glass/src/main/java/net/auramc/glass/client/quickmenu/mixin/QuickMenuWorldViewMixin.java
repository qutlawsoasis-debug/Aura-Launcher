package net.auramc.glass.client.quickmenu.mixin;

import net.auramc.glass.client.quickmenu.*;

import net.minecraft.client.world.ClientWorld;
import net.minecraft.world.World;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(World.class)
public abstract class QuickMenuWorldViewMixin {
    @Inject(method = "getTimeOfDay", at = @At("HEAD"), cancellable = true, require = 1)
    private void auraQuickMenuVisualTime(CallbackInfoReturnable<Long> cir) {
        if ((Object) this instanceof ClientWorld && QuickMenuConfig.get().visualTime) cir.setReturnValue((long) QuickMenuConfig.get().timeOfDay % 24000L);
    }
    @Inject(method = {"getRainGradient", "getThunderGradient"}, at = @At("HEAD"), cancellable = true, require = 1)
    private void auraQuickMenuVisualWeather(float delta, CallbackInfoReturnable<Float> cir) {
        if ((Object) this instanceof ClientWorld && QuickMenuConfig.get().clearWeather) cir.setReturnValue(0f);
    }
}

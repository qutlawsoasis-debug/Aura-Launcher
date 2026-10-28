package net.auramc.glass.client.mixin;

import net.auramc.glass.common.render.MenuResources;
import net.minecraft.client.MinecraftClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(MinecraftClient.class)
public abstract class MenuResizeMixin {
    @Inject(method="onResolutionChanged",at=@At("TAIL"))
    private void aura_prepareMenuResources(CallbackInfo ci){MenuResources.prepare((MinecraftClient)(Object)this);}
}

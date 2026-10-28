package net.auramc.defaults.client.mixin.quiet.dh;

import net.auramc.defaults.client.quiet.AuraQuietMod;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Pseudo
@Mixin(targets = "com.seibel.distanthorizons.core.jar.updater.SelfUpdater", remap = false)
public abstract class SelfUpdaterMixin {
    private static boolean auraQuiet$logged = false;

    @Inject(method = "onStart()Z", at = @At("HEAD"), cancellable = true, remap = false, require = 0)
    private static void auraQuiet$disableBuiltInUpdater(CallbackInfoReturnable<Boolean> cir) {
        cir.setReturnValue(false);
        if (!auraQuiet$logged) {
            auraQuiet$logged = true;
            AuraQuietMod.LOGGER.info("[aura-quiet] Distant Horizons built-in updater blocked; mod updates are delivered by Aura-Pack");
        }
    }
}

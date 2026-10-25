package net.auramc.defaults.client.mixin;

import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.multiplayer.MultiplayerScreen;
import net.minecraft.client.gui.screen.multiplayer.MultiplayerWarningScreen;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(MultiplayerWarningScreen.class)
public abstract class MultiplayerWarningScreenMixin extends Screen {
    @Shadow
    @Final
    private Screen parent;

    protected MultiplayerWarningScreenMixin(Text title) {
        super(title);
    }

    @Inject(method = "init", at = @At("HEAD"), cancellable = true)
    private void aura_bypassMultiplayerWarning(CallbackInfo ci) {
        if (this.client != null) {
            this.client.options.skipMultiplayerWarning = true;
            this.client.options.write();
            this.client.setScreen(new MultiplayerScreen(this.parent));
            ci.cancel();
        }
    }
}

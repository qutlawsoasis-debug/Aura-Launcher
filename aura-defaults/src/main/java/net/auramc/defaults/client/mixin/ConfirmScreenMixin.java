package net.auramc.defaults.client.mixin;

import it.unimi.dsi.fastutil.booleans.BooleanConsumer;
import net.minecraft.client.gui.screen.ConfirmScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;
import net.minecraft.text.TranslatableTextContent;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ConfirmScreen.class)
public abstract class ConfirmScreenMixin extends Screen {
    @Shadow
    @Final
    protected BooleanConsumer callback;

    @Shadow
    @Final
    private Text message;

    protected ConfirmScreenMixin(Text title) {
        super(title);
    }

    @Inject(method = "init", at = @At("HEAD"), cancellable = true)
    private void aura_autoConfirmExperimental(CallbackInfo ci) {
        boolean isExperimental = false;
        if (this.getTitle().getContent() instanceof TranslatableTextContent translatableTitle
                && "selectWorld.warning.experimental.title".equals(translatableTitle.getKey())) {
            isExperimental = true;
        } else if (this.message != null && this.message.getContent() instanceof TranslatableTextContent translatableMsg
                && "selectWorld.warning.experimental.question".equals(translatableMsg.getKey())) {
            isExperimental = true;
        }

        if (isExperimental) {
            this.callback.accept(true);
            ci.cancel();
        }
    }
}

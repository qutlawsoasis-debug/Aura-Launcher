package net.auramc.defaults.client.mixin;

import net.minecraft.client.gui.screen.BackupPromptScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;
import net.minecraft.text.TranslatableTextContent;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(BackupPromptScreen.class)
public abstract class BackupPromptScreenMixin extends Screen {
    @Shadow
    @Final
    protected BackupPromptScreen.Callback callback;

    protected BackupPromptScreenMixin(Text title) {
        super(title);
    }

    @Inject(method = "init", at = @At("HEAD"), cancellable = true)
    private void aura_autoSkipExperimentalBackupPrompt(CallbackInfo ci) {
        if (this.getTitle().getContent() instanceof TranslatableTextContent translatable
                && "selectWorld.backupQuestion.experimental".equals(translatable.getKey())) {
            this.callback.proceed(false, false);
            ci.cancel();
        }
    }
}

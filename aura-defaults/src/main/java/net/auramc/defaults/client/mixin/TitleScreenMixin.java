package net.auramc.defaults.client.mixin;

import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.text.Text;
import net.minecraft.text.TranslatableTextContent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

import java.util.ArrayList;
import java.util.List;

@Mixin(TitleScreen.class)
public abstract class TitleScreenMixin extends Screen {
    protected TitleScreenMixin(Text title) {
        super(title);
    }

    @Inject(method = "init", at = @At("HEAD"))
    private void aura_ensureSkipMultiplayerWarning(CallbackInfo ci) {
        if (this.client != null && !this.client.options.skipMultiplayerWarning) {
            this.client.options.skipMultiplayerWarning = true;
            this.client.options.write();
        }
    }

    @Inject(method = "initWidgetsNormal", at = @At("TAIL"))
    private void aura_removeRealmsButton(int y, int spacingY, CallbackInfo ci) {
        List<Element> toRemove = new ArrayList<>();
        for (Element child : this.children()) {
            if (child instanceof ButtonWidget button) {
                if (button.getMessage().getContent() instanceof TranslatableTextContent translatable
                        && "menu.online".equals(translatable.getKey())) {
                    toRemove.add(button);
                }
            }
        }
        for (Element child : toRemove) {
            this.remove(child);
        }
    }

    @Inject(method = "isRealmsNotificationsGuiDisplayed", at = @At("HEAD"), cancellable = true)
    private void aura_hideRealmsNotifications(CallbackInfoReturnable<Boolean> cir) {
        cir.setReturnValue(false);
    }
}

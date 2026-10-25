package net.auramc.defaults.client.mixin;

import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.screen.TitleScreen;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.option.GameOptions;
import net.minecraft.text.Text;
import org.objectweb.asm.Opcodes;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

import java.util.ArrayList;
import java.util.List;

@Mixin(TitleScreen.class)
public abstract class TitleScreenMixin extends Screen {
    protected TitleScreenMixin(Text title) {
        super(title);
    }

    @Redirect(
        method = "onMultiplayerButtonPressed",
        at = @At(value = "FIELD", target = "Lnet/minecraft/client/option/GameOptions;skipMultiplayerWarning:Z", opcode = Opcodes.GETFIELD)
    )
    private boolean aura_alwaysSkipMultiplayerWarning(GameOptions options) {
        return true;
    }

    @Inject(method = "isRealmsNotificationsGuiDisplayed", at = @At("HEAD"), cancellable = true)
    private void aura_hideRealmsNotifications(CallbackInfoReturnable<Boolean> cir) {
        cir.setReturnValue(false);
    }

    @Inject(method = "initWidgetsNormal", at = @At("TAIL"))
    private void aura_removeRealmsButton(int y, int spacing, CallbackInfo ci) {
        Text realmsText = Text.translatable("menu.online");
        List<Element> toRemove = new ArrayList<>();
        for (Element child : this.children()) {
            if (child instanceof ButtonWidget button && button.getMessage() != null) {
                if (button.getMessage().getString().equals(realmsText.getString())) {
                    toRemove.add(button);
                }
            }
        }
        for (Element child : toRemove) {
            this.remove(child);
        }
    }
}

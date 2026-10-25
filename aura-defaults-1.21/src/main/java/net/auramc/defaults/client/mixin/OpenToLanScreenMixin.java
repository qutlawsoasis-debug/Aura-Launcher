package net.auramc.defaults.client.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.screen.OpenToLanScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.tooltip.Tooltip;
import net.minecraft.client.gui.widget.CyclingButtonWidget;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(OpenToLanScreen.class)
public abstract class OpenToLanScreenMixin extends Screen {
    @Shadow
    private boolean allowCommands;

    protected OpenToLanScreenMixin(Text title) {
        super(title);
    }

    @Inject(method = "init", at = @At("TAIL"))
    private void aura_disableLanCheatsButton(CallbackInfo ci) {
        if (!AuraDefaultsConfig.get().lockCheats) {
            return;
        }

        this.allowCommands = false;
        Text allowCommandsText = Text.translatable("selectWorld.allowCommands");
        Tooltip disabledTooltip = Tooltip.of(Text.literal("Отключено в Aura"));

        for (Element element : this.children()) {
            if (element instanceof CyclingButtonWidget<?> button) {
                if (button.getMessage() != null && button.getMessage().getString().contains(allowCommandsText.getString())) {
                    @SuppressWarnings("unchecked")
                    CyclingButtonWidget<Boolean> boolButton = (CyclingButtonWidget<Boolean>) button;
                    boolButton.setValue(false);
                    boolButton.active = false;
                    boolButton.setTooltip(disabledTooltip);
                }
            }
        }
    }
}

package net.auramc.defaults.client.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.client.gui.screen.world.CreateWorldScreen;
import net.minecraft.client.gui.tab.TabManager;
import net.minecraft.client.gui.tooltip.Tooltip;
import net.minecraft.client.gui.widget.CyclingButtonWidget;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(CreateWorldScreen.class)
public abstract class CreateWorldScreenMixin {
    @Shadow
    @Final
    private TabManager tabManager;

    @Inject(method = "initTabNavigation", at = @At("TAIL"))
    private void aura_lockCreateWorldCheats(CallbackInfo ci) {
        if (!AuraDefaultsConfig.get().lockCheats) {
            return;
        }

        Text allowCommandsText = Text.translatable("selectWorld.allowCommands");
        Tooltip disabledTooltip = Tooltip.of(Text.literal("Отключено в Aura"));

        if (this.tabManager != null && this.tabManager.getCurrentTab() != null) {
            this.tabManager.getCurrentTab().forEachChild(widget -> {
                if (widget instanceof CyclingButtonWidget<?> button) {
                    if (button.getMessage() != null && button.getMessage().getString().contains(allowCommandsText.getString())) {
                        @SuppressWarnings("unchecked")
                        CyclingButtonWidget<Boolean> boolButton = (CyclingButtonWidget<Boolean>) button;
                        boolButton.setValue(false);
                        boolButton.active = false;
                        boolButton.setTooltip(disabledTooltip);
                    }
                }
            });
        }
    }
}

package net.auramc.defaults.client.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.client.gui.screen.world.CreateWorldScreen;
import net.minecraft.client.gui.tab.Tab;
import net.minecraft.client.gui.tab.TabManager;
import net.minecraft.client.gui.tooltip.Tooltip;
import net.minecraft.client.gui.widget.CyclingButtonWidget;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TabManager.class)
public abstract class TabManagerMixin {
    @Inject(method = "setCurrentTab", at = @At("TAIL"))
    private void aura_onTabChanged(Tab tab, boolean playSound, CallbackInfo ci) {
        if (!AuraDefaultsConfig.get().lockCheats) {
            return;
        }

        if (tab != null) {
            Text allowCommandsText = Text.translatable("selectWorld.allowCommands");
            Tooltip disabledTooltip = Tooltip.of(Text.literal("Отключено в Aura"));

            tab.forEachChild(widget -> {
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

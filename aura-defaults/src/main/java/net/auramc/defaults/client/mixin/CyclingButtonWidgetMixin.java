package net.auramc.defaults.client.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.client.gui.tooltip.Tooltip;
import net.minecraft.client.gui.widget.CyclingButtonWidget;
import net.minecraft.client.gui.widget.PressableWidget;
import net.minecraft.screen.ScreenTexts;
import net.minecraft.text.Text;
import net.minecraft.text.TranslatableTextContent;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(CyclingButtonWidget.class)
public abstract class CyclingButtonWidgetMixin<T> extends PressableWidget {
    @Shadow
    @Final
    private Text optionText;

    @Shadow
    @Mutable
    private T value;

    @Shadow
    private int index;

    protected CyclingButtonWidgetMixin(int x, int y, int width, int height, Text message) {
        super(x, y, width, height, message);
    }

    @Unique
    private boolean aura_isAllowCommandsButton() {
        if (!AuraDefaultsConfig.get().lockCheats || this.optionText == null) {
            return false;
        }
        if (this.optionText.getContent() instanceof TranslatableTextContent translatable) {
            return "selectWorld.allowCommands".equals(translatable.getKey());
        }
        String expected = Text.translatable("selectWorld.allowCommands").getString();
        return !expected.isEmpty() && this.optionText.getString().contains(expected);
    }

    @Unique
    @SuppressWarnings("unchecked")
    private void aura_enforceLockedOff() {
        if (!aura_isAllowCommandsButton()) {
            return;
        }
        if (this.value instanceof Boolean) {
            this.value = (T) Boolean.FALSE;
            this.index = 1;
            this.setMessage(ScreenTexts.composeGenericOptionText(this.optionText, ScreenTexts.OFF));
        }
        this.active = false;
        this.setTooltip(Tooltip.of(Text.literal("Отключено в Aura")));
    }

    @Inject(method = "<init>", at = @At("TAIL"))
    private void aura_onInit(CallbackInfo ci) {
        aura_enforceLockedOff();
    }

    @Inject(method = "setValue", at = @At("TAIL"))
    private void aura_onSetValue(T value, CallbackInfo ci) {
        aura_enforceLockedOff();
    }

    @Inject(method = "onPress", at = @At("HEAD"), cancellable = true)
    private void aura_onPress(CallbackInfo ci) {
        if (aura_isAllowCommandsButton()) {
            aura_enforceLockedOff();
            ci.cancel();
        }
    }

    @Inject(method = "mouseScrolled", at = @At("HEAD"), cancellable = true)
    private void aura_onMouseScrolled(double mouseX, double mouseY, double amount, CallbackInfoReturnable<Boolean> cir) {
        if (aura_isAllowCommandsButton()) {
            aura_enforceLockedOff();
            cir.setReturnValue(false);
        }
    }
}

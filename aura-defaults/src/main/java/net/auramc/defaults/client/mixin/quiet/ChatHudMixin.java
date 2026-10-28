package net.auramc.defaults.client.mixin.quiet;

import net.auramc.defaults.client.quiet.AuraQuietMod;
import net.auramc.defaults.client.quiet.QuietChatRules;
import net.minecraft.client.gui.hud.ChatHud;
import net.minecraft.client.gui.hud.MessageIndicator;
import net.minecraft.network.message.MessageSignatureData;
import net.minecraft.text.Text;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ChatHud.class)
public abstract class ChatHudMixin {

    @Inject(method = "addMessage(Lnet/minecraft/text/Text;)V", at = @At("HEAD"), cancellable = true)
    private void auraQuiet$suppressSimple(Text message, CallbackInfo ci) {
        if (QuietChatRules.shouldSuppress(message)) {
            AuraQuietMod.LOGGER.info("[aura-quiet] Suppressed mod chat message: {}", QuietChatRules.describe(message));
            ci.cancel();
        }
    }

    @Inject(method = "addMessage(Lnet/minecraft/text/Text;Lnet/minecraft/network/message/MessageSignatureData;Lnet/minecraft/client/gui/hud/MessageIndicator;)V", at = @At("HEAD"), cancellable = true)
    private void auraQuiet$suppressFull(Text message, MessageSignatureData signature, MessageIndicator indicator, CallbackInfo ci) {
        if (QuietChatRules.shouldSuppress(message)) {
            AuraQuietMod.LOGGER.info("[aura-quiet] Suppressed mod chat message: {}", QuietChatRules.describe(message));
            ci.cancel();
        }
    }
}

package net.auramc.defaults.mixin;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.minecraft.server.PlayerManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(PlayerManager.class)
public abstract class PlayerManagerMixin {
    @org.spongepowered.asm.mixin.Shadow
    public abstract net.minecraft.server.MinecraftServer getServer();

    @org.spongepowered.asm.mixin.Shadow
    public abstract net.minecraft.server.BannedPlayerList getUserBanList();

    @Inject(method = "areCheatsAllowed", at = @At("HEAD"), cancellable = true)
    private void aura_lockCheats(CallbackInfoReturnable<Boolean> cir) {
        if (AuraDefaultsConfig.get().lockCheats) {
            cir.setReturnValue(false);
        }
    }

    @Inject(method = "checkCanJoin", at = @At("HEAD"), cancellable = true)
    private void aura_checkBannedPlayer(java.net.SocketAddress address, com.mojang.authlib.GameProfile profile, CallbackInfoReturnable<net.minecraft.text.Text> cir) {
        if (this.getServer() instanceof net.minecraft.server.integrated.IntegratedServer integratedServer && integratedServer.isHost(profile)) {
            return;
        }

        net.minecraft.server.BannedPlayerList banList = this.getUserBanList();
        if (banList != null) {
            for (net.minecraft.server.BannedPlayerEntry entry : banList.values()) {
                com.mojang.authlib.GameProfile bannedProfile = (com.mojang.authlib.GameProfile) ((ServerConfigEntryAccessor) entry).aura_getKey();
                if (bannedProfile != null) {
                    boolean idMatch = profile.getId() != null && profile.getId().equals(bannedProfile.getId());
                    boolean nameMatch = profile.getName() != null && bannedProfile.getName() != null &&
                            profile.getName().equalsIgnoreCase(bannedProfile.getName());

                    if (idMatch || nameMatch) {
                        String reason = entry.getReason();
                        cir.setReturnValue(net.minecraft.text.Text.literal("\u00a7c\u0412\u044b \u0437\u0430\u0431\u0430\u043d\u0435\u043d\u044b \u043d\u0430 \u044d\u0442\u043e\u043c \u0441\u0435\u0440\u0432\u0435\u0440\u0435!\n\u00a77\u041f\u0440\u0438\u0447\u0438\u043d\u0430: \u00a7f" +
                                (reason != null && !reason.isBlank() ? reason : "\u0417\u0430\u0431\u0430\u043d\u0435\u043d \u0445\u043e\u0441\u0442\u043e\u043c")));
                        return;
                    }
                }
            }
        }
    }
}

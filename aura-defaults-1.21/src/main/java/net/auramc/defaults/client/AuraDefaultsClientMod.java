package net.auramc.defaults.client;

import net.fabricmc.api.ClientModInitializer;

public class AuraDefaultsClientMod implements ClientModInitializer {
    @Override
    public void onInitializeClient() {
        AuraUpdateNoticeHandler.init();
    }
}

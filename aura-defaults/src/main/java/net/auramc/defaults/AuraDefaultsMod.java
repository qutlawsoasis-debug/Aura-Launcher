package net.auramc.defaults;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.fabricmc.api.ModInitializer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class AuraDefaultsMod implements ModInitializer {
    public static final String MOD_ID = "aura-defaults";
    public static final Logger LOGGER = LoggerFactory.getLogger(MOD_ID);

    @Override
    public void onInitialize() {
        AuraDefaultsConfig.load();
        LOGGER.info("[aura-defaults] Initialized (keepInventoryByDefault={}, lockCheats={})",
                AuraDefaultsConfig.get().keepInventoryByDefault,
                AuraDefaultsConfig.get().lockCheats);
    }
}

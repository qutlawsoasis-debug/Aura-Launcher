package net.auramc.defaults.client.quiet;

import net.fabricmc.api.ClientModInitializer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class AuraQuietMod implements ClientModInitializer {
    public static final Logger LOGGER = LoggerFactory.getLogger("aura-quiet");

    @Override
    public void onInitializeClient() {
        LOGGER.info("[aura-quiet] Mod message suppression active (targeted chat filter + Distant Horizons updater gate)");
    }
}

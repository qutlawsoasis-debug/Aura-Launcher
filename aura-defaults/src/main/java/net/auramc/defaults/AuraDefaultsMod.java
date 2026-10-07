package net.auramc.defaults;

import net.auramc.defaults.config.AuraDefaultsConfig;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerWorldEvents;
import net.minecraft.world.GameRules;
import net.minecraft.world.World;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class AuraDefaultsMod implements ModInitializer {
    public static final String MOD_ID = "aura-defaults";
    public static final Logger LOGGER = LoggerFactory.getLogger(MOD_ID);

    @Override
    public void onInitialize() {
        AuraDefaultsConfig.load();

        ServerWorldEvents.LOAD.register((server, world) -> {
            if (world.getRegistryKey() == World.OVERWORLD && world.getTime() == 0L) {
                if (AuraDefaultsConfig.get().keepInventoryByDefault) {
                    world.getGameRules().get(GameRules.KEEP_INVENTORY).set(true, server);
                    LOGGER.info("[aura-defaults] Set keepInventory to true for new world");
                }
            }
        });
    }
}

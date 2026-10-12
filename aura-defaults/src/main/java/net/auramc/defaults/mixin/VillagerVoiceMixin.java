package net.auramc.defaults.mixin;

import net.minecraft.entity.EntityType;
import net.minecraft.entity.passive.MerchantEntity;
import net.minecraft.entity.passive.VillagerEntity;
import net.minecraft.world.World;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(VillagerEntity.class)
public abstract class VillagerVoiceMixin extends MerchantEntity {
    protected VillagerVoiceMixin(EntityType<? extends MerchantEntity> entityType, World world) {
        super(entityType, world);
    }

    @Override
    public float getSoundPitch() {
        return 1.0f;
    }

    @Override
    public int getMinAmbientSoundDelay() {
        return 240;
    }
}

package com.auramc.rustbeds;

import com.mojang.authlib.GameProfile;
import net.minecraft.component.DataComponentTypes;
import net.minecraft.component.type.ProfileComponent;
import net.minecraft.item.ItemStack;

final class HeadAssertions {
    static void verify(ItemStack head, GameProfile expected) {
        ProfileComponent actual = head.get(DataComponentTypes.PROFILE);
        if (actual == null || !expected.getId().equals(actual.gameProfile().getId())) {
            throw new AssertionError("PROFILE component must retain the recipient's UUID");
        }
    }
}

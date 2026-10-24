package com.auramc.rustbeds;

import com.mojang.authlib.GameProfile;
import net.minecraft.item.ItemStack;
import net.minecraft.nbt.NbtHelper;

final class HeadAssertions {
    static void verify(ItemStack head, GameProfile expected) {
        GameProfile actual = NbtHelper.toGameProfile(head.getOrCreateNbt().getCompound("SkullOwner"));
        if (actual == null || !expected.getId().equals(actual.getId())) {
            throw new AssertionError("SkullOwner must retain the recipient's UUID");
        }
    }
}

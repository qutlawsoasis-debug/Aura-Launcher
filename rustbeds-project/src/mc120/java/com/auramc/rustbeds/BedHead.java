package com.auramc.rustbeds;

import com.mojang.authlib.GameProfile;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import net.minecraft.nbt.NbtCompound;
import net.minecraft.nbt.NbtHelper;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;

final class BedHead {
    static ItemStack create(GameProfile profile) {
        ItemStack head = new ItemStack(Items.PLAYER_HEAD);
        head.getOrCreateNbt().put("SkullOwner", NbtHelper.writeGameProfile(new NbtCompound(), profile));
        head.setCustomName(Text.literal(profile.getName()).formatted(Formatting.YELLOW, Formatting.BOLD));
        return head;
    }
}

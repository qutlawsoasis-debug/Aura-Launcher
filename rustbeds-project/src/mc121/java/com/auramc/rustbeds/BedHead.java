package com.auramc.rustbeds;

import com.mojang.authlib.GameProfile;
import net.minecraft.component.DataComponentTypes;
import net.minecraft.component.type.ProfileComponent;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import net.minecraft.text.Text;
import net.minecraft.util.Formatting;

final class BedHead {
    static ItemStack create(GameProfile profile) {
        ItemStack head = new ItemStack(Items.PLAYER_HEAD);
        head.set(DataComponentTypes.PROFILE, new ProfileComponent(profile));
        head.set(DataComponentTypes.CUSTOM_NAME,
                Text.literal(profile.getName()).formatted(Formatting.YELLOW, Formatting.BOLD));
        return head;
    }
}

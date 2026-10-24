package com.auramc.rustbeds;

import com.mojang.authlib.GameProfile;
import net.fabricmc.fabric.api.event.player.UseBlockCallback;
import net.fabricmc.fabric.api.gametest.v1.FabricGameTest;
import net.minecraft.block.BedBlock;
import net.minecraft.block.Blocks;
import net.minecraft.block.enums.BedPart;
import net.minecraft.item.ItemStack;
import net.minecraft.item.Items;
import net.minecraft.screen.GenericContainerScreenHandler;
import net.minecraft.screen.slot.SlotActionType;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.test.GameTest;
import net.minecraft.test.TestContext;
import net.minecraft.util.ActionResult;
import net.minecraft.util.Hand;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Direction;
import net.minecraft.util.math.Vec3d;

import java.util.List;
import java.util.UUID;

public class BedAssignmentTest implements FabricGameTest {
    @GameTest(templateName = FabricGameTest.EMPTY_STRUCTURE)
    public void assignBedWithoutCrashing(TestContext context) {
        GameProfile profile = new GameProfile(UUID.randomUUID(), "BedRecipient");
        ItemStack head = BedHead.create(profile);
        require(head.isOf(Items.PLAYER_HEAD), "Recipient icon must be a player head");
        require(head.getName().getString().equals(profile.getName()), "Recipient name must survive item creation");
        HeadAssertions.verify(head, profile);

        ServerPlayerEntity sender = context.createMockCreativeServerPlayerInWorld();
        ServerPlayerEntity recipient = context.createMockCreativeServerPlayerInWorld();
        BlockPos bed = context.getAbsolutePos(new BlockPos(2, 2, 2));
        context.getWorld().setBlockState(bed.down(), Blocks.STONE.getDefaultState());
        context.getWorld().setBlockState(bed.south().down(), Blocks.STONE.getDefaultState());
        context.getWorld().setBlockState(bed.south(), Blocks.WHITE_BED.getDefaultState().with(BedBlock.PART, BedPart.FOOT), 2);
        context.getWorld().setBlockState(bed, Blocks.WHITE_BED.getDefaultState().with(BedBlock.PART, BedPart.HEAD), 2);
        sender.refreshPositionAndAngles(bed.getX() + 0.5, bed.getY(), bed.getZ() + 0.5, 0, 0);
        sender.setSneaking(true);
        sender.setStackInHand(Hand.MAIN_HAND, ItemStack.EMPTY);

        ActionResult opened = UseBlockCallback.EVENT.invoker().interact(sender, context.getWorld(), Hand.MAIN_HAND,
                new BlockHitResult(Vec3d.ofCenter(bed), Direction.UP, bed, false));
        require(opened == ActionResult.SUCCESS, "Sneak-use must open the assignment menu");
        require(sender.currentScreenHandler instanceof GenericContainerScreenHandler, "Assignment menu must be a vanilla container");
        require(sender.currentScreenHandler.getSlot(0).getStack().isOf(Items.PLAYER_HEAD), "Menu must contain a real recipient head");
        sender.currentScreenHandler.onSlotClick(0, 0, SlotActionType.PICKUP, sender);
        require(bed.equals(recipient.getSpawnPointPosition()), "Click must assign the selected recipient's spawn");

        recipient.setSpawnPoint(context.getWorld().getRegistryKey(), bed.up(), 0, true, false);
        new RustBedsMod().openBedGui(sender, bed, List.of(recipient));
        context.getWorld().setBlockState(bed, Blocks.AIR.getDefaultState());
        sender.currentScreenHandler.onSlotClick(0, 0, SlotActionType.PICKUP, sender);
        require(bed.up().equals(recipient.getSpawnPointPosition()), "A removed bed must not overwrite the recipient's spawn");
        context.complete();
    }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}

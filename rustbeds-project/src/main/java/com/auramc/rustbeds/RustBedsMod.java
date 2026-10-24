package com.auramc.rustbeds;

import com.mojang.brigadier.arguments.IntegerArgumentType;
import com.mojang.brigadier.arguments.StringArgumentType;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.command.v2.CommandRegistrationCallback;
import net.fabricmc.fabric.api.event.player.UseBlockCallback;
import net.minecraft.block.BedBlock;
import net.minecraft.block.BlockState;
import net.minecraft.entity.player.PlayerEntity;
import net.minecraft.entity.player.PlayerInventory;
import net.minecraft.inventory.SimpleInventory;
import net.minecraft.item.ItemStack;
import net.minecraft.screen.GenericContainerScreenHandler;
import net.minecraft.screen.NamedScreenHandlerFactory;
import net.minecraft.screen.ScreenHandler;
import net.minecraft.screen.ScreenHandlerType;
import net.minecraft.screen.slot.SlotActionType;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.command.CommandManager;
import net.minecraft.server.command.ServerCommandSource;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.text.Text;
import net.minecraft.util.ActionResult;
import net.minecraft.util.Formatting;
import net.minecraft.util.Hand;
import net.minecraft.util.hit.BlockHitResult;
import net.minecraft.util.math.BlockPos;
import net.minecraft.world.World;

import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

public class RustBedsMod implements ModInitializer {

    @Override
    public void onInitialize() {
        UseBlockCallback.EVENT.register(this::onUseBlock);

        CommandRegistrationCallback.EVENT.register((dispatcher, registryAccess, environment) -> {
            dispatcher.register(CommandManager.literal("givebed")
                .then(CommandManager.argument("player", StringArgumentType.word())
                    .then(CommandManager.argument("x", IntegerArgumentType.integer())
                        .then(CommandManager.argument("y", IntegerArgumentType.integer())
                            .then(CommandManager.argument("z", IntegerArgumentType.integer())
                                .executes(ctx -> {
                                    ServerCommandSource source = ctx.getSource();
                                    ServerPlayerEntity sender = source.getPlayer();
                                    if (sender == null) return 0;
                                    String targetName = StringArgumentType.getString(ctx, "player");
                                    int x = IntegerArgumentType.getInteger(ctx, "x");
                                    int y = IntegerArgumentType.getInteger(ctx, "y");
                                    int z = IntegerArgumentType.getInteger(ctx, "z");
                                    return executeGiveBed(sender, targetName, new BlockPos(x, y, z));
                                })
                            )
                        )
                    )
                )
            );
        });
    }

    private ActionResult onUseBlock(PlayerEntity player, World world, Hand hand, BlockHitResult hitResult) {
        if (world.isClient() || hand != Hand.MAIN_HAND) {
            return ActionResult.PASS;
        }
        if (!player.isSneaking() || !player.getStackInHand(hand).isEmpty()) {
            return ActionResult.PASS;
        }

        BlockPos pos = hitResult.getBlockPos();
        BlockState state = world.getBlockState(pos);
        if (!(state.getBlock() instanceof BedBlock)) {
            return ActionResult.PASS;
        }

        if (!(player instanceof ServerPlayerEntity serverPlayer)) {
            return ActionResult.PASS;
        }

        MinecraftServer server = serverPlayer.getServer();
        if (server == null) {
            return ActionResult.PASS;
        }

        List<ServerPlayerEntity> otherPlayers = new ArrayList<>();
        for (ServerPlayerEntity p : server.getPlayerManager().getPlayerList()) {
            if (p != serverPlayer) {
                otherPlayers.add(p);
            }
        }

        if (otherPlayers.isEmpty()) {
            serverPlayer.sendMessage(
                Text.literal("[Кровать] ").formatted(Formatting.GOLD)
                    .append(Text.literal("Сейчас нет других игроков в сети, чтобы передать кровать.").formatted(Formatting.RED)),
                false
            );
            return ActionResult.SUCCESS;
        }

        openBedGui(serverPlayer, pos, otherPlayers);
        return ActionResult.SUCCESS;
    }

    void openBedGui(ServerPlayerEntity player, BlockPos bedPos, List<ServerPlayerEntity> candidates) {
        SimpleInventory inv = new SimpleInventory(54);
        List<UUID> candidateIds = new ArrayList<>();
        int slot = 0;
        for (ServerPlayerEntity candidate : candidates) {
            if (slot >= 54) break;
            ItemStack head = BedHead.create(candidate.getGameProfile());
            inv.setStack(slot, head);
            candidateIds.add(candidate.getUuid());
            slot++;
        }

        player.openHandledScreen(new NamedScreenHandlerFactory() {
            @Override
            public Text getDisplayName() {
                return Text.literal("Выберите друга для передачи спавна");
            }

            @Override
            public ScreenHandler createMenu(int syncId, PlayerInventory playerInventory, PlayerEntity playerEntity) {
                return new GenericContainerScreenHandler(ScreenHandlerType.GENERIC_9X6, syncId, playerInventory, inv, 6) {
                    @Override
                    public void onSlotClick(int slotIndex, int button, SlotActionType actionType, PlayerEntity clickingPlayer) {
                        if (slotIndex >= 0 && slotIndex < candidateIds.size() && clickingPlayer instanceof ServerPlayerEntity sp) {
                            MinecraftServer server = sp.getServer();
                            ServerPlayerEntity chosen = server == null ? null : server.getPlayerManager().getPlayer(candidateIds.get(slotIndex));
                            sp.closeHandledScreen();
                            if (chosen != null) {
                                giveBed(sp, chosen, bedPos);
                            }
                            return;
                        }
                        if (slotIndex >= 0 && slotIndex < 54) {
                            return;
                        }
                        super.onSlotClick(slotIndex, button, actionType, clickingPlayer);
                    }

                    @Override
                    public ItemStack quickMove(PlayerEntity player, int index) {
                        return ItemStack.EMPTY;
                    }
                };
            }
        });
    }

    public static int executeGiveBed(ServerPlayerEntity sender, String targetName, BlockPos bedPos) {
        MinecraftServer server = sender.getServer();
        if (server == null) return 0;

        ServerPlayerEntity target = server.getPlayerManager().getPlayer(targetName);
        if (target == null) {
            sender.sendMessage(
                Text.literal("[Кровать] ").formatted(Formatting.GOLD)
                    .append(Text.literal("Игрок " + targetName + " сейчас не в сети!").formatted(Formatting.RED)),
                false
            );
            return 0;
        }
        return giveBed(sender, target, bedPos);
    }

    private static int giveBed(ServerPlayerEntity sender, ServerPlayerEntity target, BlockPos bedPos) {
        if (sender.squaredDistanceTo(bedPos.getX() + 0.5, bedPos.getY() + 0.5, bedPos.getZ() + 0.5) > 100.0) {
            sender.sendMessage(
                Text.literal("[Кровать] ").formatted(Formatting.GOLD)
                    .append(Text.literal("Вы слишком далеко от этой кровати!").formatted(Formatting.RED)),
                false
            );
            return 0;
        }

        BlockState state = sender.getServerWorld().getBlockState(bedPos);
        if (!(state.getBlock() instanceof BedBlock)) {
            sender.sendMessage(
                Text.literal("[Кровать] ").formatted(Formatting.GOLD)
                    .append(Text.literal("На этих координатах нет кровати!").formatted(Formatting.RED)),
                false
            );
            return 0;
        }

        target.setSpawnPoint(sender.getServerWorld().getRegistryKey(), bedPos, 0.0f, true, false);

        sender.sendMessage(
            Text.literal("[Кровать] ").formatted(Formatting.GOLD)
                .append(Text.literal("Вы успешно передали кровать игроку ").formatted(Formatting.GREEN))
                .append(Text.literal(target.getName().getString()).formatted(Formatting.YELLOW))
                .append(Text.literal("!").formatted(Formatting.GREEN)),
            false
        );

        target.sendMessage(
            Text.literal("[Кровать] ").formatted(Formatting.GOLD)
                .append(Text.literal(sender.getName().getString()).formatted(Formatting.YELLOW))
                .append(Text.literal(" передал вам кровать на [" + bedPos.getX() + ", " + bedPos.getY() + ", " + bedPos.getZ() + "]!").formatted(Formatting.GREEN)),
            false
        );

        return 1;
    }
}

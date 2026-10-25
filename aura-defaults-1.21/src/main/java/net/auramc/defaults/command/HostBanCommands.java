package net.auramc.defaults.command;

import com.mojang.authlib.GameProfile;
import com.mojang.brigadier.CommandDispatcher;
import com.mojang.brigadier.arguments.StringArgumentType;
import com.mojang.brigadier.context.CommandContext;
import com.mojang.brigadier.exceptions.CommandSyntaxException;
import net.minecraft.command.CommandSource;
import net.minecraft.command.argument.EntityArgumentType;
import net.minecraft.command.argument.GameProfileArgumentType;
import net.minecraft.command.argument.MessageArgumentType;
import net.minecraft.entity.Entity;
import net.minecraft.server.BannedPlayerEntry;
import net.minecraft.server.BannedPlayerList;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.command.CommandManager;
import net.minecraft.server.command.ServerCommandSource;
import net.minecraft.server.integrated.IntegratedServer;
import net.minecraft.server.network.ServerPlayerEntity;
import net.minecraft.text.Text;

import java.io.IOException;
import java.util.ArrayList;
import java.util.Collection;

public final class HostBanCommands {
    private HostBanCommands() {
    }

    public static boolean isHost(ServerCommandSource source) {
        Entity entity = source.getEntity();
        if (entity == null) {
            return true;
        }
        if (!(entity instanceof ServerPlayerEntity player)) {
            return false;
        }
        MinecraftServer server = source.getServer();
        if (server instanceof IntegratedServer integratedServer) {
            return integratedServer.isHost(player.getGameProfile());
        }
        return source.hasPermissionLevel(3);
    }

    public static void register(CommandDispatcher<ServerCommandSource> dispatcher) {
        // 1. /ban <targets> [reason]
        dispatcher.register(
                CommandManager.literal("ban")
                        .requires(HostBanCommands::isHost)
                        .then(CommandManager.argument("targets", GameProfileArgumentType.gameProfile())
                                .executes(ctx -> ban(ctx.getSource(), GameProfileArgumentType.getProfileArgument(ctx, "targets"), null))
                                .then(CommandManager.argument("reason", MessageArgumentType.message())
                                        .executes(ctx -> ban(ctx.getSource(), GameProfileArgumentType.getProfileArgument(ctx, "targets"), MessageArgumentType.getMessage(ctx, "reason")))))
        );

        // 2. /kick <targets> [reason]
        dispatcher.register(
                CommandManager.literal("kick")
                        .requires(HostBanCommands::isHost)
                        .then(CommandManager.argument("targets", EntityArgumentType.players())
                                .executes(ctx -> kick(ctx.getSource(), EntityArgumentType.getPlayers(ctx, "targets"), null))
                                .then(CommandManager.argument("reason", MessageArgumentType.message())
                                        .executes(ctx -> kick(ctx.getSource(), EntityArgumentType.getPlayers(ctx, "targets"), MessageArgumentType.getMessage(ctx, "reason")))))
        );

        // 3. /pardon <target> и /unban <target>
        var pardonCmd = CommandManager.literal("pardon")
                .requires(HostBanCommands::isHost)
                .then(CommandManager.argument("target", StringArgumentType.word())
                        .suggests((ctx, builder) -> {
                            BannedPlayerList banList = ctx.getSource().getServer().getPlayerManager().getUserBanList();
                            return CommandSource.suggestMatching(banList.getNames(), builder);
                        })
                        .executes(ctx -> unban(ctx.getSource(), StringArgumentType.getString(ctx, "target"))));

        var unbanCmd = CommandManager.literal("unban")
                .requires(HostBanCommands::isHost)
                .then(CommandManager.argument("target", StringArgumentType.word())
                        .suggests((ctx, builder) -> {
                            BannedPlayerList banList = ctx.getSource().getServer().getPlayerManager().getUserBanList();
                            return CommandSource.suggestMatching(banList.getNames(), builder);
                        })
                        .executes(ctx -> unban(ctx.getSource(), StringArgumentType.getString(ctx, "target"))));

        dispatcher.register(pardonCmd);
        dispatcher.register(unbanCmd);

        // 4. /banlist
        dispatcher.register(
                CommandManager.literal("banlist")
                        .requires(HostBanCommands::isHost)
                        .executes(ctx -> banlist(ctx.getSource()))
        );
    }

    private static int ban(ServerCommandSource source, Collection<GameProfile> targets, Text reason) {
        if (!isHost(source)) {
            source.sendError(Text.literal("Только хост сервера может использовать эту команду"));
            return 0;
        }

        MinecraftServer server = source.getServer();
        var playerManager = server.getPlayerManager();
        BannedPlayerList banList = playerManager.getUserBanList();
        int count = 0;
        String reasonStr = reason != null ? reason.getString() : "Забанен хостом";

        for (GameProfile profile : targets) {
            if (server instanceof IntegratedServer integratedServer && integratedServer.isHost(profile)) {
                source.sendError(Text.literal("Нельзя забанить хоста сервера"));
                continue;
            }

            if (!banList.contains(profile)) {
                BannedPlayerEntry entry = new BannedPlayerEntry(profile, null, source.getName(), null, reasonStr);
                banList.add(entry);
                try {
                    banList.save();
                } catch (IOException ignored) {
                }
                count++;

                source.sendFeedback(() -> Text.literal("\u00a7c[Aura] \u00a7fИгрок \u00a7e" + profile.getName() + "\u00a7f забанен: \u00a77" + reasonStr), true);

                ServerPlayerEntity online = playerManager.getPlayer(profile.getId());
                if (online == null) {
                    online = playerManager.getPlayer(profile.getName());
                }
                if (online != null) {
                    Text kickMsg = Text.literal("\u00a7cВы забанены на этом сервере!\n\u00a77Причина: \u00a7f" + reasonStr);
                    online.networkHandler.disconnect(kickMsg);
                }
            }
        }

        if (count == 0 && !targets.isEmpty()) {
            source.sendError(Text.literal("Выбранный игрок уже находится в бан-листе"));
        }
        return count;
    }

    private static int kick(ServerCommandSource source, Collection<ServerPlayerEntity> targets, Text reason) {
        if (!isHost(source)) {
            source.sendError(Text.literal("Только хост сервера может использовать эту команду"));
            return 0;
        }

        MinecraftServer server = source.getServer();
        int count = 0;
        String reasonStr = reason != null ? reason.getString() : "Исключён хостом";

        for (ServerPlayerEntity target : targets) {
            if (server instanceof IntegratedServer integratedServer && integratedServer.isHost(target.getGameProfile())) {
                source.sendError(Text.literal("Нельзя кикнуть хоста сервера"));
                continue;
            }

            Text kickMsg = Text.literal("\u00a7cВы были исключены из игры!\n\u00a77Причина: \u00a7f" + reasonStr);
            target.networkHandler.disconnect(kickMsg);
            count++;

            source.sendFeedback(() -> Text.literal("\u00a7c[Aura] \u00a7fИгрок \u00a7e" + target.getName().getString() + "\u00a7f кикнут: \u00a77" + reasonStr), true);
        }
        return count;
    }

    private static int unban(ServerCommandSource source, String targetName) {
        if (!isHost(source)) {
            source.sendError(Text.literal("Только хост сервера может использовать эту команду"));
            return 0;
        }

        BannedPlayerList banList = source.getServer().getPlayerManager().getUserBanList();
        boolean removed = false;

        for (BannedPlayerEntry entry : new ArrayList<>(banList.values())) {
            GameProfile profile = (GameProfile) ((net.auramc.defaults.mixin.ServerConfigEntryAccessor) entry).aura_getKey();
            if (profile != null && profile.getName() != null &&
                    profile.getName().equalsIgnoreCase(targetName)) {
                banList.remove(profile);
                try {
                    banList.save();
                } catch (IOException ignored) {
                }
                removed = true;
            }
        }

        if (removed) {
            source.sendFeedback(() -> Text.literal("\u00a7a[Aura] \u00a7fИгрок \u00a7e" + targetName + "\u00a7f разбанен"), true);
            return 1;
        } else {
            source.sendError(Text.literal("Игрок " + targetName + " не найден в бан-листе"));
            return 0;
        }
    }

    private static int banlist(ServerCommandSource source) {
        if (!isHost(source)) {
            source.sendError(Text.literal("Только хост сервера может использовать эту команду"));
            return 0;
        }

        BannedPlayerList banList = source.getServer().getPlayerManager().getUserBanList();
        Collection<BannedPlayerEntry> entries = banList.values();

        if (entries.isEmpty()) {
            source.sendFeedback(() -> Text.literal("\u00a7a[Aura] \u00a7fБан-лист пуст"), false);
            return 0;
        }

        source.sendFeedback(() -> Text.literal("\u00a7c[Aura] \u00a7fЗабаненных игроков: \u00a7e" + entries.size()), false);
        for (BannedPlayerEntry entry : entries) {
            GameProfile profile = (GameProfile) ((net.auramc.defaults.mixin.ServerConfigEntryAccessor) entry).aura_getKey();
            String name = (profile != null && profile.getName() != null) ? profile.getName() : "Неизвестно";
            String reason = entry.getReason() != null ? entry.getReason() : "без причины";
            String banSource = entry.getSource() != null ? entry.getSource() : "хост";
            source.sendFeedback(() -> Text.literal("\u00a77- \u00a7e" + name + " \u00a77(кем: " + banSource + ", причина: " + reason + ")"), false);
        }
        return entries.size();
    }
}

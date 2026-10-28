package net.auramc.defaults.client.quiet;

import net.minecraft.text.ClickEvent;
import net.minecraft.text.Text;
import net.minecraft.text.TranslatableTextContent;

public final class QuietChatRules {
    private QuietChatRules() {
    }

    private static final String IRIS_KEY_PREFIX = "iris.load.failure";

    private static final String[] STELLARITY_CLICK_URLS = {
        // Stellarity v2 (Aura-Pack 1.20.1)
        "https://ko-fi.com/kohara",
        "https://discord.gg/J6guYAySN8",
        // Stellarity 3.x (Aura-Pack 1.21.1)
        "https://prismaticshards.wiki.gg/wiki/Stellarity",
        "http://discord.prismaticshards.xyz",
        "https://github.com/Prismatic-Shards/translations",
        "https://bisecthosting.com/kohara",
        "https://github.com/Prismatic-Shards/Stellarity",
        "https://curseforge.com/minecraft/mc-mods/stellarity",
        "https://modrinth.com/datapack/stellarity",
    };

    private static final String[] STELLARITY_EXACT_LINES = {
        // Stellarity v2 (1.20.1)
        "Stellarity has successfully loaded!",
        "Made by kohara",
        "Originally made by Hex",
        // Stellarity 3.x (1.21.1)
        "| ⭐ Stellarity ⭐ has succesfully loaded!",
        "|      Cʀᴇᴀᴛᴇᴅ ʙʏ ᴋᴏʜᴀʀᴀ",
        "|      Mᴀɪɴᴛᴀɪɴᴇᴅ ʙʏ ᴘʀɪѕᴍᴀᴛɪᴄ ѕʜᴀʀᴅѕ",
        "| Usᴇғᴜʟ Lɪɴᴋs",
        "| Pʀᴏᴊᴇᴄᴛ Pᴀɢᴇ",
    };

    public static boolean shouldSuppress(Text message) {
        if (message == null) {
            return false;
        }

        if (message.getContent() instanceof TranslatableTextContent translatable
                && translatable.getKey().startsWith(IRIS_KEY_PREFIX)) {
            return true;
        }

        if (hasStellarityClickEvent(message)) {
            return true;
        }

        String plain = message.getString();
        if (plain == null || plain.isEmpty()) {
            return false;
        }
        for (String exact : STELLARITY_EXACT_LINES) {
            if (plain.equals(exact)) {
                return true;
            }
        }
        return false;
    }

    public static String describe(Text message) {
        if (message != null && message.getContent() instanceof TranslatableTextContent translatable) {
            return "[key=" + translatable.getKey() + "] " + message.getString();
        }
        return message == null ? "" : message.getString();
    }

    private static boolean hasStellarityClickEvent(Text text) {
        ClickEvent click = text.getStyle().getClickEvent();
        if (click != null && click.getValue() != null) {
            String value = click.getValue();
            if (click.getAction() == ClickEvent.Action.RUN_COMMAND
                    && value.startsWith("/function stellarity:")) {
                return true;
            }
            if (click.getAction() == ClickEvent.Action.OPEN_URL) {
                for (String url : STELLARITY_CLICK_URLS) {
                    if (value.equals(url)) {
                        return true;
                    }
                }
            }
        }
        for (Text sibling : text.getSiblings()) {
            if (hasStellarityClickEvent(sibling)) {
                return true;
            }
        }
        return false;
    }
}

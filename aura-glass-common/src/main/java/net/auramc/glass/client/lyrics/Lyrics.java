package net.auramc.glass.client.lyrics;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Full lyrics model matching lobby-api /api/lyrics format.
 *
 * @param source Source identifier (e.g. "lrclib")
 * @param synced Whether the lyrics have timed lines/words
 * @param lines  List of lyric lines
 */
public record Lyrics(String source, boolean synced, List<Line> lines) {
    public Lyrics {
        if (source == null) {
            source = "";
        }
        lines = lines != null ? List.copyOf(lines) : Collections.emptyList();
    }

    public static Lyrics parseJson(String jsonStr) {
        if (jsonStr == null || jsonStr.isBlank()) {
            return new Lyrics("", false, Collections.emptyList());
        }
        JsonElement elem = JsonParser.parseString(jsonStr);
        if (!elem.isJsonObject()) {
            return new Lyrics("", false, Collections.emptyList());
        }
        return fromJson(elem.getAsJsonObject());
    }

    public static Lyrics fromJson(JsonObject obj) {
        if (obj == null) {
            return new Lyrics("", false, Collections.emptyList());
        }
        String source = obj.has("source") && !obj.get("source").isJsonNull() ? obj.get("source").getAsString() : "";
        boolean synced = obj.has("synced") && !obj.get("synced").isJsonNull() && obj.get("synced").getAsBoolean();
        List<Line> lines = new ArrayList<>();
        if (obj.has("lines") && obj.get("lines").isJsonArray()) {
            JsonArray arr = obj.getAsJsonArray("lines");
            for (JsonElement el : arr) {
                if (el != null && el.isJsonObject()) {
                    lines.add(Line.fromJson(el.getAsJsonObject()));
                }
            }
        }
        return new Lyrics(source, synced, lines);
    }

    public JsonObject toJson() {
        JsonObject obj = new JsonObject();
        obj.addProperty("source", source);
        obj.addProperty("synced", synced);
        JsonArray arr = new JsonArray();
        for (Line l : lines) {
            arr.add(l.toJson());
        }
        obj.add("lines", arr);
        return obj;
    }
}

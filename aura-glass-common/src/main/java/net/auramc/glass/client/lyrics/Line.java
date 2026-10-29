package net.auramc.glass.client.lyrics;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

/**
 * Line of lyrics matching lobby-api /api/lyrics format.
 *
 * @param t     Start time in milliseconds
 * @param text  Full text of the line
 * @param words Word-by-word timing list
 */
public record Line(long t, String text, List<Word> words) {
    public Line {
        if (text == null) {
            text = "";
        }
        words = words != null ? List.copyOf(words) : Collections.emptyList();
    }

    public long duration() {
        if (words.isEmpty()) {
            return 0L;
        }
        Word last = words.get(words.size() - 1);
        return Math.max(0L, last.end() - t);
    }

    public static Line fromJson(JsonObject obj) {
        if (obj == null) {
            return new Line(0L, "", Collections.emptyList());
        }
        long t = obj.has("t") && !obj.get("t").isJsonNull() ? obj.get("t").getAsLong() : 0L;
        String text = obj.has("text") && !obj.get("text").isJsonNull() ? obj.get("text").getAsString() : "";
        List<Word> words = new ArrayList<>();
        if (obj.has("words") && obj.get("words").isJsonArray()) {
            JsonArray arr = obj.getAsJsonArray("words");
            for (JsonElement el : arr) {
                if (el != null && el.isJsonObject()) {
                    words.add(Word.fromJson(el.getAsJsonObject()));
                }
            }
        }
        return new Line(t, text, words);
    }

    public JsonObject toJson() {
        JsonObject obj = new JsonObject();
        obj.addProperty("t", t);
        obj.addProperty("text", text);
        JsonArray arr = new JsonArray();
        for (Word w : words) {
            arr.add(w.toJson());
        }
        obj.add("words", arr);
        return obj;
    }
}

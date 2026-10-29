package net.auramc.glass.client.lyrics;

import com.google.gson.JsonObject;

/**
 * Word-level timing representation matching lobby-api /api/lyrics format.
 *
 * @param t Start time in milliseconds
 * @param d Duration in milliseconds
 * @param w Word text
 */
public record Word(long t, long d, String w) {
    public Word {
        if (w == null) {
            w = "";
        }
    }

    public long end() {
        return t + d;
    }

    public boolean isActive(long positionMs) {
        return positionMs >= t && positionMs < (t + d);
    }

    public static Word fromJson(JsonObject obj) {
        if (obj == null) {
            return new Word(0L, 0L, "");
        }
        long t = obj.has("t") && !obj.get("t").isJsonNull() ? obj.get("t").getAsLong() : 0L;
        long d = obj.has("d") && !obj.get("d").isJsonNull() ? obj.get("d").getAsLong() : 0L;
        String w = obj.has("w") && !obj.get("w").isJsonNull() ? obj.get("w").getAsString() : "";
        return new Word(t, d, w);
    }

    public JsonObject toJson() {
        JsonObject obj = new JsonObject();
        obj.addProperty("t", t);
        obj.addProperty("d", d);
        obj.addProperty("w", w);
        return obj;
    }
}

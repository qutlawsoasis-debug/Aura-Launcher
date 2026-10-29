package net.auramc.glass.client.music;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;

public final class MusicJsonParser {
    private MusicJsonParser() {}

    public static NowPlaying parseNowPlaying(String json) {
        if (json == null || json.isBlank()) {
            return NowPlaying.stopped();
        }
        try {
            JsonElement rootElem = JsonParser.parseString(json);
            if (!rootElem.isJsonObject()) {
                return NowPlaying.stopped();
            }
            JsonObject obj = rootElem.getAsJsonObject();

            PlaybackState state = PlaybackState.STOPPED;
            if (obj.has("state")) {
                state = PlaybackState.fromString(obj.get("state").getAsString());
            }

            Track track = null;
            if (obj.has("track") && obj.get("track").isJsonObject()) {
                track = parseTrack(obj.getAsJsonObject("track"));
            }

            long positionMs = 0L;
            if (obj.has("positionMs")) {
                positionMs = obj.get("positionMs").getAsLong();
            } else if (obj.has("position")) {
                positionMs = obj.get("position").getAsLong();
            }

            long updatedAtUnixMs = 0L;
            if (obj.has("updatedAtUnixMs")) {
                updatedAtUnixMs = obj.get("updatedAtUnixMs").getAsLong();
            } else if (obj.has("updatedAt")) {
                updatedAtUnixMs = obj.get("updatedAt").getAsLong();
            }

            float volume = 1.0f;
            if (obj.has("volume")) {
                volume = obj.get("volume").getAsFloat();
            }

            return new NowPlaying(state, track, positionMs, updatedAtUnixMs, volume);
        } catch (Exception ignored) {
            return NowPlaying.stopped();
        }
    }

    public static Track parseTrack(JsonObject obj) {
        if (obj == null) return null;
        String id = getString(obj, "id");
        String artist = getString(obj, "artist");
        String title = getString(obj, "title");
        long durationMs = 0L;
        if (obj.has("durationMs")) {
            durationMs = obj.get("durationMs").getAsLong();
        } else if (obj.has("duration")) {
            durationMs = obj.get("duration").getAsLong();
        }
        String coverUrl = getString(obj, "coverUrl");
        return new Track(id, artist, title, durationMs, coverUrl);
    }

    public static List<Track> parseTrackList(String json) {
        if (json == null || json.isBlank()) {
            return Collections.emptyList();
        }
        try {
            JsonElement root = JsonParser.parseString(json);
            JsonArray array = null;
            if (root.isJsonArray()) {
                array = root.getAsJsonArray();
            } else if (root.isJsonObject()) {
                JsonObject obj = root.getAsJsonObject();
                if (obj.has("tracks") && obj.get("tracks").isJsonArray()) {
                    array = obj.getAsJsonArray("tracks");
                } else if (obj.has("results") && obj.get("results").isJsonArray()) {
                    array = obj.getAsJsonArray("results");
                }
            }
            if (array == null) {
                return Collections.emptyList();
            }

            List<Track> result = new ArrayList<>(array.size());
            for (JsonElement elem : array) {
                if (elem.isJsonObject()) {
                    Track t = parseTrack(elem.getAsJsonObject());
                    if (t != null) {
                        result.add(t);
                    }
                }
            }
            return Collections.unmodifiableList(result);
        } catch (Exception ignored) {
            return Collections.emptyList();
        }
    }

    private static String getString(JsonObject obj, String key) {
        if (obj.has(key) && !obj.get(key).isJsonNull()) {
            return obj.get(key).getAsString();
        }
        return "";
    }
}

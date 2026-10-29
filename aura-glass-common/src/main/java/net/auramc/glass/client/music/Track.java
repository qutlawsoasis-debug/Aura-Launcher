package net.auramc.glass.client.music;

import java.util.Objects;

public record Track(
    String id,
    String artist,
    String title,
    long durationMs,
    String coverUrl
) {
    public Track {
        id = Objects.requireNonNullElse(id, "");
        artist = Objects.requireNonNullElse(artist, "");
        title = Objects.requireNonNullElse(title, "");
        coverUrl = Objects.requireNonNullElse(coverUrl, "");
        durationMs = Math.max(0L, durationMs);
    }

    public static Track empty() {
        return new Track("", "", "", 0L, "");
    }

    public boolean isEmpty() {
        return id.isEmpty() && title.isEmpty() && artist.isEmpty();
    }
}

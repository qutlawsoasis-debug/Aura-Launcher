package net.auramc.glass.client.music;

public record Track(
        String id,
        String artist,
        String title,
        long durationMs,
        String coverUrl
) {
    public Track {
        if (id == null) id = "";
        if (artist == null) artist = "";
        if (title == null) title = "";
        if (coverUrl == null) coverUrl = "";
    }
}

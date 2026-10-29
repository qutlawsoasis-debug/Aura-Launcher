package net.auramc.glass.client.quickmenu.music;

public record MusicTrack(String id, String title, String author, String artwork, long durationMs,
                         String source, boolean local) {
    public boolean playable() { return local || source != null && source.startsWith("https://") && !source.contains("youtube.com/") && !source.contains("youtu.be/"); }
}
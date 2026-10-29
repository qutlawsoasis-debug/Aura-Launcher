package net.auramc.glass.client.music;

public enum PlaybackState {
    PLAYING,
    PAUSED,
    STOPPED;

    public static PlaybackState fromString(String state) {
        if (state == null) {
            return STOPPED;
        }
        return switch (state.trim().toLowerCase()) {
            case "playing", "play" -> PLAYING;
            case "paused", "pause" -> PAUSED;
            case "stopped", "stop" -> STOPPED;
            default -> STOPPED;
        };
    }
}

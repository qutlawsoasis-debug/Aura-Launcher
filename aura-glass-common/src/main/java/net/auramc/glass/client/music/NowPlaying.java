package net.auramc.glass.client.music;

import java.util.Objects;

public record NowPlaying(
    PlaybackState state,
    Track track,
    long positionMs,
    long updatedAtUnixMs,
    float volume
) {
    public static final NowPlaying STOPPED = new NowPlaying(PlaybackState.STOPPED, null, 0L, 0L, 1.0f);

    public NowPlaying {
        state = Objects.requireNonNullElse(state, PlaybackState.STOPPED);
        positionMs = Math.max(0L, positionMs);
        updatedAtUnixMs = Math.max(0L, updatedAtUnixMs);
        volume = Math.max(0.0f, Math.min(1.0f, volume));
    }

    public static NowPlaying stopped() {
        return STOPPED;
    }

    public boolean isPlaying() {
        return state == PlaybackState.PLAYING;
    }

    public boolean isPaused() {
        return state == PlaybackState.PAUSED;
    }

    public boolean isStopped() {
        return state == PlaybackState.STOPPED;
    }

    public boolean hasTrack() {
        return track != null && !track.isEmpty();
    }
}

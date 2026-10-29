package net.auramc.glass.client.music;

public record NowPlaying(
        String state,
        Track track,
        long positionMs,
        long updatedAtUnixMs,
        float volume
) {
    public boolean isPlaying() {
        return "playing".equalsIgnoreCase(state);
    }

    public boolean isPaused() {
        return "paused".equalsIgnoreCase(state);
    }

    public boolean isStopped() {
        return !isPlaying() && !isPaused();
    }

    public long currentPositionMs() {
        if (!isPlaying()) return positionMs;
        long elapsed = Math.max(0, System.currentTimeMillis() - updatedAtUnixMs);
        long current = positionMs + elapsed;
        if (track != null && track.durationMs() > 0) {
            current = Math.min(current, track.durationMs());
        }
        return current;
    }
}

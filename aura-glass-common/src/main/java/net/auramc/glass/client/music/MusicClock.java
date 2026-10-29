package net.auramc.glass.client.music;

import java.util.Objects;
import java.util.function.LongSupplier;

public final class MusicClock {
    private final MusicSource source;
    private final LongSupplier timeSupplier;

    private volatile NowPlaying manualSnapshot;
    private double renderedPositionMs = 0.0;
    private long lastFrameTimeMs = -1L;
    private String lastTrackId = "";
    private PlaybackState lastState = PlaybackState.STOPPED;

    private volatile boolean seekFlag = false;
    private volatile long seekTargetMs = 0L;
    private long epoch = 0L;

    public MusicClock() {
        this((MusicSource) null, System::currentTimeMillis);
    }

    public MusicClock(MusicSource source) {
        this(source, System::currentTimeMillis);
    }

    public MusicClock(MusicSource source, LongSupplier timeSupplier) {
        this.source = source;
        this.timeSupplier = Objects.requireNonNullElse(timeSupplier, System::currentTimeMillis);
    }

    public MusicClock(LongSupplier timeSupplier) {
        this((MusicSource) null, timeSupplier);
    }

    public MusicSource source() {
        return source;
    }

    public synchronized long epoch() {
        return epoch;
    }

    public synchronized void update(NowPlaying nowPlaying) {
        this.manualSnapshot = nowPlaying;
    }

    public synchronized void seek(long ms) {
        this.seekTargetMs = Math.max(0L, ms);
        this.seekFlag = true;
        this.epoch++;
        if (source != null) {
            source.seek(ms);
        }
    }

    public synchronized void reset() {
        this.renderedPositionMs = 0.0;
        this.lastFrameTimeMs = -1L;
        this.lastTrackId = "";
        this.lastState = PlaybackState.STOPPED;
        this.seekFlag = false;
        this.seekTargetMs = 0L;
        this.epoch++;
    }

    public long positionMs() {
        return positionMs(timeSupplier.getAsLong());
    }

    public synchronized long positionMs(long nowTimeMs) {
        NowPlaying np = (source != null) ? source.now() : manualSnapshot;
        if (np == null || np.isStopped() || !np.hasTrack()) {
            renderedPositionMs = 0.0;
            lastFrameTimeMs = nowTimeMs;
            lastTrackId = "";
            lastState = PlaybackState.STOPPED;
            return 0L;
        }

        Track track = np.track();
        String trackId = (track != null) ? track.id() : "";
        long durationMs = (track != null && track.durationMs() > 0) ? track.durationMs() : Long.MAX_VALUE;

        // 1. Hard resynchronization after seek
        if (seekFlag) {
            seekFlag = false;
            renderedPositionMs = clamp(seekTargetMs, durationMs);
            lastFrameTimeMs = nowTimeMs;
            return Math.round(renderedPositionMs);
        }

        // 2. Track change: increment epoch and snap to target
        if (!trackId.equals(lastTrackId)) {
            lastTrackId = trackId;
            lastState = np.state();
            epoch++;
            long elapsedSinceUpdate = Math.max(0L, nowTimeMs - np.updatedAtUnixMs());
            renderedPositionMs = clamp(np.positionMs() + (np.isPlaying() ? elapsedSinceUpdate : 0L), durationMs);
            lastFrameTimeMs = nowTimeMs;
            return Math.round(renderedPositionMs);
        }

        // 3. Paused state: position does not advance with time
        if (np.isPaused()) {
            lastState = PlaybackState.PAUSED;
            renderedPositionMs = clamp(np.positionMs(), durationMs);
            lastFrameTimeMs = nowTimeMs;
            return Math.round(renderedPositionMs);
        }

        // 4. Playing state
        long elapsedSinceUpdate = Math.max(0L, nowTimeMs - np.updatedAtUnixMs());
        double targetMs = clamp(np.positionMs() + elapsedSinceUpdate, durationMs);

        // Resume from stopped or paused
        if (lastState != PlaybackState.PLAYING) {
            lastState = PlaybackState.PLAYING;
            renderedPositionMs = targetMs;
            lastFrameTimeMs = nowTimeMs;
            return Math.round(renderedPositionMs);
        }

        long dt = (lastFrameTimeMs >= 0) ? Math.max(0L, nowTimeMs - lastFrameTimeMs) : 0L;
        lastFrameTimeMs = nowTimeMs;

        double discrepancy = targetMs - renderedPositionMs;

        if (Math.abs(discrepancy) > 250.0) {
            // Hard resynchronization on large deviation (> 250 ms)
            renderedPositionMs = targetMs;
            epoch++;
        } else if (discrepancy < 0.0) {
            // Negative discrepancy <= 250 ms: DO NOT rollback!
            // Slow down progression (dt * 0.5) until target converges with rendered position
            double advanced = renderedPositionMs + dt * 0.5;
            renderedPositionMs = clamp(advanced, durationMs);
        } else if (discrepancy <= 40.0) {
            // Small positive jump <= 40 ms: smooth catch-up
            double smoothFactor = Math.min(1.0, Math.max(0.05, dt / 200.0));
            double advanced = renderedPositionMs + dt + discrepancy * smoothFactor;
            renderedPositionMs = clamp(advanced, durationMs);
        } else {
            // Moderate positive drift (40 < discrepancy <= 250): smooth convergence over ~100 ms
            double moderateFactor = Math.min(1.0, Math.max(0.2, dt / 100.0));
            double advanced = renderedPositionMs + dt + discrepancy * moderateFactor;
            renderedPositionMs = clamp(advanced, durationMs);
        }

        return Math.round(renderedPositionMs);
    }

    private static double clamp(double value, long max) {
        if (value < 0.0) return 0.0;
        if (value > max) return max;
        return value;
    }
}

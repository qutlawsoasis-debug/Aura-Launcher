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

    public synchronized void update(NowPlaying nowPlaying) {
        this.manualSnapshot = nowPlaying;
    }

    public synchronized void seek(long ms) {
        this.seekTargetMs = Math.max(0L, ms);
        this.seekFlag = true;
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

        // 2. Paused state: position does not advance with time
        if (np.isPaused()) {
            lastState = PlaybackState.PAUSED;
            renderedPositionMs = clamp(np.positionMs(), durationMs);
            lastFrameTimeMs = nowTimeMs;
            return Math.round(renderedPositionMs);
        }

        // 3. Playing state: calculate extrapolated target from server
        long elapsedSinceUpdate = Math.max(0L, nowTimeMs - np.updatedAtUnixMs());
        double targetMs = clamp(np.positionMs() + elapsedSinceUpdate, durationMs);

        // Track change or resume from stopped/paused: snap to extrapolated target
        if (!trackId.equals(lastTrackId) || lastState != PlaybackState.PLAYING) {
            lastTrackId = trackId;
            lastState = PlaybackState.PLAYING;
            renderedPositionMs = targetMs;
            lastFrameTimeMs = nowTimeMs;
            return Math.round(renderedPositionMs);
        }

        long dt = (lastFrameTimeMs >= 0) ? Math.max(0L, nowTimeMs - lastFrameTimeMs) : 0L;
        lastFrameTimeMs = nowTimeMs;

        // Predicted position continuing along the timeline
        double predicted = clamp(renderedPositionMs + dt, durationMs);
        double discrepancy = targetMs - predicted;

        if (Math.abs(discrepancy) > 250.0) {
            // Hard resynchronization when discrepancy exceeds 250 ms
            renderedPositionMs = targetMs;
        } else if (Math.abs(discrepancy) <= 40.0) {
            // Smoothing small jumps <= 40 ms
            double smoothFactor = Math.min(1.0, Math.max(0.05, dt / 200.0));
            renderedPositionMs = clamp(predicted + discrepancy * smoothFactor, durationMs);
        } else {
            // Moderate drift (40 < discrepancy <= 250): smooth convergence over ~100 ms
            double moderateFactor = Math.min(1.0, Math.max(0.2, dt / 100.0));
            renderedPositionMs = clamp(predicted + discrepancy * moderateFactor, durationMs);
        }

        return Math.round(renderedPositionMs);
    }

    private static double clamp(double value, long max) {
        if (value < 0.0) return 0.0;
        if (value > max) return max;
        return value;
    }
}

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

    private volatile boolean seekPending = false;
    private volatile boolean seekFirstFrame = false;
    private volatile long seekTargetMs = 0L;
    private volatile long seekBaseUpdatedAt = -1L;
    private volatile long seekStartTimeMs = -1L;

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
        NowPlaying current = (source != null) ? source.now() : manualSnapshot;
        Track track = (current != null) ? current.track() : null;
        long duration = (track != null && track.durationMs() > 0) ? track.durationMs() : Long.MAX_VALUE;

        this.seekTargetMs = Math.round(clamp(ms, duration));
        this.renderedPositionMs = seekTargetMs;
        this.seekBaseUpdatedAt = (current != null) ? current.updatedAtUnixMs() : -1L;
        this.seekStartTimeMs = -1L;
        this.seekPending = true;
        this.seekFirstFrame = true;
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
        this.seekPending = false;
        this.seekFirstFrame = false;
        this.seekTargetMs = 0L;
        this.seekBaseUpdatedAt = -1L;
        this.seekStartTimeMs = -1L;
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
            seekPending = false;
            return 0L;
        }

        Track track = np.track();
        String trackId = (track != null) ? track.id() : "";
        long durationMs = (track != null && track.durationMs() > 0) ? track.durationMs() : Long.MAX_VALUE;

        // 1. Track change: clears seek, increments epoch, snaps to target
        if (!trackId.equals(lastTrackId)) {
            lastTrackId = trackId;
            lastState = np.state();
            seekPending = false;
            epoch++;
            long elapsedSinceUpdate = Math.max(0L, nowTimeMs - np.updatedAtUnixMs());
            renderedPositionMs = clamp(np.positionMs() + (np.isPlaying() ? elapsedSinceUpdate : 0L), durationMs);
            lastFrameTimeMs = nowTimeMs;
            return Math.round(renderedPositionMs);
        }

        // 2. Pending seek handling (survives stale snapshots until fresh confirmed snapshot)
        if (seekPending) {
            if (seekFirstFrame) {
                seekFirstFrame = false;
                seekStartTimeMs = nowTimeMs;
                lastFrameTimeMs = nowTimeMs;
                return Math.round(renderedPositionMs);
            }

            boolean freshSnapshot = np.updatedAtUnixMs() > seekBaseUpdatedAt;
            boolean timedOut = (seekStartTimeMs >= 0 && Math.abs(nowTimeMs - seekStartTimeMs) > 3000L);

            if (freshSnapshot || timedOut) {
                // Seek confirmed by fresh server snapshot (or safety timeout reached)
                seekPending = false;
            } else {
                // Stale snapshot: keep local seek timeline progressing smoothly
                long dt = (lastFrameTimeMs >= 0) ? Math.max(0L, nowTimeMs - lastFrameTimeMs) : 0L;
                lastFrameTimeMs = nowTimeMs;

                if (np.isPlaying()) {
                    renderedPositionMs = clamp(renderedPositionMs + dt, durationMs);
                }
                return Math.round(renderedPositionMs);
            }
        }

        // 3. Paused state: position does not advance with time
        if (np.isPaused()) {
            lastState = PlaybackState.PAUSED;
            renderedPositionMs = clamp(np.positionMs(), durationMs);
            lastFrameTimeMs = nowTimeMs;
            return Math.round(renderedPositionMs);
        }

        // 4. Playing state: calculate extrapolated target from server
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

        double predicted = clamp(renderedPositionMs + dt, durationMs);
        double discrepancy = targetMs - predicted;

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
            renderedPositionMs = clamp(predicted + discrepancy * smoothFactor, durationMs);
        } else {
            // Moderate positive drift (40 < discrepancy <= 250): smooth convergence over ~100 ms
            double moderateFactor = Math.min(1.0, Math.max(0.2, dt / 100.0));
            renderedPositionMs = clamp(predicted + discrepancy * moderateFactor, durationMs);
        }

        return Math.round(renderedPositionMs);
    }

    private static double clamp(double value, double max) {
        if (value < 0.0) return 0.0;
        if (value > max) return max;
        return value;
    }
}

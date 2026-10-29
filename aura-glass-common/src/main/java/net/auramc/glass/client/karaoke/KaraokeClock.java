package net.auramc.glass.client.karaoke;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.function.LongSupplier;

/**
 * Audio playback clock tracker with audio latency compensation and onset error logging.
 * Connects to MusicClock via LongSupplier positionMs.
 */
public final class KaraokeClock {

    /**
     * Pre-roll time in ms for word appearance animation.
     * A word starts manifesting PREROLL_MS before its onset time t,
     * so that the spring animation arrives at scale 1.0 exactly when the word begins singing.
     */
    public static final long PREROLL_MS = 240L;

    private LongSupplier clockSupplier = null;
    private long demoStartTime = -1L;
    private boolean demoPaused = false;
    private long demoPauseOffset = 0L;

    // Onset error tracking for QA (p95 <= 50 ms)
    private final List<Long> onsetErrors = Collections.synchronizedList(new ArrayList<>());
    private final List<String> onsetLogs = Collections.synchronizedList(new ArrayList<>());

    public KaraokeClock() {}

    public void setClockSupplier(LongSupplier supplier) {
        this.clockSupplier = supplier;
    }

    public boolean hasClock() {
        return clockSupplier != null || demoStartTime >= 0L;
    }

    public void startDemoTimeline() {
        this.demoStartTime = System.currentTimeMillis();
        this.demoPaused = false;
        this.demoPauseOffset = 0L;
        this.onsetErrors.clear();
        this.onsetLogs.clear();
    }

    public void reset() {
        this.demoStartTime = -1L;
        this.demoPaused = false;
        this.demoPauseOffset = 0L;
    }

    public long getRawPositionMs() {
        if (clockSupplier != null) {
            try {
                return clockSupplier.getAsLong();
            } catch (Throwable ignored) {
            }
        }
        if (demoStartTime >= 0L) {
            if (demoPaused) {
                return demoPauseOffset;
            }
            return System.currentTimeMillis() - demoStartTime;
        }
        return -1L;
    }

    /**
     * Effective playback position in ms including manual timing offset (+/- 500 ms).
     */
    public long getEffectivePositionMs() {
        long raw = getRawPositionMs();
        if (raw < 0L) return -1L;
        return raw + KaraokeConfig.get().getTimingOffsetMs();
    }

    /**
     * Records onset error when a word transitions into active singing.
     */
    public void recordOnset(String wordText, long targetOnsetMs, long actualOnsetMs) {
        long error = Math.abs(actualOnsetMs - targetOnsetMs);
        onsetErrors.add(error);
        if (onsetLogs.size() < 200) {
            onsetLogs.add(String.format("word='%s', target=%d ms, actual=%d ms, error=%d ms",
                    wordText, targetOnsetMs, actualOnsetMs, error));
        }
    }

    public OnsetStats getStats() {
        List<Long> errorsCopy;
        synchronized (onsetErrors) {
            errorsCopy = new ArrayList<>(onsetErrors);
        }
        if (errorsCopy.isEmpty()) {
            return new OnsetStats(0, 0, 0, 0, 0, 0, Collections.emptyList());
        }
        Collections.sort(errorsCopy);
        int n = errorsCopy.size();
        long p50 = Math.round(computePercentile(errorsCopy, 0.50));
        long p90 = Math.round(computePercentile(errorsCopy, 0.90));
        long p95 = Math.round(computePercentile(errorsCopy, 0.95));
        long max = errorsCopy.get(n - 1);
        double mean = errorsCopy.stream().mapToLong(Long::longValue).average().orElse(0.0);

        List<String> logsCopy;
        synchronized (onsetLogs) {
            logsCopy = new ArrayList<>(onsetLogs);
        }
        return new OnsetStats(n, mean, p50, p90, p95, max, logsCopy);
    }

    private static double computePercentile(List<Long> sorted, double percentile) {
        if (sorted.isEmpty()) return 0.0;
        if (sorted.size() == 1) return sorted.get(0);
        double index = (sorted.size() - 1) * percentile;
        int lower = (int) Math.floor(index);
        int upper = (int) Math.ceil(index);
        double weight = index - lower;
        return sorted.get(lower) * (1.0 - weight) + sorted.get(upper) * weight;
    }

    public static final class OnsetStats {
        public final int sampleCount;
        public final double meanErrorMs;
        public final long p50Ms;
        public final long p90Ms;
        public final long p95Ms;
        public final long maxErrorMs;
        public final List<String> recentLogs;

        public OnsetStats(int sampleCount, double meanErrorMs, long p50Ms, long p90Ms, long p95Ms, long maxErrorMs, List<String> recentLogs) {
            this.sampleCount = sampleCount;
            this.meanErrorMs = meanErrorMs;
            this.p50Ms = p50Ms;
            this.p90Ms = p90Ms;
            this.p95Ms = p95Ms;
            this.maxErrorMs = maxErrorMs;
            this.recentLogs = recentLogs;
        }

        public boolean meetsP95Requirement() {
            return sampleCount == 0 || p95Ms <= 50L;
        }

        @Override
        public String toString() {
            return String.format("OnsetStats(samples=%d, mean=%.1f ms, p50=%d ms, p90=%d ms, p95=%d ms, max=%d ms, p95<=50ms: %b)",
                    sampleCount, meanErrorMs, p50Ms, p90Ms, p95Ms, maxErrorMs, meetsP95Requirement());
        }
    }
}

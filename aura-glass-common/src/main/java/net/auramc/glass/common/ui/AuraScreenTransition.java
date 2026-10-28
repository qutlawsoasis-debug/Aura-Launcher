package net.auramc.glass.common.ui;

import java.util.Objects;
import java.util.function.LongSupplier;

public final class AuraScreenTransition {
    private static final long DURATION_NANOS = 200_000_000L;
    private final LongSupplier clock;
    private float start;
    private float target = 1.0f;
    private long started;
    private long duration = DURATION_NANOS;
    private boolean motionEnabled = true;

    public AuraScreenTransition() {
        this(System::nanoTime);
    }

    public AuraScreenTransition(LongSupplier clock) {
        this.clock = Objects.requireNonNull(clock);
        started = clock.getAsLong();
    }

    public void open() {
        moveTo(1.0f);
    }

    public void close() {
        moveTo(0.0f);
    }

    private void moveTo(float next) {
        if (target == next) return;
        long now = clock.getAsLong();
        start = progressAt(now);
        target = next;
        started = now;
        duration = Math.max(1L, (long) (DURATION_NANOS * Math.abs(target - start)));
        if (!motionEnabled) start = target;
    }

    public void setMotionEnabled(boolean enabled) {
        if (motionEnabled == enabled) return;
        long now = clock.getAsLong();
        start = enabled ? progressAt(now) : target;
        started = now;
        motionEnabled = enabled;
    }

    public boolean isClosing() {
        return target == 0.0f;
    }

    public boolean isInteractive() {
        return !isClosing() && getProgress() >= 1.0f;
    }

    public boolean isCloseFinished() {
        return isClosing() && getProgress() <= 0.0f;
    }

    public float getProgress() {
        return progressAt(clock.getAsLong());
    }

    private float progressAt(long now) {
        if (!motionEnabled) return target;
        float t = Math.min(1.0f, Math.max(0.0f, (now - started) / (float) duration));
        float remaining = 1.0f - t;
        return start + (target - start) * (1.0f - remaining * remaining * remaining);
    }
}
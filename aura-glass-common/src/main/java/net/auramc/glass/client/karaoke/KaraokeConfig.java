package net.auramc.glass.client.karaoke;

/**
 * User configuration options for in-world karaoke.
 * Accessible from the Music / F8 section.
 */
public final class KaraokeConfig {

    public enum Density {
        LESS(0.75f, 0.7f),
        NORMAL(1.0f, 1.0f),
        MORE(1.25f, 1.3f);

        public final float wordGroupMultiplier;
        public final float spawnRateMultiplier;

        Density(float wordGroupMultiplier, float spawnRateMultiplier) {
            this.wordGroupMultiplier = wordGroupMultiplier;
            this.spawnRateMultiplier = spawnRateMultiplier;
        }
    }

    private static final KaraokeConfig INSTANCE = new KaraokeConfig();

    public static KaraokeConfig get() {
        return INSTANCE;
    }

    private boolean enabled = true;
    private float size = 1.0f; // Scale multiplier (0.75 - 1.5)
    private float distance = 4.5f; // Distance in blocks (3.0 - 7.0)
    private Density density = Density.NORMAL;
    private boolean physicsEnabled = true;
    private long timingOffsetMs = 0L; // Manual text sync offset: -500 to +500 ms
    private boolean demoMode = false;

    private KaraokeConfig() {}

    public boolean isEnabled() {
        return enabled;
    }

    public void setEnabled(boolean enabled) {
        this.enabled = enabled;
    }

    public float getSize() {
        return size;
    }

    public void setSize(float size) {
        this.size = Math.max(0.5f, Math.min(2.0f, size));
    }

    public float getDistance() {
        return distance;
    }

    public void setDistance(float distance) {
        this.distance = Math.max(3.0f, Math.min(7.0f, distance));
    }

    public Density getDensity() {
        return density;
    }

    public void setDensity(Density density) {
        if (density != null) this.density = density;
    }

    public boolean isPhysicsEnabled() {
        return physicsEnabled;
    }

    public void setPhysicsEnabled(boolean physicsEnabled) {
        this.physicsEnabled = physicsEnabled;
    }

    public long getTimingOffsetMs() {
        return timingOffsetMs;
    }

    public void setTimingOffsetMs(long timingOffsetMs) {
        this.timingOffsetMs = Math.max(-500L, Math.min(500L, timingOffsetMs));
    }

    public boolean isDemoMode() {
        return demoMode;
    }

    public void setDemoMode(boolean demoMode) {
        this.demoMode = demoMode;
    }
}

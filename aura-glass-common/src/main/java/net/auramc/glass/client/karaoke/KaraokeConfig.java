package net.auramc.glass.client.karaoke;

import net.auramc.glass.client.quickmenu.QuickMenuConfig;

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

    private boolean demoMode = false;

    private KaraokeConfig() {}

    public boolean isEnabled() {
        return QuickMenuConfig.get().karaoke;
    }

    public void setEnabled(boolean enabled) {
        QuickMenuConfig.get().karaoke = enabled;
        if (!enabled) {
            KaraokeEngine.getInstance().resetWords();
        }
        QuickMenuConfig.save();
    }

    public float getSize() {
        return (float) QuickMenuConfig.get().karaokeSize;
    }

    public void setSize(float size) {
        QuickMenuConfig.get().karaokeSize = Math.max(0.75, Math.min(1.50, (double) size));
        QuickMenuConfig.save();
    }

    public float getDistance() {
        return (float) QuickMenuConfig.get().karaokeDistance;
    }

    public void setDistance(float distance) {
        QuickMenuConfig.get().karaokeDistance = Math.max(3.0, Math.min(7.0, (double) distance));
        QuickMenuConfig.save();
    }

    public Density getDensity() {
        int d = QuickMenuConfig.get().karaokeDensity;
        if (d <= 0) return Density.LESS;
        if (d >= 2) return Density.MORE;
        return Density.NORMAL;
    }

    public void setDensity(Density density) {
        if (density == Density.LESS) QuickMenuConfig.get().karaokeDensity = 0;
        else if (density == Density.MORE) QuickMenuConfig.get().karaokeDensity = 2;
        else QuickMenuConfig.get().karaokeDensity = 1;
        QuickMenuConfig.save();
    }

    public boolean isPhysicsEnabled() {
        return QuickMenuConfig.get().karaokePhysics;
    }

    public void setPhysicsEnabled(boolean physicsEnabled) {
        QuickMenuConfig.get().karaokePhysics = physicsEnabled;
        QuickMenuConfig.save();
    }

    public long getTimingOffsetMs() {
        return QuickMenuConfig.get().lyricsOffsetMs;
    }

    public void setTimingOffsetMs(long timingOffsetMs) {
        QuickMenuConfig.get().lyricsOffsetMs = (int) Math.max(-500L, Math.min(500L, timingOffsetMs));
        QuickMenuConfig.save();
    }

    public int getMaxWords() {
        int max = QuickMenuConfig.get().karaokeMaxWords;
        return max >= 1 && max <= 6 ? max : 4;
    }

    public void setMaxWords(int maxWords) {
        QuickMenuConfig.get().karaokeMaxWords = Math.max(1, Math.min(6, maxWords));
        QuickMenuConfig.save();
    }

    public boolean isOnlyStanding() {
        return QuickMenuConfig.get().karaokeOnlyStanding;
    }

    public void setOnlyStanding(boolean onlyStanding) {
        QuickMenuConfig.get().karaokeOnlyStanding = onlyStanding;
        QuickMenuConfig.save();
        if (onlyStanding && KaraokeEngine.getInstance().getAdaptiveness().getCurrentResult().state != PlayerAdaptiveness.ActivityState.STANDING) {
            KaraokeEngine.getInstance().resetWords();
        }
    }

    public boolean isDemoMode() {
        return demoMode;
    }

    public void setDemoMode(boolean demoMode) {
        this.demoMode = demoMode;
    }
}

package net.auramc.glass.client.karaoke;

/**
 * Handles spring animation and color states for karaoke words based on Apple HIG:
 * - Scale 0.6 -> 1.0 via Apple Spring (response=0.35s, damping=0.85)
 * - Letter-by-letter reveal with 20 ms stagger
 * - Current word accent #64D2FF, sung words #FFFFFF, upcoming words #D0C4E8F8
 * - Post-phrase hold (400 ms) before physical drop
 */
public final class WordAnimation {

    // Apple Spring parameters
    public static final float SPRING_RESPONSE = 0.35f; // seconds
    public static final float SPRING_DAMPING = 0.85f;  // damping ratio
    public static final float OMEGA = 18.0f;           // 2*pi / response
    public static final float ZETA = 0.85f;            // damping ratio
    public static final float DAMPED_OMEGA = 9.48f;    // omega * sqrt(1 - zeta^2)
    public static final long LETTER_STAGGER_MS = 20L;  // 15–25 ms per letter
    public static final long POST_PHRASE_HOLD_MS = 400L;

    // Palette per Apple HIG / W6b spec
    public static final int COLOR_ACTIVE_DARK = 0xFF64D2FF;  // Accent cyan on dark background
    public static final int COLOR_ACTIVE_LIGHT = 0xFF0A84FF; // Apple systemBlue on light background / snow
    public static final int COLOR_ACTIVE = COLOR_ACTIVE_DARK; // Default / fallback
    public static final int COLOR_UPCOMING = 0xF2FFFFFF;     // White 0.95 (rgba(255, 255, 255, 0.95))
    public static final int COLOR_SUNG = 0xCCFFFFFF;         // White 0.80 (rgba(255, 255, 255, 0.80))
    public static final int COLOR_OUTLINE = 0xFF000000;      // rgb(0, 0, 0) deep contrast outline / halo

    // MSDF halo alpha thresholds per W6b ТЗ
    public static final float HALO_ALPHA_DARK = 0.35f;
    public static final float HALO_ALPHA_LIGHT = 0.60f;

    public enum State {
        ENTERING,
        ACTIVE,
        SUNG,
        HOLDING,
        PHYSICAL,
        FINISHED
    }

    private WordAnimation() {}

    /**
     * Analytical Apple Spring progress function (0.0 to 1.0 with subtle physical bounce).
     *
     * @param elapsedSec time since spring trigger
     * @return spring scale in range [0.0, 1.04]
     */
    public static float evaluateSpring(float elapsedSec) {
        if (elapsedSec <= 0f) return 0f;
        if (elapsedSec >= 0.40f) return 1.0f; // Settle cleanly

        float decay = (float) Math.exp(-ZETA * OMEGA * elapsedSec);
        float phase = DAMPED_OMEGA * elapsedSec;
        float c = (float) Math.cos(phase);
        float s = (float) Math.sin(phase);

        float position = 1.0f - decay * (c + (ZETA * OMEGA / DAMPED_OMEGA) * s);
        return Math.max(0f, Math.min(1.05f, position));
    }

    /**
     * Computes the scale of a letter during reveal.
     */
    public static float letterScale(long wordElapsedMs, int charIndex) {
        long charElapsedMs = wordElapsedMs - charIndex * LETTER_STAGGER_MS;
        if (charElapsedMs <= 0) return 0.60f;
        float springProgress = evaluateSpring(charElapsedMs / 1000.0f);
        return 0.60f + 0.40f * springProgress;
    }

    /**
     * Computes the opacity (0.0 - 1.0) of a letter during reveal.
     */
    public static float letterAlpha(long wordElapsedMs, int charIndex) {
        long charElapsedMs = wordElapsedMs - charIndex * LETTER_STAGGER_MS;
        if (charElapsedMs <= 0) return 0.0f;
        float springProgress = evaluateSpring(charElapsedMs / 1000.0f);
        return Math.min(1.0f, springProgress);
    }

    /**
     * Interpolates active word color between #64D2FF (dark bg) and #0A84FF (light bg / snow).
     */
    public static int getActiveWordColor(float lightFactor) {
        float t = Math.max(0.0f, Math.min(1.0f, lightFactor));
        int r = Math.round((1.0f - t) * 0x64 + t * 0x0A);
        int g = Math.round((1.0f - t) * 0xD2 + t * 0x84);
        int b = 0xFF;
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }

    /**
     * Interpolates MSDF halo alpha between 0.35 (dark bg) and 0.60 (light bg / snow).
     */
    public static float getHaloAlpha(float lightFactor) {
        float t = Math.max(0.0f, Math.min(1.0f, lightFactor));
        return HALO_ALPHA_DARK + (HALO_ALPHA_LIGHT - HALO_ALPHA_DARK) * t;
    }

    /**
     * Applies alpha factor to a color (ARGB).
     */
    public static int applyAlpha(int color, float alpha) {
        int a = Math.max(0, Math.min(255, (int) (((color >>> 24) & 0xFF) * alpha)));
        return (a << 24) | (color & 0x00FFFFFF);
    }

    /**
     * Determines the display color for a word based on current playback time and background light.
     */
    public static int getWordColor(long currentPlaybackMs, long wordStartMs, long wordDurationMs, float alpha, float lightFactor) {
        int baseColor;
        long wordEnd = wordStartMs + wordDurationMs;

        if (currentPlaybackMs >= wordStartMs && currentPlaybackMs <= wordEnd) {
            baseColor = getActiveWordColor(lightFactor);
        } else if (currentPlaybackMs > wordEnd) {
            baseColor = COLOR_SUNG;
        } else {
            baseColor = COLOR_UPCOMING;
        }

        return applyAlpha(baseColor, alpha);
    }

    /**
     * Determines the display color for a word based on current playback time (default dark background).
     */
    public static int getWordColor(long currentPlaybackMs, long wordStartMs, long wordDurationMs, float alpha) {
        return getWordColor(currentPlaybackMs, wordStartMs, wordDurationMs, alpha, 0.0f);
    }
}

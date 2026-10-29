package net.auramc.glass.client.karaoke;

/**
 * Handles spring animation and color states for karaoke words based on Apple HIG & W6b:
 * - Scale 0.6 -> 1.0 via Apple Spring (response=0.35s, damping=0.85)
 * - Letter-by-letter reveal with 20 ms stagger
 * - Light background (> 0.60): words dark rgb(29,29,31) alpha 0.90; current #0A84FF; sung dark rgb(29,29,31) alpha 0.65; white halo alpha 0.45 (width 0.08 em, blur 0.03 em)
 * - Dark background (< 0.50): words white alpha 0.95; current #64D2FF; sung white alpha 0.75; black halo alpha 0.55 (width 0.10 em, blur 0.03 em)
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

    // Palette per Apple HIG / W6b VOZVRAT 1 spec:
    // Dark background (< 0.50):
    public static final int COLOR_ACTIVE_DARK = 0xFF64D2FF;   // Accent cyan
    public static final int COLOR_UPCOMING_DARK = 0xF2FFFFFF; // White alpha 0.95 (0.95 * 255 = 242 = 0xF2)
    public static final int COLOR_SUNG_DARK = 0xBFFFFFFF;     // White alpha 0.75 (0.75 * 255 = 191 = 0xBF)
    public static final int COLOR_HALO_DARK = 0x8C000000;     // Black alpha 0.55 (0.55 * 255 = 140 = 0x8C)
    public static final float HALO_WIDTH_DARK = 0.10f;        // 0.10 em
    public static final float HALO_BLUR_DARK = 0.03f;         // 0.03 em (crisp, not blurry)

    // Light background (> 0.60):
    public static final int COLOR_ACTIVE_LIGHT = 0xFF0A84FF;  // Apple systemBlue
    public static final int COLOR_UPCOMING_LIGHT = 0xE61D1D1F;// Dark rgb(29,29,31) alpha 0.90 (0.90 * 255 = 230 = 0xE6)
    public static final int COLOR_SUNG_LIGHT = 0xA61D1D1F;    // Dark rgb(29,29,31) alpha 0.65 (0.65 * 255 = 166 = 0xA6)
    public static final int COLOR_HALO_LIGHT = 0x73FFFFFF;    // White alpha 0.45 (0.45 * 255 = 115 = 0x73)
    public static final float HALO_WIDTH_LIGHT = 0.08f;       // 0.08 em
    public static final float HALO_BLUR_LIGHT = 0.03f;        // 0.03 em

    // Backward-compatible aliases
    public static final int COLOR_ACTIVE = COLOR_ACTIVE_DARK;
    public static final int COLOR_UPCOMING = COLOR_UPCOMING_DARK;
    public static final int COLOR_SUNG = COLOR_SUNG_DARK;
    public static final int COLOR_OUTLINE = 0xFF000000;
    public static final float HALO_ALPHA_DARK = 0.55f;
    public static final float HALO_ALPHA_LIGHT = 0.45f;

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
     * Interpolates upcoming word color between white 0.95 (dark bg) and dark rgb(29,29,31) 0.90 (light bg).
     */
    public static int getUpcomingWordColor(float lightFactor) {
        float t = Math.max(0.0f, Math.min(1.0f, lightFactor));
        int r = Math.round((1.0f - t) * 255f + t * 29f);
        int g = Math.round((1.0f - t) * 255f + t * 29f);
        int b = Math.round((1.0f - t) * 255f + t * 31f);
        int a = Math.round(((1.0f - t) * 0.95f + t * 0.90f) * 255f);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    /**
     * Interpolates sung word color between white 0.75 (dark bg) and dark rgb(29,29,31) 0.65 (light bg).
     */
    public static int getSungWordColor(float lightFactor) {
        float t = Math.max(0.0f, Math.min(1.0f, lightFactor));
        int r = Math.round((1.0f - t) * 255f + t * 29f);
        int g = Math.round((1.0f - t) * 255f + t * 29f);
        int b = Math.round((1.0f - t) * 255f + t * 31f);
        int a = Math.round(((1.0f - t) * 0.75f + t * 0.65f) * 255f);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    /**
     * Interpolates halo outline color between black 0.55 (dark bg) and white 0.45 (light bg).
     */
    public static int getHaloColor(float lightFactor) {
        float t = Math.max(0.0f, Math.min(1.0f, lightFactor));
        int rgb = Math.round(t * 255f);
        int a = Math.round(((1.0f - t) * 0.55f + t * 0.45f) * 255f);
        return (a << 24) | (rgb << 16) | (rgb << 8) | rgb;
    }

    /**
     * Interpolates MSDF halo alpha between 0.55 (dark bg) and 0.45 (light bg / snow).
     */
    public static float getHaloAlpha(float lightFactor) {
        float t = Math.max(0.0f, Math.min(1.0f, lightFactor));
        return 0.55f + (0.45f - 0.55f) * t;
    }

    /**
     * Interpolates MSDF halo width between 0.10 em (dark bg) and 0.08 em (light bg).
     */
    public static float getHaloWidth(float lightFactor) {
        float t = Math.max(0.0f, Math.min(1.0f, lightFactor));
        return HALO_WIDTH_DARK + (HALO_WIDTH_LIGHT - HALO_WIDTH_DARK) * t;
    }

    /**
     * MSDF halo blur: 0.03 em (crisp edge, not cloudy).
     */
    public static float getHaloBlur(float lightFactor) {
        return 0.03f;
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
            baseColor = getSungWordColor(lightFactor);
        } else {
            baseColor = getUpcomingWordColor(lightFactor);
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

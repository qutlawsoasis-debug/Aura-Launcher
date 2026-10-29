package net.auramc.glass.client.karaoke;

/**
 * Handles letter-by-letter Apple Spring animations and adaptive typography colors for W6d:
 * - Onest 700 (Bold) typography
 * - ZERO shadow, ZERO outline, ZERO halo: pure MSDF contrast
 * - Light background (> 0.60): letters rgb(29,29,31); active word #0A84FF; sung letters #0A84FF alpha 0.70
 * - Dark background (< 0.50): letters white; active word #64D2FF; sung letters #64D2FF alpha 0.70
 * - Letter-by-letter pop-in on word onset: step = min(35 ms, 0.3 * wordDuration / wordLength)
 * - Apple Spring: response = 0.32 s, damping = 0.62 (overshoot ~6%)
 * - Start: scale 0.0, yOffset +0.12 blocks, rotZ -12° -> settles to scale 1.0, yOffset 0.0, rotZ 0°
 * - Alpha: 0 -> 1 over 90 ms
 */
public final class WordAnimation {

    // Apple Spring parameters (W6d: response 0.32s, damping 0.62)
    public static final float SPRING_RESPONSE = 0.32f;
    public static final float SPRING_DAMPING = 0.62f;
    public static final float OMEGA = (float) (2.0 * Math.PI / SPRING_RESPONSE); // ~19.635
    public static final float ZETA = SPRING_DAMPING;                             // 0.62
    public static final float DAMPED_OMEGA = (float) (OMEGA * Math.sqrt(1.0 - ZETA * ZETA)); // ~15.405

    // Word post-singing hold before letters detach and fall (0.35 s)
    public static final long POST_WORD_HOLD_MS = 350L;
    public static final long POST_PHRASE_HOLD_MS = 350L;

    // Palette:
    // Dark background (< 0.50):
    public static final int COLOR_ACTIVE_DARK = 0xFF64D2FF;   // Accent cyan #64D2FF
    public static final int COLOR_BASE_DARK = 0xFFFFFFFF;     // White
    public static final int COLOR_SUNG_DARK = 0xB3FFFFFF;     // White alpha 0.70 (Return 1)

    // Light background (> 0.60):
    public static final int COLOR_ACTIVE_LIGHT = 0xFF0A84FF;  // Apple systemBlue #0A84FF
    public static final int COLOR_BASE_LIGHT = 0xFF1D1D1F;    // Dark rgb(29,29,31)
    public static final int COLOR_SUNG_LIGHT = 0xB31D1D1F;    // rgb(29,29,31) alpha 0.70 (Return 1)

    // Backward-compatible constants
    public static final int COLOR_ACTIVE = COLOR_ACTIVE_DARK;
    public static final int COLOR_UPCOMING_DARK = COLOR_BASE_DARK;
    public static final int COLOR_UPCOMING_LIGHT = COLOR_BASE_LIGHT;
    public static final int COLOR_SUNG = COLOR_SUNG_DARK;
    public static final int COLOR_UPCOMING = COLOR_BASE_DARK;
    public static final int COLOR_OUTLINE = 0x00000000;       // Zero halo

    private WordAnimation() {}

    /**
     * Analytical Apple Spring progress function (response 0.32s, damping 0.62, overshoot ~6%).
     */
    public static float evaluateSpring(float elapsedSec) {
        if (elapsedSec <= 0f) return 0f;
        if (elapsedSec >= 0.50f) return 1.0f; // Settled cleanly

        float decay = (float) Math.exp(-ZETA * OMEGA * elapsedSec);
        float phase = DAMPED_OMEGA * elapsedSec;
        float c = (float) Math.cos(phase);
        float s = (float) Math.sin(phase);

        float position = 1.0f - decay * (c + (ZETA * OMEGA / DAMPED_OMEGA) * s);
        return Math.max(0.0f, position);
    }

    /**
     * Calculates letter reveal step in ms: min(35 ms, 0.3 * duration / length).
     */
    public static long getLetterStepMs(long wordDurationMs, int wordLength) {
        if (wordLength <= 1) return 35L;
        long step = (long) (0.30 * wordDurationMs / wordLength);
        return Math.min(35L, Math.max(10L, step));
    }

    /**
     * Computes the scale of letter i during pop-in appearance.
     */
    public static float letterScale(long playbackMs, long wordOnsetMs, long wordDurationMs, int wordLength, int charIndex) {
        long stepMs = getLetterStepMs(wordDurationMs, wordLength);
        long charStartMs = wordOnsetMs + charIndex * stepMs;
        long charElapsedMs = playbackMs - charStartMs;
        if (charElapsedMs <= 0L) return 0.0f;
        return evaluateSpring(charElapsedMs / 1000.0f);
    }

    /**
     * Computes upward Y offset of letter i during pop-in appearance (+0.12 blocks -> 0.0 blocks).
     */
    public static float letterYOffset(long playbackMs, long wordOnsetMs, long wordDurationMs, int wordLength, int charIndex) {
        long stepMs = getLetterStepMs(wordDurationMs, wordLength);
        long charStartMs = wordOnsetMs + charIndex * stepMs;
        long charElapsedMs = playbackMs - charStartMs;
        if (charElapsedMs <= 0L) return 0.12f;
        float prog = evaluateSpring(charElapsedMs / 1000.0f);
        return (1.0f - Math.min(1.0f, prog)) * 0.12f;
    }

    /**
     * Computes Z rotation (tilt) of letter i during pop-in appearance (-12° -> 0°).
     */
    public static float letterRotZ(long playbackMs, long wordOnsetMs, long wordDurationMs, int wordLength, int charIndex) {
        long stepMs = getLetterStepMs(wordDurationMs, wordLength);
        long charStartMs = wordOnsetMs + charIndex * stepMs;
        long charElapsedMs = playbackMs - charStartMs;
        if (charElapsedMs <= 0L) return -12.0f;
        float prog = evaluateSpring(charElapsedMs / 1000.0f);
        return (1.0f - Math.min(1.0f, prog)) * (-12.0f);
    }

    /**
     * Computes opacity of letter i during pop-in (0 -> 1 over 90 ms).
     */
    public static float letterAlpha(long playbackMs, long wordOnsetMs, long wordDurationMs, int wordLength, int charIndex) {
        long stepMs = getLetterStepMs(wordDurationMs, wordLength);
        long charStartMs = wordOnsetMs + charIndex * stepMs;
        long charElapsedMs = playbackMs - charStartMs;
        if (charElapsedMs <= 0L) return 0.0f;
        return Math.min(1.0f, charElapsedMs / 90.0f);
    }

    // Simplified overloads for tests / linear word timeline:
    public static float letterScale(long wordElapsedMs, int charIndex) {
        long charElapsedMs = wordElapsedMs - charIndex * 35L;
        if (charElapsedMs <= 0) return 0.0f;
        return evaluateSpring(charElapsedMs / 1000.0f);
    }

    public static float letterAlpha(long wordElapsedMs, int charIndex) {
        long charElapsedMs = wordElapsedMs - charIndex * 35L;
        if (charElapsedMs <= 0) return 0.0f;
        return Math.min(1.0f, charElapsedMs / 90.0f);
    }

    /**
     * Interpolates active word color: #64D2FF (dark) <-> #0A84FF (light).
     */
    public static int getActiveWordColor(float lightProgress) {
        return interpolateColor(COLOR_ACTIVE_DARK, COLOR_ACTIVE_LIGHT, lightProgress);
    }

    /**
     * Interpolates sung word color (alpha 0.70).
     */
    public static int getSungWordColor(float lightProgress) {
        return interpolateColor(COLOR_SUNG_DARK, COLOR_SUNG_LIGHT, lightProgress);
    }

    /**
     * Interpolates base text color: white (dark) <-> rgb(29,29,31) (light).
     */
    public static int getBaseWordColor(float lightProgress) {
        return interpolateColor(COLOR_BASE_DARK, COLOR_BASE_LIGHT, lightProgress);
    }

    public static int getUpcomingWordColor(float lightProgress) {
        return getBaseWordColor(lightProgress);
    }

    // Zero halo/shadow in W6d
    public static int getHaloColor(float lightProgress) {
        return 0x00000000;
    }

    public static float getHaloWidth(float lightProgress) {
        return 0.0f;
    }

    public static float getHaloBlur(float lightProgress) {
        return 0.0f;
    }

    /**
     * Resolves letter color based on playback time and active background luminance.
     */
    public static int getLetterColor(long playbackMs, long wordOnsetMs, long wordDurationMs, float lightProgress) {
        if (playbackMs < wordOnsetMs) {
            // Un-sung (invisible prior to singing, but base color if rendered)
            return getBaseWordColor(lightProgress);
        } else if (playbackMs < (wordOnsetMs + wordDurationMs)) {
            // Actively singing: 100% active accent
            return getActiveWordColor(lightProgress);
        } else {
            // Sung (Return 1: sung words white alpha 0.70 on dark / rgb(29,29,31) alpha 0.70 on light)
            return getSungWordColor(lightProgress);
        }
    }

    public static int getWordColor(long playbackMs, long wordOnsetMs, long wordDurationMs, float scale, float lightProgress) {
        return getLetterColor(playbackMs, wordOnsetMs, wordDurationMs, lightProgress);
    }

    public static int getWordColor(long playbackMs, long wordOnsetMs, long wordDurationMs, float scale) {
        return getWordColor(playbackMs, wordOnsetMs, wordDurationMs, scale, 0.0f);
    }

    public static int applyAlpha(int argb, float alpha) {
        int a = Math.max(0, Math.min(255, (int) (((argb >>> 24) & 0xFF) * alpha)));
        return (a << 24) | (argb & 0x00FFFFFF);
    }

    public static int interpolateColor(int colA, int colB, float t) {
        float f = Math.max(0.0f, Math.min(1.0f, t));
        int aA = (colA >>> 24) & 0xFF;
        int rA = (colA >>> 16) & 0xFF;
        int gA = (colA >>> 8) & 0xFF;
        int bA = colA & 0xFF;

        int aB = (colB >>> 24) & 0xFF;
        int rB = (colB >>> 16) & 0xFF;
        int gB = (colB >>> 8) & 0xFF;
        int bB = colB & 0xFF;

        int a = (int) (aA + (aB - aA) * f);
        int r = (int) (rA + (rB - rA) * f);
        int g = (int) (gA + (gB - gA) * f);
        int b = (int) (bA + (bB - bA) * f);

        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}

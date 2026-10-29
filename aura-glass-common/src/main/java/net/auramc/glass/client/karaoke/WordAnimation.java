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

    // Palette per Apple HIG / Aura Glass spec
    public static final int COLOR_ACTIVE = 0xFF64D2FF;   // Accent cyan
    public static final int COLOR_SUNG = 0xFFFFFFFF;     // White
    public static final int COLOR_UPCOMING = 0xD0C4E8F8; // Translucent sky-white
    public static final int COLOR_OUTLINE = 0xFF0A1420;  // Deep dark contrast outline

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
     * Determines the display color for a word based on current playback time.
     */
    public static int getWordColor(long currentPlaybackMs, long wordStartMs, long wordDurationMs, float alpha) {
        int baseColor;
        long wordEnd = wordStartMs + wordDurationMs;

        if (currentPlaybackMs >= wordStartMs && currentPlaybackMs <= wordEnd) {
            baseColor = COLOR_ACTIVE;
        } else if (currentPlaybackMs > wordEnd) {
            baseColor = COLOR_SUNG;
        } else {
            baseColor = COLOR_UPCOMING;
        }

        int a = Math.max(0, Math.min(255, (int) (((baseColor >>> 24) & 0xFF) * alpha)));
        return (a << 24) | (baseColor & 0x00FFFFFF);
    }
}

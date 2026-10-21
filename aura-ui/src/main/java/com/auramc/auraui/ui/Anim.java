package com.auramc.auraui.ui;

public final class Anim {
    private Anim() {}

    public static float now() {
        return (float) (System.nanoTime() / 1_000_000_000.0);
    }

    public static float clamp(float val, float min, float max) {
        return Math.max(min, Math.min(max, val));
    }

    public static float lerp(float a, float b, float t) {
        return a + (b - a) * t;
    }

    public static int lerpColor(int c1, int c2, float t) {
        t = clamp(t, 0.0f, 1.0f);
        int a1 = (c1 >> 24) & 0xFF;
        int r1 = (c1 >> 16) & 0xFF;
        int g1 = (c1 >> 8) & 0xFF;
        int b1 = c1 & 0xFF;

        int a2 = (c2 >> 24) & 0xFF;
        int r2 = (c2 >> 16) & 0xFF;
        int g2 = (c2 >> 8) & 0xFF;
        int b2 = c2 & 0xFF;

        int a = (int) (a1 + (a2 - a1) * t);
        int r = (int) (r1 + (r2 - r1) * t);
        int g = (int) (g1 + (g2 - g1) * t);
        int b = (int) (b1 + (b2 - b1) * t);

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    public static float easeOutCubic(float t) {
        t = clamp(t, 0.0f, 1.0f);
        float f = 1.0f - t;
        return 1.0f - f * f * f;
    }

    public static float easeOutQuad(float t) {
        t = clamp(t, 0.0f, 1.0f);
        return 1.0f - (1.0f - t) * (1.0f - t);
    }

    public static float spring(float t) {
        t = clamp(t, 0.0f, 1.0f);
        // Approximation of cubic-bezier(0.34, 1.3, 0.5, 1)
        float c4 = (2.0f * (float) Math.PI) / 3.0f;
        if (t == 0.0f) return 0.0f;
        if (t == 1.0f) return 1.0f;
        return (float) (Math.pow(2.0, -10.0 * t) * Math.sin((t * 10.0f - 0.75f) * c4) + 1.0);
    }
}

package net.auramc.glass.common.ui;

/** Resolves each axis to its nearest candidate, independent of sibling iteration order. */
public final class AuraSnapSolver {
    private float xDistance, yDistance;
    private int x, y, xGuide, yGuide;
    private static final int RANGE = 5;

    public void begin(int x, int y) {
        this.x = x; this.y = y;
        xDistance = yDistance = RANGE + 1;
        xGuide = yGuide = -1;
    }

    public void x(int raw, int candidate, int guide) {
        int distance = Math.abs(raw - candidate);
        if (distance <= RANGE && (distance < xDistance || (distance == xDistance && candidate < x))) {
            xDistance = distance; x = candidate; xGuide = guide;
        }
    }

    public void y(int raw, int candidate, int guide) {
        int distance = Math.abs(raw - candidate);
        if (distance <= RANGE && (distance < yDistance || (distance == yDistance && candidate < y))) {
            yDistance = distance; y = candidate; yGuide = guide;
        }
    }

    public int x() { return x; }
    public int y() { return y; }
    public int xGuide() { return xGuide; }
    public int yGuide() { return yGuide; }

    public int softX(int raw) { return magnetic(raw, x); }
    public int softY(int raw) { return magnetic(raw, y); }

    private static int magnetic(int raw, int target) {
        float t = Math.max(0, 1f - Math.abs(raw - target) / (float) RANGE);
        float weight = t * t * (3 - 2 * t);
        return Math.round(raw + (target - raw) * weight);
    }
}

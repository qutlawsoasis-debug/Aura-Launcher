package net.auramc.glass.client.quickmenu;

import net.auramc.glass.common.render.GlassSpring;

public final class QuickMenuMotion {
    private static final long OPEN_TIME = 420_000_000L;
    private static final long CLOSE_TIME = 320_000_000L;
    private static final long AXIS_DELAY = 40_000_000L;
    private static final long CONTENT_FADE = 100_000_000L;
    private static final long SECTION_DELAY = 25_000_000L;
    private static final long CONTENT_END = CONTENT_FADE + 2 * SECTION_DELAY;
    // GlassSpring with Apple response 0.35s and damping 0.85
    private final GlassSpring heightSpring = new GlassSpring(0.35f, 0.85f);
    private final GlassSpring widthSpring = new GlassSpring(0.35f, 0.85f);
    private long lastTime = Long.MIN_VALUE, changedAt;
    private long heightClock = 1, widthClock = 1;
    private boolean open, initialized, positionSet, openingFromClosed;
    private long contentElapsed, closeFadeDuration;
    private double center = .5, bottom = Double.NaN;
    private double displayCenter, displayBottom, startCenter, startBottom;
    private float widthProgress, heightProgress;
    private Geometry geometry = new Geometry(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public record Geometry(float x, float y, float width, float height, float radius,
                           float contentOpacity, float progress, float anchorX, float anchorY, long contentElapsedNanos) {
        public float bottom() { return y + height; }
        public float centerX() { return x + width * .5f; }
        public float scale() { return 0.08f + 0.92f * progress; }
        public float opacity() { return progress; }
        public float sectionOpacity(int section) {
            return smooth(unit((contentElapsedNanos - Math.max(0, section) * SECTION_DELAY) / (float) CONTENT_FADE));
        }
        public float sectionOffset(int section) { return 8 * (1 - sectionOpacity(section)); }
        public boolean contains(double px, double py) {
            return width > 0 && height > 0 && px >= x && px <= x + width && py >= y && py <= bottom();
        }
    }

    public void setOpen(boolean value, long now) {
        if (open == value) return;
        openingFromClosed = value && settledClosed();
        if (!value) closeFadeDuration = (long) Math.ceil(contentElapsed / 2.5);
        open = value;
        changedAt = now;
        startCenter = displayCenter;
        startBottom = displayBottom;
    }

    public boolean isOpen() { return open; }
    public Geometry geometry() { return geometry; }
    public boolean settledClosed() { return !open && geometry.width() == 0 && geometry.height() == 0; }
    public double centerX() { return center; }
    public double bottomY() { return bottom; }

    public void setPosition(double centerXNorm, double bottomYNorm) {
        if (!Double.isFinite(centerXNorm) || !Double.isFinite(bottomYNorm)) return;
        center = unit(centerXNorm);
        bottom = unit(bottomYNorm);
        positionSet = true;
    }

    public void resetPosition() {
        center = .5;
        bottom = Double.NaN;
        positionSet = true;
    }

    public Geometry update(long now, int viewportWidth, int viewportHeight,
                           double savedCenterX, double savedBottomY) {
        int screenWidth = Math.max(1, viewportWidth), screenHeight = Math.max(1, viewportHeight);
        float anchorX = screenWidth * .5f, anchorY = Math.max(0, screenHeight - 12);
        float targetWidth = Math.min(Math.max(1, screenWidth - 32), Math.max(520, screenWidth * .75f));
        float targetHeight = Math.min(Math.max(1, screenHeight - 48), Math.max(320, screenHeight * .667f));
        if (!initialized) {
            initialized = true;
            if (!positionSet) {
                center = Double.isFinite(savedCenterX) ? unit(savedCenterX) : .5;
                bottom = Double.isFinite(savedBottomY) ? unit(savedBottomY) : Double.NaN;
            }
            displayCenter = startCenter = anchorX;
            displayBottom = startBottom = anchorY;
            heightSpring.approach(0, heightClock);
            widthSpring.approach(0, widthClock);
            if (open) changedAt = now;
        }
        if (openingFromClosed) {
            openingFromClosed = false;
            displayCenter = startCenter = anchorX;
            displayBottom = startBottom = anchorY;
        }
        long elapsed = Math.max(0, now - changedAt);
        long dt = lastTime == Long.MIN_VALUE ? 0 : Math.max(0, Math.min(50_000_000L, now - lastTime));
        lastTime = now;
        boolean heightMoving = open || elapsed >= closeFadeDuration + AXIS_DELAY;
        boolean widthMoving = open ? elapsed >= AXIS_DELAY : elapsed >= closeFadeDuration;
        if (heightMoving) heightClock += (long) (dt * (open ? 1.55 : 2.8));
        if (widthMoving) widthClock += (long) (dt * (open ? 1.2 : 2.3));
        heightProgress = unit(heightSpring.approach(open ? 1 : 0, heightClock));
        widthProgress = unit(widthSpring.approach(open ? 1 : 0, widthClock));
        float progress = Math.min(heightProgress, widthProgress);
        if (open && progress >= .6f) contentElapsed = Math.min(CONTENT_END, contentElapsed + dt);
        else if (!open) contentElapsed = Math.max(0, contentElapsed - (long) (dt * 2.5));
        double targetCenter = bound(center * screenWidth, targetWidth * .5 + 16, screenWidth - targetWidth * .5 - 16);
        double targetBottom = Double.isFinite(bottom) ? bottom * screenHeight : anchorY;
        targetBottom = bound(targetBottom, targetHeight + 16, Math.max(anchorY, screenHeight - 12));
        center = targetCenter / screenWidth;
        if (Double.isFinite(bottom)) bottom = targetBottom / screenHeight;
        if (open) {
            double phase = smooth(unit(elapsed / (double) OPEN_TIME));
            double toX = startCenter + (targetCenter - startCenter) * phase;
            double toY = startBottom + (targetBottom - startBottom) * phase;
            double follow = elapsed < OPEN_TIME ? 1 : 1 - Math.exp(-dt / 25_000_000.0);
            displayCenter += (toX - displayCenter) * follow;
            displayBottom += (toY - displayBottom) * follow;
        } else {
            double phase = smooth(unit((elapsed - closeFadeDuration) / (double) (CLOSE_TIME - closeFadeDuration)));
            displayCenter = startCenter + (anchorX - startCenter) * phase;
            double arc = Math.sin(Math.PI * phase) * Math.min(48, Math.abs(startCenter - anchorX) * .16);
            displayBottom = startBottom + (anchorY - startBottom) * phase - arc;
        }
        if (!open && (elapsed >= CLOSE_TIME && progress < .004f || elapsed >= 2_000_000_000L)) {
            displayCenter = startCenter = anchorX;
            displayBottom = startBottom = anchorY;
            geometry = new Geometry(anchorX, anchorY, 0, 0, 0, 0, 0, anchorX, anchorY, 0);
            return geometry;
        }
        float scaleX = 0.08f + 0.92f * widthProgress;
        float scaleY = 0.08f + 0.92f * heightProgress;
        float width = Math.min(targetWidth, targetWidth * scaleX);
        float height = Math.min(targetHeight, targetHeight * scaleY);
        double cx = bound(displayCenter, width * .5 + 1, screenWidth - width * .5 - 1);
        double by = bound(displayBottom, height + 1, screenHeight - 1);
        float morph = smooth(unit((progress - .15f) / .75f));
        float radius = Math.min(width, height) * .5f;
        radius += (Math.min(22, Math.min(width, height) * .5f) - radius) * morph;
        float opacity = smooth(unit(contentElapsed / (float) CONTENT_FADE));
        geometry = new Geometry((float) cx - width * .5f, (float) by - height, width, height,
                radius, opacity, progress, anchorX, anchorY, contentElapsed);
        return geometry;
    }

    private static float unit(float value) { return Math.max(0, Math.min(1, value)); }
    private static double unit(double value) { return Math.max(0, Math.min(1, value)); }
    private static float smooth(float value) { return value * value * (3 - 2 * value); }
    private static double smooth(double value) { return value * value * (3 - 2 * value); }
    private static double bound(double value, double min, double max) {
        return min > max ? (min + max) * .5 : Math.max(min, Math.min(max, value));
    }
}

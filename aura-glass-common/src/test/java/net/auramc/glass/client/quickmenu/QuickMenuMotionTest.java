package net.auramc.glass.client.quickmenu;

public final class QuickMenuMotionTest {
    private static final long STEP = 8_000_000L;
    private static final int W = 1280, H = 720;

    public static void main(String[] args) {
        anchoredOpeningAndArea();
        stagedAxesAndContent();
        timedSectionStaggerAndCloseOrder();
        interruptionKeepsGeometry();
        dragAndArcReturn();
        smallViewportClamps();
        System.out.println("Quick menu motion: anchored opening, area, axis delay, reversible springs, drag bounds and arc return PASS");
    }

    private static void anchoredOpeningAndArea() {
        QuickMenuMotion motion = new QuickMenuMotion();
        long start = 1_000_000_000L;
        motion.setOpen(true, start);
        for (long t = start; t <= start + 1_000_000_000L; t += STEP) {
            var frame = motion.update(t, W, H, .5, Double.NaN);
            near(frame.bottom(), H - 24, .001, "bottom stays on anchor while opening");
            check(frame.y() >= 0 && frame.x() >= 0, "opening stays inside viewport");
            check(frame.radius() <= Math.min(frame.width(), frame.height()) * .5f, "radius fits current shape");
        }
        var frame = motion.geometry();
        near(frame.width() * frame.height() / (W * (double) H), .50025, .003, "unclamped panel occupies half screen");
        near(frame.radius(), 22, .05, "expanded radius is22");
        check(frame.contentOpacity() > .99, "content fully appears");
    }

    private static void stagedAxesAndContent() {
        QuickMenuMotion motion = new QuickMenuMotion();
        long start = 1_000_000_000L;
        motion.setOpen(true, start);
        var first = motion.update(start, W, H, .5, Double.NaN);
        near(first.width(), 8, .001, "initial dot width");
        near(first.height(), 8, .001, "initial dot height");
        near(first.radius(), 4, .001, "initial circular dot");
        for (long t = start + STEP; t < start + 40_000_000L; t += STEP) {
            var frame = motion.update(t, W, H, .5, Double.NaN);
            near(frame.width(), 8, .001, "width waits40ms");
            check(frame.height() > 8, "height extends first");
            near(frame.contentOpacity(), 0, .001, "content absent below60percent");
        }
        long t = start + 40_000_000L;
        for (; t <= start + 800_000_000L; t += STEP) {
            var frame = motion.update(t, W, H, .5, Double.NaN);
            check(frame.sectionOpacity(0) >= frame.sectionOpacity(1), "sections appear in order");
            check(frame.sectionOffset(0) >= 0 && frame.sectionOffset(0) <= 8, "content offset8px");
        }
    }

    private static void timedSectionStaggerAndCloseOrder() {
        QuickMenuMotion motion = new QuickMenuMotion();
        long start = 1_000_000_000L, now = start;
        long[] sectionStarted = {-1, -1, -1};
        motion.setOpen(true, now);
        for (; now < start + 800_000_000L; now += 1_000_000L) {
            var frame = motion.update(now, W, H, .5, Double.NaN);
            for (int section = 0; section < sectionStarted.length; section++) {
                if (sectionStarted[section] < 0 && frame.sectionOpacity(section) > 0) sectionStarted[section] = now;
            }
        }
        check(sectionStarted[0] > start, "content waits for expansion threshold");
        near(sectionStarted[1] - sectionStarted[0], 25_000_000L, 1_000_000L, "second section starts25ms later");
        near(sectionStarted[2] - sectionStarted[1], 25_000_000L, 1_000_000L, "third section starts25ms later");
        var expanded = motion.geometry();
        motion.setOpen(false, now);
        for (long until = now + 56_000_000L; now <= until; now += STEP) {
            var frame = motion.update(now, W, H, .5, Double.NaN);
            near(frame.width(), expanded.width(), .001, "panel width holds while content fades");
            near(frame.height(), expanded.height(), .001, "panel height holds while content fades");
        }
        check(motion.geometry().contentOpacity() < expanded.contentOpacity(), "content fades before contraction");
        long closeStart = now - 64_000_000L;
        for (; now <= closeStart + 400_000_000L; now += STEP) motion.update(now, W, H, .5, Double.NaN);
        check(motion.settledClosed(), "closing settles close to320ms");
    }

    private static void interruptionKeepsGeometry() {
        QuickMenuMotion motion = new QuickMenuMotion();
        long start = 1_000_000_000L;
        motion.setOpen(true, start);
        long now = start;
        for (; now < start + 160_000_000L; now += STEP) motion.update(now, W, H, .5, Double.NaN);
        var before = motion.update(now, W, H, .5, Double.NaN);
        motion.setOpen(false, now);
        var reversed = motion.update(now, W, H, .5, Double.NaN);
        same(before, reversed, "closing reversal has no geometry jump");
        now += 48_000_000L;
        motion.update(now, W, H, .5, Double.NaN);
        var closing = motion.geometry();
        motion.setOpen(true, now);
        var reopened = motion.update(now, W, H, .5, Double.NaN);
        same(closing, reopened, "reopening reversal has no geometry jump");
        for (now += STEP; now <= start + 1_000_000_000L; now += STEP) motion.update(now, W, H, .5, Double.NaN);
        check(motion.geometry().progress() > .99, "reversed opening completes");
    }

    private static void dragAndArcReturn() {
        QuickMenuMotion motion = new QuickMenuMotion();
        long start = 1_000_000_000L, now = start;
        motion.setOpen(true, now);
        for (; now < start + 1_000_000_000L; now += STEP) motion.update(now, W, H, .5, Double.NaN);
        motion.setPosition(0, 0);
        for (long until = now + 300_000_000L; now < until; now += STEP) motion.update(now, W, H, .5, Double.NaN);
        var dragged = motion.geometry();
        check(dragged.x() >= 0 && dragged.y() >= 0, "drag clamps top/left edge");
        check(dragged.x() + dragged.width() <= W && dragged.bottom() <= H, "drag clamps bottom/right edge");
        check(Math.abs(dragged.centerX() - W * .5) > 20, "drag moves panel");
        double savedX = motion.centerX(), savedY = motion.bottomY();
        check(Double.isFinite(savedX) && Double.isFinite(savedY), "saved position normalized finite");
        check(savedX >= 0 && savedX <= 1 && savedY >= 0 && savedY <= 1, "saved position relative");
        motion.setOpen(false, now);
        var closingStart = motion.update(now, W, H, .5, Double.NaN);
        near(closingStart.centerX(), dragged.centerX(), .1, "closing starts at dragged location");
        for (long until = now + 800_000_000L; now <= until; now += STEP) motion.update(now, W, H, .5, Double.NaN);
        check(motion.settledClosed(), "closing finishes");
        near(motion.geometry().width(), 0, .001, "closed has zero width");
        near(motion.geometry().height(), 0, .001, "closed has zero height");
        near(motion.geometry().x(), W * .5, .001, "closed returns to anchorX");
        near(motion.geometry().y(), H - 24, .001, "closed returns to anchorY");
        motion.resetPosition();
        near(motion.centerX(), .5, .001, "doubleclick reset center");
        check(Double.isNaN(motion.bottomY()), "reset bottom tracks viewport anchor");
    }

    private static void smallViewportClamps() {
        QuickMenuMotion motion = new QuickMenuMotion();
        long start = 1_000_000_000L;
        motion.setOpen(true, start);
        for (long now = start; now <= start + 1_000_000_000L; now += STEP) motion.update(now, 400, 250, .5, Double.NaN);
        var frame = motion.geometry();
        check(frame.width() <= 368 && frame.height() <= 202, "small screens honor maximum bounds over minimums");
        check(frame.x() >= 0 && frame.y() >= 0, "small viewport has no overflow");
        check(frame.x() + frame.width() <= 400 && frame.bottom() <= 250, "small viewport complete bounds");
    }

    private static void same(QuickMenuMotion.Geometry a, QuickMenuMotion.Geometry b, String reason) {
        near(a.x(), b.x(), .001, reason + " x");
        near(a.y(), b.y(), .001, reason + " y");
        near(a.width(), b.width(), .001, reason + " width");
        near(a.height(), b.height(), .001, reason + " height");
    }

    private static void near(double actual, double expected, double tolerance, String reason) {
        if (Math.abs(actual - expected) > tolerance) throw new AssertionError(reason + ": " + actual + " expected " + expected);
    }
    private static void check(boolean value, String reason) { if (!value) throw new AssertionError(reason); }
}
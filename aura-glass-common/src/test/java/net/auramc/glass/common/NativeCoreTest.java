package net.auramc.glass.common;
import net.auramc.glass.common.render.GlassSpring;
import net.auramc.glass.common.ui.AuraSnapSolver;

public final class NativeCoreTest {
    public static void main(String[] args) {
        AuraSnapSolver a = new AuraSnapSolver();
        a.begin(100, 50); a.x(100, 104, 104); a.x(100, 101, 101);
        check(a.x() == 101, "nearest edge");
        a.begin(100, 50); a.x(100, 101, 101); a.x(100, 104, 104);
        check(a.x() == 101, "sibling order independence");
        a.begin(100, 50); a.x(100, 101, 101); a.x(100, 99, 99);
        check(a.x() == 99, "deterministic tie breaking");
        a.begin(100, 50); a.x(100, 106, 106);
        check(a.x() == 100 && a.xGuide() == -1, "free movement outside threshold");
        a.begin(200, 80); a.x(200, 198, 198); a.y(80, 82, 82);
        check(a.x() - 20 == 178 && a.y() - 30 == 52, "resize uses far edges");
        int previous = 90;
        for (int raw = 90; raw <= 110; raw++) {
            a.begin(raw, 0); a.x(raw, 100, 100);
            int current = a.softX(raw);
            check(Math.abs(current - previous) <= 2, "magnet must not jump at threshold");
            previous = current;
        }
        GlassSpring anim = new GlassSpring(); long start = 1_000_000_000L;
        check(anim.approach(1, start) == 0, "animation initial value");
        float mid = anim.approach(1, start + 90_000_000);
        check(mid > 0 && mid < 1, "intermediate frames");
        check(Math.abs(mid - anim.approach(0, start + 90_000_000)) < .0001, "reversal continuity");
        float settled = mid;
        for (int frame = 1; frame <= 180; frame++) {
            settled = anim.approach(0, start + 90_000_000L + frame * 16_666_667L);
            check(Float.isFinite(settled) && settled >= -.05f && settled <= 1.12f, "bounded spring on reversal");
        }
        check(Math.abs(settled) < .0001f, "spring settles after reversal");
        System.out.println("Native core checks passed (no game process)");
    }
    private static void check(boolean ok, String reason) { if (!ok) throw new AssertionError(reason); }
}

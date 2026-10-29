package net.auramc.glass.client.quickmenu.lyrics;

import java.util.ArrayList;
import java.util.concurrent.atomic.AtomicLong;

public final class KaraokeCoreTest {
    private static final long START = 10_000_000_000L;
    public static void main(String[] args) {
        parserAndTiming(); onsetFixture(); gatesAndNoReplay(); collisionAndDeadzone(); budgetsAndCap(); spatialSettingsAndLongPhrase(); benchmarkCore();
        System.out.println("Karaoke core: enhanced/weighted timing, synthetic onset p95<=50ms, gates/no replay, cone/deadzone, voxel collision, budgets and32body cap PASS");
    }
    private static void parserAndTiming() {
        var enhanced = LyricsTrack.parse("[00:01.00]<00:01.00>Amber <00:01.40>river <00:02.00>glows\n[00:03.00]");
        check(enhanced.words().size() == 3, "enhanced words parsed");
        check(enhanced.words().get(1).startMs() == 1400 && enhanced.words().get(1).endMs() == 2000, "enhanced timestamps exact");
        check(enhanced.activeWord(1420).text().equals("river"), "active word binary lookup");
        var inferred = LyricsTrack.parse("[00:00.00]a radiant, echo.\n[00:03.00]");
        check(!inferred.words().get(0).enhanced(), "plain LRC identified");
        check(inferred.words().get(1).endMs() - inferred.words().get(1).startMs() > inferred.words().get(0).endMs(), "vowels and punctuation receive duration");
        check(inferred.words().get(2).endMs() == 3000, "fallback spans phrase exactly");
        check(LyricsTrack.parse("[ar:Example]\n\n[00:01.0]one").words().size() == 1, "metadata ignored safely");
    }
    private static void onsetFixture() {
        AtomicLong clock = new AtomicLong();
        var engine = new KaraokeEngine(clock::get, box -> false, 14);
        var track = LyricsTrack.parse("[00:01.00]<00:01.00>Amber <00:01.40>river <00:01.80>glows\n[00:03.00]");
        engine.setTrack(track); engine.configure(80, 30);
        var errors = new ArrayList<Long>();
        boolean[] observed = new boolean[3];
        for (long ms = 0; ms <= 2300; ms += 10) {
            clock.set(ms + 50); engine.update(START + ms * 1_000_000L, state(0, 0, false, false));
            for (var body : engine.bodies()) {
                int index = body.word().index();
                if (!observed[index] && ms >= body.word().startMs() && body.scale() >= .98) {
                    errors.add(ms - body.word().startMs()); observed[index] = true;
                }
            }
        }
        check(errors.size() == 3, "all synthetic fixture onsets observed");
        errors.sort(Long::compare); long p95 = errors.get((int) Math.ceil(errors.size() * .95) - 1);
        check(p95 <= 50, "fixture onset p95 within50ms, actual=" + p95);
        System.out.println("Synthetic fixture spring completion onset p95=" + p95 + "ms (enhanced timestamps; inferred timing has no audio-ground-truth claim)");
    }
    private static void gatesAndNoReplay() {
        AtomicLong clock = new AtomicLong();
        var engine = new KaraokeEngine(clock::get, box -> false, 7);
        engine.setTrack(LyricsTrack.parse("[00:01.00]quiet\n[00:01.50]future\n[00:02.00]"));
        clock.set(1000); engine.update(START, state(0, 0, true, false));
        check(engine.bodies().isEmpty(), "GUI forbids spawn");
        clock.set(1250); engine.update(START + 250_000_000L, state(0, 0, false, false));
        check(engine.bodies().isEmpty(), "missed word never replayed on gate release");
        clock.set(1400); engine.update(START + 400_000_000L, state(0, 210, false, false));
        check(!engine.spawningAllowed() && engine.bodies().isEmpty(), "fast yaw gate");
        clock.set(1600); engine.update(START + 600_000_000L, new KaraokeEngine.ListenerState(0, 2, 0, 0, 0, 0, false, false, false, false, false, false, false, 5, 0, true));
        check(!engine.spawningAllowed(), "low health gate");
        clock.set(1800); engine.update(START + 800_000_000L, new KaraokeEngine.ListenerState(0, 2, 0, 0, 0, 0, false, false, false, false, false, false, false, 20, START, true));
        check(!engine.spawningAllowed(), "combat3second gate");
        engine.update(START + 900_000_000L, state(0, 0, false, true));
        check(engine.bodies().isEmpty(), "stopped music clears immediately");
    }
    private static void collisionAndDeadzone() {
        AtomicLong clock = new AtomicLong(900);
        var floor = new KaraokeEngine.CollisionWorld() {
            @Override public boolean blocked(KaraokeEngine.Aabb box) { return box.minY() < 0; }
            @Override public boolean visible(KaraokeEngine.Vec3 from, KaraokeEngine.Vec3 to) { return true; }
        };
        var engine = new KaraokeEngine(clock::get, floor, 55);
        engine.setTrack(LyricsTrack.parse("[00:01.00]stone\n[00:01.10]"));
        engine.update(START, state(0, 0, false, false));
        check(engine.bodies().size() == 1, "word spawned");
        var pos = engine.bodies().get(0).position();
        double angle = Math.abs(Math.toDegrees(Math.atan2(pos.x(), pos.z())));
        double distance = Math.hypot(pos.x(), pos.z());
        check(angle >= 4 && angle <= 12 && distance >= 3 && distance <= 7, "spawn cone and distance");
        double halfAngle = Math.toDegrees(Math.atan(.175 * .5 / distance));
        check(angle - halfAngle >= 3, "word bounds outside3degree deadzone");
        boolean rested = false;
        for (long ms = 950; ms <= 7000; ms += 50) {
            clock.set(ms); engine.update(START + (ms - 900) * 1_000_000L, state(0, 0, false, false));
            for (var body : engine.bodies()) {
                check(body.position().y() >= .069, "voxel floor not penetrated");
                rested |= body.phase() == KaraokeEngine.Phase.RESTING;
            }
        }
        check(rested, "bounced word sleeps on voxel floor");
        check(engine.bodies().isEmpty(), "rest lifetime2to4seconds then fade removes body");
        var blocked = new KaraokeEngine(clock::get, box -> true, 3); blocked.setTrack(LyricsTrack.parse("[00:01.00]test"));
        clock.set(900); blocked.update(START, state(0, 0, false, false)); check(blocked.bodies().isEmpty(), "solid spawn rejected");
        var occluded = new KaraokeEngine(clock::get, new KaraokeEngine.CollisionWorld() {
            public boolean blocked(KaraokeEngine.Aabb box) { return false; }
            public boolean visible(KaraokeEngine.Vec3 from, KaraokeEngine.Vec3 to) { return false; }
        }, 3); occluded.setTrack(LyricsTrack.parse("[00:01.00]test")); occluded.update(START, state(0, 0, false, false));
        check(occluded.bodies().isEmpty(), "occluded spawn rejected");
    }
    private static void budgetsAndCap() {
        AtomicLong clock = new AtomicLong(900);
        var engine = new KaraokeEngine(clock::get, box -> false, 1); engine.setTrack(LyricsTrack.parse("[00:01.00]a b c d e f g h i"));
        engine.update(START, state(0, 0, false, false)); check(engine.groupSize() >= 3 && engine.groupSize() <= 6, "standing3to6 budget");
        engine.update(START + 100_000_000L, state(.08, 0, false, false)); check(engine.groupSize() >= 2 && engine.groupSize() <= 3, "walking2to3 budget");
        engine.update(START + 200_000_000L, state(.2, 100, false, false)); check(engine.groupSize() >= 1 && engine.groupSize() <= 2, "running turning1to2 budget");
        engine.update(START + 300_000_000L, new KaraokeEngine.ListenerState(0, 2, 0, 0, 0, 0, false, false, true, false, false, false, false, 20, 0, true));
        check(engine.groupSize() == 1, "flight one word");
        StringBuilder lrc = new StringBuilder();
        for (int i = 0; i < 100; i++) lrc.append(String.format(java.util.Locale.ROOT, "[00:%02d.%02d]test\n", i / 10, i % 10 * 10));
        engine.setTrack(LyricsTrack.parse(lrc.toString()));
        for (long ms = 0; ms < 15_000; ms += 10) {
            clock.set(ms); engine.update(START + ms * 1_000_000L, state(0, 0, false, false));
            check(engine.bodies().size() <= KaraokeEngine.MAX_BODIES, "bounded32bodies");
        }
    }
    private static void spatialSettingsAndLongPhrase() {
        AtomicLong clock = new AtomicLong(900);
        var near = new KaraokeEngine(clock::get, box -> false, 23);
        var far = new KaraokeEngine(clock::get, box -> false, 23);
        var fixture = LyricsTrack.parse("[00:01.00]test\n[00:01.20]");
        near.setTrack(fixture); far.setTrack(fixture);
        near.configureSpatial(3, 0, false); far.configureSpatial(7, 2, true);
        near.update(START, state(0, 0, false, false)); far.update(START, state(0, 0, false, false));
        check(near.groupSize() == 3 && far.groupSize() == 6, "density selects legal standing range endpoints");
        check(distance(far.bodies().get(0).position()) > distance(near.bodies().get(0).position()) + 2, "configured distance moves spawn band");
        near.update(START + 100_000_000L, state(.08, 0, false, false)); far.update(START + 100_000_000L, state(.08, 0, false, false));
        check(near.groupSize() == 2 && far.groupSize() == 3, "walking density never exceeds2to3");
        near.update(START + 200_000_000L, state(.2, 0, false, false)); far.update(START + 200_000_000L, state(.2, 0, false, false));
        check(near.groupSize() == 1 && far.groupSize() == 2, "running density never exceeds1to2");
        for (long ms = 1000; ms < 2600; ms += 50) {
            clock.set(ms); near.update(START + (ms - 900) * 1_000_000L, state(0, 0, false, false));
            check(near.bodies().stream().noneMatch(b -> b.phase() == KaraokeEngine.Phase.FALLING || b.phase() == KaraokeEngine.Phase.RESTING), "physics disabled never falls");
        }
        check(near.bodies().isEmpty(), "physics disabled still fades after phrase hold");
        var track = LyricsTrack.parse("[00:01.00]<00:01.00>a <00:01.40>b <00:01.80>c <00:02.20>d <00:02.60>e <00:03.00>f <00:03.40>g <00:03.80>h <00:04.20>i <00:04.60>j <00:05.00>k <00:05.40>l\n[00:06.00]");
        var engine = new KaraokeEngine(clock::get, box -> false, 59); engine.setTrack(track);
        engine.configureSpatial(5, 0, true); boolean[] observed = new boolean[track.words().size()];
        for (long ms = 800; ms < 5900; ms += 20) {
            clock.set(ms); engine.update(START + ms * 1_000_000L, state(0, 0, false, false));
            for (var body : engine.bodies()) observed[body.word().index()] = true;
        }
        for (int i = 0; i < observed.length; i++) check(observed[i], "long phrase word retained beyond initial budget index=" + i);
    }
    private static double distance(KaraokeEngine.Vec3 p) { return Math.hypot(p.x(), p.z()); }
    private static void benchmarkCore() {
        AtomicLong clock = new AtomicLong();
        var engine = new KaraokeEngine(clock::get, box -> false, 4);
        engine.setTrack(LyricsTrack.parse("[00:01.00]test\n[00:02.00]fixture"));
        var stopped = state(0, 0, false, true);
        for (int i = 0; i < 20_000; i++) { engine.update(START + i, stopped); engine.bodies(); }
        long[] samples = new long[200];
        for (int batch = 0; batch < samples.length; batch++) {
            long begin = System.nanoTime();
            for (int i = 0; i < 100; i++) { engine.update(START + batch * 100L + i, stopped); engine.bodies(); }
            samples[batch] = (System.nanoTime() - begin) / 100;
        }
        java.util.Arrays.sort(samples); double p95ms = samples[(int) (samples.length * .95)] / 1_000_000.0;
        System.out.println("Pure stopped core p95 per update+snapshot=" + p95ms + "ms; excludes renderer/audio/MC integration");
        check(p95ms < .3, "stopped pure core budget below.3ms on test host");
    }
    private static KaraokeEngine.ListenerState state(double speed, double yawRate, boolean gui, boolean stopped) {
        return new KaraokeEngine.ListenerState(0, 2, 0, 0, speed, yawRate, false, false, false, false, gui, false, false, 20, 0, !stopped);
    }
    private static void check(boolean value, String reason) { if (!value) throw new AssertionError(reason); }
}
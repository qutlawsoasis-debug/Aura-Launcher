package net.auramc.glass.common.render;

public final class GpuGlassTimersTest {
    public static void main(String[] args) {
        FakeQueries api = new FakeQueries();
        GpuGlassTimers timers = new GpuGlassTimers(api);
        timers.beginFrame(true);
        boolean started = timers.begin(GpuGlassTimers.GLASS);
        check(started, "first query starts");
        check(!timers.begin(GpuGlassTimers.CAPTURE), "own queries never nest");
        timers.end(started);
        timers.beginFrame(true);
        timers.beginFrame(true);
        check(api.availabilityChecks == 0 && api.reads == 0, "no result polling in first two frames");
        timers.beginFrame(true);
        check(api.availabilityChecks == 1 && api.reads == 0, "pending GPU query is not synchronously read");
        api.ready = true;
        timers.beginFrame(true);
        check(api.reads == 1 && timers.milliseconds(GpuGlassTimers.GLASS) == 2.0, "available delayed result converted to milliseconds");
        api.current = 777;
        check(!timers.begin(GpuGlassTimers.BLUR), "foreign elapsed query is not nested");
        api.current = 0;
        timers.beginFrame(false);
        check(!timers.begin(GpuGlassTimers.BLUR), "disabled overlay issues no GPU query");
        timers.close();
        check(api.deleted == api.created, "shutdown releases every query");

        FakeQueries delayed = new FakeQueries();
        GpuGlassTimers busy = new GpuGlassTimers(delayed);
        for (int i = 0; i < 8; i++) {
            busy.beginFrame(true);
            boolean query = busy.begin(GpuGlassTimers.CAPTURE);
            check(query, "free frame slot accepts query");
            busy.end(query);
        }
        busy.beginFrame(true);
        check(!busy.begin(GpuGlassTimers.CAPTURE), "ring exhaustion skips rather than overwrites pending query");
        check(delayed.created == 8 && delayed.reads == 0, "GPU lag never forces result read or unbounded allocation");
        delayed.ready = true;
        busy.beginFrame(true);
        check(delayed.reads > 0, "completed slots can be reclaimed after lag");
        busy.close();

        FakeQueries unsupported = new FakeQueries();
        unsupported.supported = false;
        GpuGlassTimers unavailable = new GpuGlassTimers(unsupported);
        unavailable.beginFrame(true);
        check(!unavailable.begin(GpuGlassTimers.BLUR) && unsupported.created == 0,
                "unsupported driver disables timers without creating query");
        check(unavailable.unavailableReason() != null, "unsupported status remains visible");
        unavailable.close();
        System.out.println("GPU query checks passed: delayed reads, no stalls, no nesting, disabled path and cleanup");
    }

    private static void check(boolean ok, String reason) { if (!ok) throw new AssertionError(reason); }

    private static final class FakeQueries implements GpuGlassTimers.QueryApi {
        int created, deleted, current, availabilityChecks, reads;
        boolean ready, supported = true;
        public boolean supported() { return supported; }
        public int create() { return ++created; }
        public int current() { return current; }
        public void begin(int query) { current = query; }
        public void end() { current = 0; }
        public boolean available(int query) { availabilityChecks++; return ready; }
        public long result(int query) { if (!ready) throw new AssertionError("blocking read"); reads++; return 2_000_000; }
        public void delete(int query) { deleted++; }
    }
}

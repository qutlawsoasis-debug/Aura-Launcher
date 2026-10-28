package net.auramc.glass.common.render;

import java.util.Arrays;
import org.lwjgl.opengl.GL;
import static org.lwjgl.opengl.GL33C.*;

public final class GpuGlassTimers {
    public static final int CAPTURE = 0, BLUR = 1, GLASS = 2;
    private static final int SLOT_COUNT = 8, MAX_QUERIES = 64;
    private final QueryApi api;
    private final Slot[] slots = new Slot[SLOT_COUNT];
    private final double[] smoothed = {-1, -1, -1};
    private final long[] measuredFrame = {-1, -1, -1};
    private final double[] elapsed = new double[3];
    private final boolean[] measured = new boolean[3];
    private String unavailable;
    private Slot current;
    private int activeQuery;
    private long frame;
    private boolean enabled;

    public GpuGlassTimers() { this(new OpenGlQueries()); }

    public void prepare() {
        if(unavailable!=null)return;
        try {
            if(!api.supported())return;
            for(Slot slot:slots)for(int i=0;i<slot.queries.length;i++)if(slot.queries[i]==0)slot.queries[i]=api.create();
        }catch(RuntimeException error){fail(error);}
    }

    GpuGlassTimers(QueryApi api) {
        this.api = api;
        for (int i = 0; i < slots.length; i++) slots[i] = new Slot();
    }

    public void beginFrame(boolean enabled) {
        this.enabled = enabled;
        frame++;
        current = null;
        if (!enabled || unavailable != null) return;
        try {
            if (!api.supported()) {
                unavailable = "GPU timers unavailable: OpenGL 3.3 / ARB_timer_query unsupported";
                GlassDiagnostics.log(unavailable);
                return;
            }
            prepareSlot();
        } catch (RuntimeException error) { fail(error); }
    }

    private void prepareSlot() {
        for (Slot slot : slots) {
            if (slot.pending && frame - slot.frame >= 3 && available(slot)) {
                Arrays.fill(elapsed,0);
                Arrays.fill(measured,false);
                for (int i = 0; i < slot.count; i++) {
                    int channel = slot.channels[i];
                    elapsed[channel] += api.result(slot.queries[i]) / 1_000_000.0;
                    measured[channel] = true;
                }
                for (int i = 0; i < 3; i++) {
                    if (measured[i]) {
                        smoothed[i] = smoothed[i] < 0 ? elapsed[i] : smoothed[i] * .85 + elapsed[i] * .15;
                        measuredFrame[i] = slot.frame;
                    }
                }
                slot.pending = false;
                slot.count = 0;
            }
        }
        Slot next = slots[(int) (frame % SLOT_COUNT)];
        if (!next.pending) {
            next.frame = frame;
            next.count = 0;
            current = next;
        }
    }

    private boolean available(Slot slot) {
        for (int i = 0; i < slot.count; i++) {
            if (!api.available(slot.queries[i])) return false;
        }
        return true;
    }

    public boolean begin(int channel) {
        if (!enabled || unavailable != null || current == null || current.count >= MAX_QUERIES || activeQuery != 0) return false;
        try { return startQuery(channel); }
        catch (RuntimeException error) { fail(error); return false; }
    }

    private boolean startQuery(int channel) {
        // TIME_ELAPSED cannot nest inside another renderer's active query.
        if (api.current() != 0) return false;
        int index = current.count;
        if (current.queries[index] == 0) current.queries[index] = api.create();
        if (current.queries[index] == 0) throw new IllegalStateException("glGenQueries returned 0");
        activeQuery = current.queries[index];
        current.channels[index] = channel;
        api.begin(activeQuery);
        current.count++;
        current.pending = true;
        return true;
    }

    public void end(boolean started) {
        if (!started || activeQuery == 0) return;
        try { api.end(); }
        catch (RuntimeException error) { fail(error); }
        finally { activeQuery = 0; }
    }

    private void fail(RuntimeException error) {
        unavailable = "GPU timer failure: " + error.getClass().getSimpleName() + ": " + error.getMessage();
        GlassDiagnostics.log(unavailable);
        current = null;
    }

    public String unavailableReason() { return unavailable; }
    public long measuredAge(int channel) { return measuredFrame[channel] < 0 ? -1 : frame - measuredFrame[channel]; }
    public double milliseconds(int channel) { return smoothed[channel]; }

    public void close() {
        if (activeQuery != 0) {
            try { api.end(); } catch (RuntimeException error) { fail(error); }
        }
        activeQuery = 0;
        for (Slot slot : slots) {
            for (int query : slot.queries) if (query != 0) {
                try { api.delete(query); } catch (RuntimeException error) { fail(error); }
            }
            Arrays.fill(slot.queries, 0);
            slot.pending = false;
            slot.count = 0;
        }
        Arrays.fill(smoothed, -1);
        Arrays.fill(measuredFrame, -1);
    }

    interface QueryApi {
        default boolean supported() { return true; }
        int create();
        int current();
        void begin(int query);
        void end();
        boolean available(int query);
        long result(int query);
        void delete(int query);
    }

    private static final class OpenGlQueries implements QueryApi {
        public boolean supported() { return GL.getCapabilities().OpenGL33 || GL.getCapabilities().GL_ARB_timer_query; }
        public int create() { return glGenQueries(); }
        public int current() { return glGetQueryi(GL_TIME_ELAPSED, GL_CURRENT_QUERY); }
        public void begin(int query) { glBeginQuery(GL_TIME_ELAPSED, query); }
        public void end() { glEndQuery(GL_TIME_ELAPSED); }
        public boolean available(int query) { return glGetQueryObjecti(query, GL_QUERY_RESULT_AVAILABLE) != GL_FALSE; }
        public long result(int query) { return glGetQueryObjectui64(query, GL_QUERY_RESULT); }
        public void delete(int query) { glDeleteQueries(query); }
    }

    private static final class Slot {
        final int[] queries = new int[MAX_QUERIES];
        final int[] channels = new int[MAX_QUERIES];
        long frame;
        int count;
        boolean pending;
    }
}

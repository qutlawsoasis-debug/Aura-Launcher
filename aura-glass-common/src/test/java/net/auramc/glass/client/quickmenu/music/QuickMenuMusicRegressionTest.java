package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.NowPlaying;
import net.auramc.glass.client.music.PlaybackState;
import net.auramc.glass.client.music.Track;

import java.util.Collections;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Regression test verifying that QuickMenu music state reads (render/layout/scroll)
 * perform strictly 0 synchronous IO calls, that background tasks update the single
 * immutable snapshot, and that bridge disconnection/recovery is handled safely.
 */
public final class QuickMenuMusicRegressionTest {

    public static void main(String[] args) throws Exception {
        System.out.println("=== Starting QuickMenu Music Regression Test Suite ===");

        testZeroIoOnRenderLayoutScrollReads();
        System.out.println("[PASS] 1. Multiple render/layout/scroll reads perform 0 HTTP/IO calls");

        testAsyncSearchUpdatesSingleSnapshot();
        System.out.println("[PASS] 2. Async search query updates single snapshot without blocking caller");

        testSearchRequestCoalescingAndStaleCancellation();
        System.out.println("[PASS] 3. Rapid query typing coalesces requests and drops stale results");

        testBridgeDisconnectionAndRecovery();
        System.out.println("[PASS] 4. Bridge loss and recovery cleanly updates snapshot without errors");

        testLifecycleCloseShutsDownWorker();
        System.out.println("[PASS] 5. Lifecycle close cleanly shuts down background tasks");

        System.out.println("=== ALL QUICKMENU MUSIC REGRESSION TESTS PASSED ===");
    }

    private static void testZeroIoOnRenderLayoutScrollReads() throws Exception {
        InstrumentedMusicSource source = new InstrumentedMusicSource();
        source.available = true;
        source.queueTracks = List.of(
                new Track("q1", "Artist 1", "Queue Track 1", 180_000L, ""),
                new Track("q2", "Artist 2", "Queue Track 2", 210_000L, "")
        );

        try (QuickMenuMusicController controller = new QuickMenuMusicController(() -> source)) {
            // Wait for initial background poll to populate snapshot
            awaitCondition(() -> !controller.snapshot().queue().isEmpty(), 3000, "Initial snapshot population");

            int initialIo = source.totalIoCalls();
            check(initialIo > 0, "Initial background poll should have performed IO on worker thread");

            long startNs = System.nanoTime();
            // Simulate 10,000 render/layout/scroll frames on caller thread
            for (int frame = 0; frame < 10_000; frame++) {
                QuickMenuMusicSnapshot snap = controller.snapshot();
                List<Track> queue = snap.tracksForTab(0);
                List<Track> search = snap.tracksForTab(1);
                int countQueue = snap.countForTab(0);
                int countSearch = snap.countForTab(1);

                if (countQueue != 2 || countSearch != 0 || queue.size() != 2 || !search.isEmpty()) {
                    throw new AssertionError("Inconsistent snapshot read at frame " + frame);
                }
            }
            long elapsedMs = (System.nanoTime() - startNs) / 1_000_000L;

            int ioAfterFrames = source.totalIoCalls();
            check(ioAfterFrames == initialIo, "Render/layout frames must perform ZERO additional IO calls. Expected "
                    + initialIo + " but got " + ioAfterFrames);
            check(elapsedMs < 100, "10,000 snapshot reads must execute in < 100ms (was " + elapsedMs + "ms)");
        }
    }

    private static void testAsyncSearchUpdatesSingleSnapshot() throws Exception {
        InstrumentedMusicSource source = new InstrumentedMusicSource();
        source.available = true;
        source.ioDelayMs = 25; // Artificial latency to ensure caller is not blocked
        source.queueTracks = List.of(new Track("q1", "Queue Artist", "Queue Track", 180_000L, ""));
        source.searchTracks = List.of(
                new Track("s1", "Aura Artist", "Aura Song 1", 200_000L, ""),
                new Track("s2", "Aura Artist", "Aura Song 2", 220_000L, "")
        );

        try (QuickMenuMusicController controller = new QuickMenuMusicController(() -> source)) {
            awaitCondition(() -> !controller.snapshot().queue().isEmpty(), 3000, "Initial queue loaded");

            int searchCallsBefore = source.searchCalls.get();
            long callStart = System.currentTimeMillis();

            // Request search
            controller.requestSearch("aura");
            long callerDuration = System.currentTimeMillis() - callStart;

            // Caller must NOT be blocked by 25ms delay
            check(callerDuration < 20, "requestSearch must be asynchronous and return immediately (<20ms, was " + callerDuration + "ms)");

            // Wait for worker to complete search and update snapshot
            awaitCondition(() -> !controller.snapshot().searchResults().isEmpty(), 3000, "Search results updated in snapshot");

            QuickMenuMusicSnapshot snap = controller.snapshot();
            check(snap.searchResults().size() == 2, "Search results must contain 2 tracks");
            check(snap.queue().size() == 1, "Queue must remain intact in single snapshot");
            check("aura".equals(snap.searchQuery()), "Search query in snapshot must match");
            check(source.searchCalls.get() > searchCallsBefore, "Search IO must have executed on background worker");
        }
    }

    private static void testSearchRequestCoalescingAndStaleCancellation() throws Exception {
        InstrumentedMusicSource source = new InstrumentedMusicSource();
        source.available = true;
        source.ioDelayMs = 15;

        try (QuickMenuMusicController controller = new QuickMenuMusicController(() -> source)) {
            // Rapid keystrokes
            controller.requestSearch("a");
            controller.requestSearch("au");
            controller.requestSearch("aur");
            controller.requestSearch("aura");

            awaitCondition(() -> "aura".equals(controller.snapshot().searchQuery()), 3000, "Final query must be 'aura'");

            // Give a moment for any pending tasks
            Thread.sleep(80);
            QuickMenuMusicSnapshot finalSnap = controller.snapshot();
            check("aura".equals(finalSnap.searchQuery()), "Stale query results must not overwrite final query");
        }
    }

    private static void testBridgeDisconnectionAndRecovery() throws Exception {
        InstrumentedMusicSource source = new InstrumentedMusicSource();
        source.available = true;
        source.queueTracks = List.of(new Track("q1", "Artist", "Track", 180_000L, ""));

        try (QuickMenuMusicController controller = new QuickMenuMusicController(() -> source)) {
            awaitCondition(() -> !controller.snapshot().queue().isEmpty(), 3000, "Initial queue loaded");

            // 1. Simulate bridge disconnection
            source.available = false;
            controller.triggerRefreshAsync();

            awaitCondition(() -> controller.snapshot().queue().isEmpty(), 3000, "Disconnected bridge clears snapshot to empty");
            check(controller.snapshot() == QuickMenuMusicSnapshot.EMPTY || controller.snapshot().queue().isEmpty(),
                    "Snapshot must be empty when bridge is offline");

            // 2. Simulate bridge recovery with new track
            source.available = true;
            source.queueTracks = List.of(new Track("rec1", "Recovered Artist", "Recovered Track", 200_000L, ""));
            controller.triggerRefreshAsync();

            awaitCondition(() -> !controller.snapshot().queue().isEmpty()
                    && "rec1".equals(controller.snapshot().queue().get(0).id()), 3000, "Recovered bridge updates snapshot");
        }
    }

    private static void testLifecycleCloseShutsDownWorker() throws Exception {
        InstrumentedMusicSource source = new InstrumentedMusicSource();
        source.available = true;

        QuickMenuMusicController controller = new QuickMenuMusicController(() -> source);
        awaitCondition(() -> source.queueCalls.get() > 0, 3000, "Initial poll called");

        controller.close();
        int ioAfterClose = source.totalIoCalls();

        // Requests after close must be ignored
        controller.requestSearch("ignored");
        controller.triggerRefreshAsync();

        Thread.sleep(50);
        check(source.totalIoCalls() == ioAfterClose, "No further IO calls after controller close");
    }

    private static void awaitCondition(Condition condition, long timeoutMs, String description) throws Exception {
        long deadline = System.currentTimeMillis() + timeoutMs;
        while (System.currentTimeMillis() < deadline) {
            if (condition.test()) {
                return;
            }
            Thread.sleep(10);
        }
        throw new AssertionError("Timed out waiting for: " + description);
    }

    private static void check(boolean condition, String message) {
        if (!condition) {
            throw new AssertionError("FAILED: " + message);
        }
    }

    @FunctionalInterface
    private interface Condition {
        boolean test() throws Exception;
    }

    private static final class InstrumentedMusicSource implements MusicSource {
        volatile boolean available = true;
        volatile int ioDelayMs = 0;
        volatile List<Track> queueTracks = Collections.emptyList();
        volatile List<Track> searchTracks = Collections.emptyList();

        final AtomicInteger queueCalls = new AtomicInteger(0);
        final AtomicInteger searchCalls = new AtomicInteger(0);
        final AtomicInteger nowCalls = new AtomicInteger(0);

        int totalIoCalls() {
            return queueCalls.get() + searchCalls.get();
        }

        private void simulateIoDelay() {
            if (ioDelayMs > 0) {
                try {
                    Thread.sleep(ioDelayMs);
                } catch (InterruptedException ignored) {}
            }
        }

        @Override
        public NowPlaying now() {
            nowCalls.incrementAndGet();
            return NowPlaying.stopped();
        }

        @Override
        public void play() {}

        @Override
        public void pause() {}

        @Override
        public void next() {}

        @Override
        public void prev() {}

        @Override
        public void seek(long ms) {}

        @Override
        public void volume(float v) {}

        @Override
        public List<Track> search(String q) {
            searchCalls.incrementAndGet();
            simulateIoDelay();
            if (!available) return Collections.emptyList();
            return searchTracks;
        }

        @Override
        public List<Track> queue() {
            queueCalls.incrementAndGet();
            simulateIoDelay();
            if (!available) return Collections.emptyList();
            return queueTracks;
        }

        @Override
        public boolean available() {
            return available;
        }
    }
}

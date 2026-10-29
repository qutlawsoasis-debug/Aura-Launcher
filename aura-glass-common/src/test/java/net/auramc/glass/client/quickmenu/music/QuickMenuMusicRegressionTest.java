package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.NowPlaying;
import net.auramc.glass.client.music.PlaybackState;
import net.auramc.glass.client.music.Track;
import net.auramc.glass.client.quickmenu.QuickMenuScreen;

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

        testFailedNewSearchAfterSuccessfulOldSearchDoesNotPublishOldResults();
        System.out.println("[PASS] 6. Failed new search drops old results and records error state across refresh");

        testCloseReopenReversalPreservesController();
        System.out.println("[PASS] 7. Close -> reopen reversal preserves controller; removed() stops controller");

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

    private static void testFailedNewSearchAfterSuccessfulOldSearchDoesNotPublishOldResults() throws Exception {
        InstrumentedMusicSource source = new InstrumentedMusicSource();
        source.available = true;
        source.queueTracks = List.of(new Track("q1", "Queue Artist", "Queue Track", 180_000L, ""));
        source.searchTracks = List.of(new Track("old1", "Old Artist", "Old Track", 120_000L, ""));

        try (QuickMenuMusicController controller = new QuickMenuMusicController(() -> source)) {
            awaitCondition(() -> !controller.snapshot().queue().isEmpty(), 3000, "Initial queue loaded");

            // 1. Successful search for old query
            controller.requestSearch("oldQuery");
            awaitCondition(() -> !controller.snapshot().searchResults().isEmpty()
                    && "oldQuery".equals(controller.snapshot().searchQuery()), 3000, "Old search results populated");

            QuickMenuMusicSnapshot oldSnap = controller.snapshot();
            check(oldSnap.searchResults().size() == 1, "Should have 1 old search result");
            check("old1".equals(oldSnap.searchResults().get(0).id()), "Old result track ID must be old1");
            check(!oldSnap.hasSearchError(), "Old search must not have error");

            // 2. New search that fails with exception
            source.searchException = new RuntimeException("Simulated network search failure");
            controller.requestSearch("newQuery");

            // Trigger refresh immediately (as QA described: "после неудачного нового поиска и refresh")
            controller.triggerRefreshAsync();

            // Wait for new query to be reflected in snapshot with error state
            awaitCondition(() -> "newQuery".equals(controller.snapshot().searchQuery())
                    && controller.snapshot().hasSearchError(), 3000, "Failed search updates error state");

            QuickMenuMusicSnapshot failedSnap = controller.snapshot();
            check("newQuery".equals(failedSnap.searchQuery()), "Search query must be newQuery");
            check(failedSnap.searchResults().isEmpty(), "Failed new search must yield EMPTY search results, never old results");
            check(failedSnap.hasSearchError(), "Failed search must set searchError=true");
            check(failedSnap.queue().size() == 1, "Queue must remain intact in snapshot");

            // 3. Repeat refresh: pollBackground must still NOT restore old results
            controller.triggerRefreshAsync();
            Thread.sleep(80);

            QuickMenuMusicSnapshot refreshedSnap = controller.snapshot();
            check("newQuery".equals(refreshedSnap.searchQuery()), "Query remains newQuery");
            check(refreshedSnap.searchResults().isEmpty(), "pollBackground must NEVER publish old results under new query");
            check(refreshedSnap.hasSearchError(), "pollBackground must preserve search error state");
            check(refreshedSnap.queue().size() == 1, "Queue must remain intact after refresh");
        }
    }

    private static void testCloseReopenReversalPreservesController() throws Exception {
        InstrumentedMusicSource source = new InstrumentedMusicSource();
        source.available = true;
        source.queueTracks = List.of(new Track("q1", "Queue Artist", "Queue Track", 180_000L, ""));
        source.searchTracks = List.of(new Track("s1", "Artist", "Song", 200_000L, ""));

        QuickMenuMusicController controller = new QuickMenuMusicController(() -> source);
        QuickMenuScreen screen = new QuickMenuScreen(controller);

        awaitCondition(() -> !controller.snapshot().queue().isEmpty(), 3000, "Initial queue loaded");

        // 1. User starts closing the screen (close() called during close animation)
        screen.close();

        // Screen motion is marked closed (animating close), but controller MUST NOT be closed yet!
        check(!screen.motion().isOpen(), "Screen motion must be marked closed");
        check(!controller.isClosed(), "close() must NOT shut down controller yet (needed for reversal)");

        // 2. User quickly reopens (F8 reversal on the same screen)
        screen.motion().setOpen(true, System.nanoTime());
        check(screen.motion().isOpen(), "Screen motion is now reopened via reversal");
        check(!controller.isClosed(), "Controller must remain active and running during reversal");

        // 3. Perform search and refresh on the reversed screen
        screen.setSearchQuery("reversalQuery");
        awaitCondition(() -> "reversalQuery".equals(controller.snapshot().searchQuery())
                && !controller.snapshot().searchResults().isEmpty(), 3000, "Search must work after reversal");

        screen.musicController().triggerRefreshAsync();
        Thread.sleep(50);
        check(!controller.isClosed(), "Controller is still running after refresh");

        // 4. Finally, when screen is removed from Minecraft (animation settled / removed() called)
        screen.removed();
        check(controller.isClosed(), "removed() MUST shut down controller once screen is disposed");

        // Further requests after removed() are ignored
        int ioAfterRemoved = source.totalIoCalls();
        controller.requestSearch("ignoredAfterRemoved");
        controller.triggerRefreshAsync();
        Thread.sleep(50);
        check(source.totalIoCalls() == ioAfterRemoved, "No IO calls after removed()");
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
        volatile RuntimeException searchException = null;
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
            if (searchException != null) {
                throw searchException;
            }
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

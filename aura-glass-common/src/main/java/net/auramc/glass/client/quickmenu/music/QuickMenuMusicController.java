package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.Track;

import java.util.Collections;
import java.util.List;
import java.util.Objects;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.ScheduledFuture;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Supplier;

/**
 * Background controller managing asynchronous queue polling and search updates
 * for the QuickMenu music section. Ensures zero IO calls on the render thread.
 */
public final class QuickMenuMusicController implements AutoCloseable {
    private static final long PERIODIC_INTERVAL_MS = 2000L;

    private final Supplier<MusicSource> sourceSupplier;
    private final ScheduledExecutorService executor;
    private final ScheduledFuture<?> periodicFuture;

    private final AtomicLong searchEpoch = new AtomicLong(0);
    private final AtomicLong refreshEpoch = new AtomicLong(0);

    private volatile QuickMenuMusicSnapshot snapshot = QuickMenuMusicSnapshot.EMPTY;
    private volatile String activeSearchQuery = "";
    private volatile boolean closed = false;

    public QuickMenuMusicController() {
        this(MusicSourceProvider::get);
    }

    public QuickMenuMusicController(Supplier<MusicSource> sourceSupplier) {
        this.sourceSupplier = Objects.requireNonNull(sourceSupplier, "sourceSupplier");
        this.executor = Executors.newSingleThreadScheduledExecutor(r -> {
            Thread t = new Thread(r, "Aura-QuickMenu-Music-Worker");
            t.setDaemon(true);
            return t;
        });
        this.periodicFuture = this.executor.scheduleWithFixedDelay(
                this::pollBackground,
                0L,
                PERIODIC_INTERVAL_MS,
                TimeUnit.MILLISECONDS
        );
    }

    /**
     * Obtains the current immutable snapshot.
     * Pure cached in-memory read: strictly 0 IO, safe for render loop.
     */
    public QuickMenuMusicSnapshot snapshot() {
        return snapshot;
    }

    /**
     * Asynchronously requests search results for the given query without blocking caller.
     * Older pending searches are coalesced and discarded.
     */
    public void requestSearch(String query) {
        if (closed) return;
        final String target = (query != null) ? query : "";
        this.activeSearchQuery = target;
        final long epoch = searchEpoch.incrementAndGet();

        executor.execute(() -> {
            if (closed || epoch != searchEpoch.get()) return;
            MusicSource src = sourceSupplier.get();
            if (src == null || !src.available()) {
                if (!closed && epoch == searchEpoch.get()) {
                    snapshot = new QuickMenuMusicSnapshot(snapshot.queue(), Collections.emptyList(), target);
                }
                return;
            }

            try {
                List<Track> results = target.isEmpty() ? Collections.emptyList() : src.search(target);
                if (closed || epoch != searchEpoch.get()) return;
                List<Track> safeResults = (results != null) ? results : Collections.emptyList();
                snapshot = new QuickMenuMusicSnapshot(snapshot.queue(), safeResults, target);
            } catch (Throwable ignored) {
            }
        });
    }

    /**
     * Asynchronously triggers an immediate background refresh of queue and state
     * (e.g. upon opening the menu or switching tabs).
     */
    public void triggerRefreshAsync() {
        if (closed) return;
        final long epoch = refreshEpoch.incrementAndGet();
        executor.execute(() -> {
            if (closed || epoch != refreshEpoch.get()) return;
            pollBackground();
        });
    }

    private void pollBackground() {
        if (closed) return;
        try {
            MusicSource src = sourceSupplier.get();
            if (src == null || !src.available()) {
                if (snapshot != QuickMenuMusicSnapshot.EMPTY) {
                    snapshot = QuickMenuMusicSnapshot.EMPTY;
                }
                return;
            }

            List<Track> queue = src.queue();
            List<Track> safeQueue = (queue != null) ? queue : Collections.emptyList();

            String query = activeSearchQuery;
            List<Track> currentSearch = snapshot.searchResults();
            if (!query.isEmpty() && (currentSearch.isEmpty() || searchEpoch.get() == 0)) {
                List<Track> s = src.search(query);
                currentSearch = (s != null) ? s : Collections.emptyList();
            }

            if (!closed) {
                snapshot = new QuickMenuMusicSnapshot(safeQueue, currentSearch, query);
            }
        } catch (Throwable ignored) {
        }
    }

    @Override
    public void close() {
        if (closed) return;
        closed = true;
        if (periodicFuture != null) {
            periodicFuture.cancel(true);
        }
        executor.shutdownNow();
    }
}

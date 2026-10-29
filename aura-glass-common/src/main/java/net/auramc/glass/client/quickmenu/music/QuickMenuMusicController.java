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

    public boolean isClosed() {
        return closed;
    }

    public long currentSearchEpoch() {
        return searchEpoch.get();
    }

    public String activeSearchQuery() {
        return activeSearchQuery;
    }

    /**
     * Obtains the current immutable snapshot.
     * Pure cached in-memory read: strictly 0 IO, safe for render loop.
     * Validates that search results match current epoch/query upon reading.
     */
    public QuickMenuMusicSnapshot snapshot() {
        QuickMenuMusicSnapshot current = this.snapshot;
        long currentEpoch = searchEpoch.get();
        String currentQuery = activeSearchQuery;
        if (current.searchEpoch() == currentEpoch && Objects.equals(current.searchQuery(), currentQuery)) {
            return current;
        }
        return new QuickMenuMusicSnapshot(
                current.queue(),
                Collections.emptyList(),
                currentQuery,
                currentEpoch,
                false
        );
    }

    /**
     * Asynchronously requests search results for the given query without blocking caller.
     * Older pending searches are coalesced and discarded.
     * Failed searches record error state and yield empty results (never stale results).
     */
    public void requestSearch(String query) {
        if (closed) return;
        final String target = (query != null) ? query : "";
        final long epoch = searchEpoch.incrementAndGet();
        this.activeSearchQuery = target;

        if (target.isEmpty()) {
            this.snapshot = new QuickMenuMusicSnapshot(this.snapshot.queue(), Collections.emptyList(), "", epoch, false);
            return;
        }

        // Immediately update snapshot to clear out old search results for the new query/epoch
        this.snapshot = new QuickMenuMusicSnapshot(this.snapshot.queue(), Collections.emptyList(), target, epoch, false);

        executor.execute(() -> {
            if (closed || epoch != searchEpoch.get() || !target.equals(activeSearchQuery)) return;
            MusicSource src = sourceSupplier.get();
            if (src == null || !src.available()) {
                if (!closed && epoch == searchEpoch.get() && target.equals(activeSearchQuery)) {
                    snapshot = new QuickMenuMusicSnapshot(snapshot.queue(), Collections.emptyList(), target, epoch, false);
                }
                return;
            }

            try {
                List<Track> results = src.search(target);
                if (closed || epoch != searchEpoch.get() || !target.equals(activeSearchQuery)) return;
                List<Track> safeResults = (results != null) ? results : Collections.emptyList();
                snapshot = new QuickMenuMusicSnapshot(snapshot.queue(), safeResults, target, epoch, false);
            } catch (Throwable t) {
                // Ошибка нового поиска -> пустой список + состояние ошибки, не старые результаты
                if (!closed && epoch == searchEpoch.get() && target.equals(activeSearchQuery)) {
                    snapshot = new QuickMenuMusicSnapshot(snapshot.queue(), Collections.emptyList(), target, epoch, true);
                }
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

            QuickMenuMusicSnapshot current = this.snapshot;
            long curEpoch = searchEpoch.get();
            String curQuery = activeSearchQuery;

            List<Track> safeSearch;
            boolean curError;
            // При публикации - принимать только если query/epoch совпадают с текущими
            if (current.searchEpoch() == curEpoch && Objects.equals(current.searchQuery(), curQuery)) {
                safeSearch = current.searchResults();
                curError = current.searchError();
            } else {
                safeSearch = Collections.emptyList();
                curError = false;
            }

            if (!closed) {
                snapshot = new QuickMenuMusicSnapshot(safeQueue, safeSearch, curQuery, curEpoch, curError);
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

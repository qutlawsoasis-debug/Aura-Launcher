package net.auramc.glass.client.karaoke;

import net.auramc.glass.client.lyrics.Line;
import net.auramc.glass.client.lyrics.Lyrics;
import net.auramc.glass.client.lyrics.LyricsClient;
import net.auramc.glass.client.lyrics.Word;
import net.auramc.glass.client.music.MusicClock;
import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.NowPlaying;
import net.auramc.glass.client.music.PlaybackState;
import net.auramc.glass.client.music.Track;
import net.auramc.glass.client.quickmenu.music.MusicSourceProvider;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Objects;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Integration bridge connecting MusicClock (W5), LyricsClient (W3b), and KaraokeEngine (W6):
 * 1. Feeds MusicClock.positionMs into KaraokeEngine clock supplier.
 * 2. On track change (id): loads lyrics asynchronously via LyricsClient -> delivers immutable DTO ->
 *    applies strictly on client tick thread with generation & state validation -> feeds engine.
 *    Missing lyrics or synced=false -> karaoke stays silent with zero errors.
 * 3. On MusicClock epoch increase (seek / track change / resynchronization): resets shown and flying words.
 * 4. Playback state management:
 *    - PAUSED: new words do not appear, existing hanging words remain in place.
 *    - STOPPED: all words dissolve, engine remains stopped, lyrics cached for when playback starts.
 * 5. When bridge is unavailable (MusicSource.available() == false): zero threads / polling beyond W5,
 *    shares MusicSourceProvider.get() with F8 quick menu.
 */
public final class MusicKaraokeBridge implements AutoCloseable {

    private static final MusicKaraokeBridge INSTANCE = new MusicKaraokeBridge();

    public static MusicKaraokeBridge getInstance() {
        return INSTANCE;
    }

    private final MusicClock clock;
    private final LyricsClient lyricsClient;
    private final KaraokeEngine engine;

    private String lastTrackId = "";
    private long lastEpoch = -1L;
    private PlaybackState lastState = PlaybackState.STOPPED;
    private volatile boolean active = false;

    private long requestGeneration = 0L;
    private CompletableFuture<Optional<Lyrics>> currentLyricsFuture = null;

    /**
     * Cache of parsed KaraokeLyrics per trackId.
     * Preserves lyrics loaded during cold startup (e.g. while iframe is still STOPPED)
     * so that when PLAYING/PAUSED starts, karaoke activates without discarding or re-requesting.
     */
    private final Map<String, KaraokeModel.KaraokeLyrics> lyricsCache = new ConcurrentHashMap<>();

    /**
     * Immutable container for lyrics delivered from background HTTP/worker threads.
     * Never mutates KaraokeEngine on completion threads; consumed strictly on client tick thread.
     */
    private record PendingLyrics(long generation, String trackId, KaraokeModel.KaraokeLyrics lyrics) {}
    private final AtomicReference<PendingLyrics> pendingLyrics = new AtomicReference<>();

    public MusicKaraokeBridge() {
        this(new MusicClock(MusicSourceProvider::get, System::currentTimeMillis), new LyricsClient(), KaraokeEngine.getInstance());
    }

    public MusicKaraokeBridge(MusicClock clock, LyricsClient lyricsClient, KaraokeEngine engine) {
        this.clock = Objects.requireNonNull(clock, "clock");
        this.lyricsClient = Objects.requireNonNull(lyricsClient, "lyricsClient");
        this.engine = Objects.requireNonNull(engine, "engine");

        // Wire clock supplier to karaoke engine
        this.engine.setClockSupplier(this.clock::positionMs);
    }

    public MusicClock getMusicClock() {
        return clock;
    }

    public LyricsClient getLyricsClient() {
        return lyricsClient;
    }

    public KaraokeEngine getKaraokeEngine() {
        return engine;
    }

    public boolean isActive() {
        return active;
    }

    public String getLastTrackId() {
        return lastTrackId;
    }

    public PlaybackState getLastState() {
        return lastState;
    }

    public long getRequestGeneration() {
        return requestGeneration;
    }

    public void update() {
        update(System.currentTimeMillis());
    }

    /**
     * Periodic tick update without starting any background thread or network polling
     * beyond W5's BridgeMusicSource.
     */
    public synchronized void update(long nowTimeMs) {
        if (KaraokeConfig.get().isDemoMode()) return;
        MusicSource source = clock.source();
        if (source == null || !source.available()) {
            if (active) {
                // Source transitioned to unavailable: silence karaoke and reset state
                invalidatePendingRequests();
                lyricsCache.clear();
                engine.resetWords();
                engine.setLyrics(null);
                engine.setStopped(true);
                engine.setPaused(false);
                active = false;
                lastTrackId = "";
                lastState = PlaybackState.STOPPED;
                lastEpoch = -1L;
            }
            return;
        }

        active = true;
        NowPlaying np = source.now();
        if (np == null) {
            np = NowPlaying.stopped();
        }

        Track track = np.track();
        String currentTrackId = (track != null) ? track.id() : "";
        PlaybackState currentState = np.state();
        long currentEpoch = clock.epoch();

        // 1. Check for epoch change (seek / track change / resync)
        if (lastEpoch >= 0 && currentEpoch != lastEpoch) {
            // epoch increased -> reset shown and flying words
            long currentPos = clock.positionMs(nowTimeMs) + KaraokeConfig.get().getTimingOffsetMs();
            engine.seekTo(currentPos);
        }
        lastEpoch = currentEpoch;

        // 2. Check for track change
        if (!currentTrackId.equals(lastTrackId)) {
            lastTrackId = currentTrackId;
            invalidatePendingRequests();
            if (track == null || track.isEmpty()) {
                // Empty track: silence karaoke
                engine.resetWords();
                engine.setLyrics(null);
                engine.setStopped(true);
            } else {
                loadLyricsForTrack(track);
            }
        }

        // 3. Playback state transitions
        if (currentState != lastState) {
            handleStateTransition(lastState, currentState, track);
            lastState = currentState;
        }

        // 4. Drain and apply pending lyrics on client thread with strict generation & state checks
        drainPendingLyrics(currentState, source.available());
    }

    private void invalidatePendingRequests() {
        requestGeneration++;
        pendingLyrics.set(null);
        if (currentLyricsFuture != null && !currentLyricsFuture.isDone()) {
            currentLyricsFuture.cancel(true);
        }
    }

    private void loadLyricsForTrack(Track track) {
        // Reset words for the new track on client thread
        engine.resetWords();
        engine.setLyrics(null);
        engine.setStopped(true);

        final long generation = this.requestGeneration;
        final String trackId = track.id();
        double durationSec = track.durationMs() > 0 ? track.durationMs() / 1000.0 : 0.0;

        currentLyricsFuture = lyricsClient.getLyricsAsync(track.artist(), track.title(), durationSec)
                .thenApply(optLyrics -> {
                    // Running on HTTP completion thread: DO NOT MUTATE KaraokeEngine directly!
                    // Deliver immutable parsed lyrics DTO via atomic generation CAS publishing.
                    if (optLyrics.isPresent()) {
                        Lyrics lyrics = optLyrics.get();
                        if (lyrics.synced() && !lyrics.lines().isEmpty()) {
                            KaraokeModel.KaraokeLyrics kLyrics = convertToKaraokeModel(lyrics);
                            publishPendingLyrics(new PendingLyrics(generation, trackId, kLyrics));
                            return optLyrics;
                        }
                    }
                    // Missing or unsynced: deliver null lyrics sentinel
                    publishPendingLyrics(new PendingLyrics(generation, trackId, null));
                    return optLyrics;
                })
                .exceptionally(ex -> {
                    // On error: deliver null lyrics sentinel
                    publishPendingLyrics(new PendingLyrics(generation, trackId, null));
                    return Optional.empty();
                });
    }

    /**
     * Atomically publishes pending lyrics from background threads.
     * Prevents stale completions (older generation) from overwriting newer completions
     * even if they resolve out-of-order before the client thread drains the slot.
     */
    private void publishPendingLyrics(PendingLyrics newPending) {
        if (newPending == null) {
            return;
        }
        // Discard immediately if generation is already older than the current active generation
        if (newPending.generation < this.requestGeneration) {
            return;
        }
        pendingLyrics.accumulateAndGet(newPending, (current, incoming) -> {
            if (current == null) {
                return incoming;
            }
            // Newer generation always wins; older generation never overwrites newer
            if (incoming.generation >= current.generation) {
                return incoming;
            }
            return current;
        });
    }

    private void drainPendingLyrics(PlaybackState currentState, boolean isSourceAvailable) {
        PendingLyrics pending = pendingLyrics.getAndSet(null);
        if (pending == null) {
            return;
        }

        // Validate generation token: must match current active generation
        if (pending.generation != this.requestGeneration) {
            return;
        }

        // Validate track ID: must match current track
        if (!pending.trackId.equals(this.lastTrackId)) {
            return;
        }

        // Cache lyrics per track ID regardless of current playback state (cold start support)
        if (pending.lyrics != null && pending.lyrics.synced && !pending.lyrics.lines.isEmpty()) {
            lyricsCache.put(pending.trackId, pending.lyrics);
        } else {
            lyricsCache.remove(pending.trackId);
        }

        // Validate source availability and playback state:
        // If playback is STOPPED or source is offline, NEVER un-stop engine!
        if (!isSourceAvailable || currentState == PlaybackState.STOPPED) {
            if (pending.lyrics != null && pending.lyrics.synced) {
                engine.setLyrics(pending.lyrics);
            } else {
                engine.setLyrics(null);
            }
            engine.setStopped(true);
            return;
        }

        if (pending.lyrics != null && pending.lyrics.synced && !pending.lyrics.lines.isEmpty()) {
            engine.setLyrics(pending.lyrics);
            if (currentState == PlaybackState.PLAYING) {
                engine.setStopped(false);
                engine.setPaused(false);
            } else if (currentState == PlaybackState.PAUSED) {
                engine.setStopped(false);
                engine.setPaused(true);
            }
            long currentPos = clock.positionMs();
            engine.seekTo(currentPos);
        } else {
            // Missing lyrics or synced == false -> karaoke stays silent, zero errors
            engine.setLyrics(null);
            engine.setStopped(true);
        }
    }

    private void handleStateTransition(PlaybackState oldState, PlaybackState newState, Track currentTrack) {
        switch (newState) {
            case PLAYING -> {
                if (engine.getLyrics() == null) {
                    KaraokeModel.KaraokeLyrics lyrics = lyricsCache.get(this.lastTrackId);
                    if (lyrics != null && lyrics.synced && !lyrics.lines.isEmpty()) {
                        engine.setLyrics(lyrics);
                        long currentPos = clock.positionMs();
                        engine.seekTo(currentPos);
                    } else if (currentTrack != null && !currentTrack.isEmpty()
                            && (currentLyricsFuture == null || currentLyricsFuture.isDone())) {
                        loadLyricsForTrack(currentTrack);
                    }
                }
                if (engine.getLyrics() != null && engine.getLyrics().synced) {
                    engine.setStopped(false);
                } else {
                    engine.setStopped(true);
                }
                engine.setPaused(false);
            }
            case PAUSED -> {
                if (engine.getLyrics() == null) {
                    KaraokeModel.KaraokeLyrics lyrics = lyricsCache.get(this.lastTrackId);
                    if (lyrics != null && lyrics.synced && !lyrics.lines.isEmpty()) {
                        engine.setLyrics(lyrics);
                        long currentPos = clock.positionMs();
                        engine.seekTo(currentPos);
                    }
                }
                if (engine.getLyrics() != null && engine.getLyrics().synced) {
                    engine.setStopped(false);
                } else {
                    engine.setStopped(true);
                }
                engine.setPaused(true);
            }
            case STOPPED -> {
                // Playback stopped: all words dissolve and engine stops
                engine.setStopped(true);
                engine.setPaused(false);
                engine.dissolveAll();
            }
        }
    }

    @Override
    public synchronized void close() {
        invalidatePendingRequests();
        lyricsCache.clear();
        engine.resetWords();
        engine.setLyrics(null);
        engine.setStopped(true);
        engine.setPaused(false);
        active = false;
        lastTrackId = "";
        lastState = PlaybackState.STOPPED;
        lastEpoch = -1L;
        MusicSourceProvider.close();
    }

    /**
     * Converts W3b Lyrics representation to W6 KaraokeModel.KaraokeLyrics.
     * Preserves syllable estimations and word boundaries.
     */
    public static KaraokeModel.KaraokeLyrics convertToKaraokeModel(Lyrics lyrics) {
        if (lyrics == null || !lyrics.synced() || lyrics.lines().isEmpty()) {
            return new KaraokeModel.KaraokeLyrics("empty", false, Collections.emptyList());
        }

        List<KaraokeModel.KaraokeLine> kLines = new ArrayList<>();
        List<Line> srcLines = lyrics.lines();

        for (int i = 0; i < srcLines.size(); i++) {
            Line srcLine = srcLines.get(i);
            List<KaraokeModel.KaraokeWord> kWords = new ArrayList<>();

            if (!srcLine.words().isEmpty()) {
                for (Word w : srcLine.words()) {
                    kWords.add(new KaraokeModel.KaraokeWord(w.t(), w.d(), w.w()));
                }
            } else if (!srcLine.text().isBlank()) {
                long nextLineT = (i + 1 < srcLines.size()) ? srcLines.get(i + 1).t() : srcLine.t() + 4000L;
                kWords = KaraokeModel.estimateWordsForLine(srcLine.text(), srcLine.t(), nextLineT);
            }

            kLines.add(new KaraokeModel.KaraokeLine(srcLine.t(), srcLine.text(), kWords));
        }

        return new KaraokeModel.KaraokeLyrics(lyrics.source(), lyrics.synced(), kLines);
    }
}

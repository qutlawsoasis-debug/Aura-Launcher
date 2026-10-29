package net.auramc.glass.client.karaoke;

import net.auramc.glass.client.lyrics.Line;
import net.auramc.glass.client.lyrics.Lyrics;
import net.auramc.glass.client.lyrics.LyricsClient;
import net.auramc.glass.client.lyrics.Word;
import net.auramc.glass.client.music.BridgeMusicSource;
import net.auramc.glass.client.music.MusicClock;
import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.NowPlaying;
import net.auramc.glass.client.music.PlaybackState;
import net.auramc.glass.client.music.Track;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Objects;
import java.util.Optional;
import java.util.concurrent.CompletableFuture;

/**
 * Integration bridge connecting MusicClock (W5), LyricsClient (W3b), and KaraokeEngine (W6):
 * 1. Feeds MusicClock.positionMs into KaraokeEngine clock supplier.
 * 2. On track change (id): loads lyrics asynchronously via LyricsClient -> converts to W6 model -> feeds engine.
 *    Missing lyrics or synced=false -> karaoke stays silent with zero errors.
 * 3. On MusicClock epoch increase (seek / track change / resynchronization): resets shown and flying words.
 * 4. Playback state management:
 *    - PAUSED: new words do not appear, existing hanging words remain in place.
 *    - STOPPED: all words dissolve.
 * 5. When bridge is unavailable (MusicSource.available() == false): zero threads / polling beyond W5.
 */
public final class MusicKaraokeBridge {

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

    private CompletableFuture<Optional<Lyrics>> currentLyricsFuture = null;

    public MusicKaraokeBridge() {
        this(new MusicClock(new BridgeMusicSource()), new LyricsClient(), KaraokeEngine.getInstance());
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

    public void update() {
        update(System.currentTimeMillis());
    }

    /**
     * Periodic tick update without starting any background thread or network polling
     * beyond W5's BridgeMusicSource.
     */
    public synchronized void update(long nowTimeMs) {
        MusicSource source = clock.source();
        if (source == null || !source.available()) {
            if (active) {
                // Source transitioned to unavailable: silence karaoke and reset state
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
            long currentPos = clock.positionMs(nowTimeMs);
            engine.seekTo(currentPos);
        }
        lastEpoch = currentEpoch;

        // 2. Check for track change
        if (!currentTrackId.equals(lastTrackId)) {
            lastTrackId = currentTrackId;
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
            handleStateTransition(lastState, currentState);
            lastState = currentState;
        }
    }

    private void loadLyricsForTrack(Track track) {
        // Reset words for the new track
        engine.resetWords();
        engine.setLyrics(null);

        String trackId = track.id();
        double durationSec = track.durationMs() > 0 ? track.durationMs() / 1000.0 : 0.0;

        currentLyricsFuture = lyricsClient.getLyricsAsync(track.artist(), track.title(), durationSec)
                .thenApply(optLyrics -> {
                    synchronized (this) {
                        // Discard if track changed while fetching
                        if (!trackId.equals(this.lastTrackId)) {
                            return optLyrics;
                        }

                        if (optLyrics.isPresent()) {
                            Lyrics lyrics = optLyrics.get();
                            if (lyrics.synced() && !lyrics.lines().isEmpty()) {
                                KaraokeModel.KaraokeLyrics kLyrics = convertToKaraokeModel(lyrics);
                                engine.setLyrics(kLyrics);
                                engine.setStopped(false);
                                long currentPos = clock.positionMs();
                                engine.seekTo(currentPos);
                            } else {
                                // Missing lyrics or synced == false -> karaoke stays silent, zero errors
                                engine.setLyrics(null);
                                engine.setStopped(true);
                            }
                        } else {
                            // No lyrics found -> karaoke stays silent, zero errors
                            engine.setLyrics(null);
                            engine.setStopped(true);
                        }
                    }
                    return optLyrics;
                })
                .exceptionally(ex -> {
                    synchronized (this) {
                        if (trackId.equals(this.lastTrackId)) {
                            engine.setLyrics(null);
                            engine.setStopped(true);
                        }
                    }
                    return Optional.empty();
                });
    }

    private void handleStateTransition(PlaybackState oldState, PlaybackState newState) {
        switch (newState) {
            case PLAYING -> {
                engine.setStopped(false);
                engine.setPaused(false);
            }
            case PAUSED -> {
                engine.setStopped(false);
                engine.setPaused(true);
            }
            case STOPPED -> {
                engine.setStopped(true);
                engine.setPaused(false);
                engine.dissolveAll();
            }
        }
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

package net.auramc.glass.client.music;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Objects;
import java.util.function.LongSupplier;

public final class StubMusicSource implements MusicSource {
    private static final List<Track> DEFAULT_PLAYLIST = List.of(
            new Track("stub_1", "Aura Sound Lab", "Liquid Glass Odyssey", 180_000L, "aura://textures/music/odyssey.png"),
            new Track("stub_2", "Cupertino Dreams", "Spring Motion Beats", 210_000L, "aura://textures/music/spring.png"),
            new Track("stub_3", "Aura Orchestral", "Karaoke Horizon", 150_000L, "aura://textures/music/horizon.png")
    );

    private final List<Track> playlist;
    private final LongSupplier timeSupplier;

    private int trackIndex = 0;
    private PlaybackState state = PlaybackState.PLAYING;
    private long basePositionMs = 0L;
    private long lastStateChangeTimeMs;
    private float volume = 1.0f;
    private boolean available = true;

    public StubMusicSource() {
        this(DEFAULT_PLAYLIST, System::currentTimeMillis);
    }

    public StubMusicSource(List<Track> playlist) {
        this(playlist, System::currentTimeMillis);
    }

    public StubMusicSource(List<Track> playlist, LongSupplier timeSupplier) {
        this.playlist = (playlist != null && !playlist.isEmpty()) ? new ArrayList<>(playlist) : new ArrayList<>(DEFAULT_PLAYLIST);
        this.timeSupplier = Objects.requireNonNullElse(timeSupplier, System::currentTimeMillis);
        this.lastStateChangeTimeMs = this.timeSupplier.getAsLong();
    }

    public void setAvailable(boolean available) {
        this.available = available;
    }

    @Override
    public synchronized boolean available() {
        return available;
    }

    @Override
    public synchronized NowPlaying now() {
        if (!available || playlist.isEmpty()) {
            return NowPlaying.stopped();
        }

        Track currentTrack = playlist.get(trackIndex);
        long nowTime = timeSupplier.getAsLong();

        if (state == PlaybackState.PLAYING) {
            long elapsed = Math.max(0L, nowTime - lastStateChangeTimeMs);
            long totalPos = basePositionMs + elapsed;
            long duration = currentTrack.durationMs();

            if (duration > 0 && totalPos >= duration) {
                // Auto-advance to next track on QA timeline
                trackIndex = (trackIndex + 1) % playlist.size();
                currentTrack = playlist.get(trackIndex);
                basePositionMs = 0L;
                lastStateChangeTimeMs = nowTime;
                return new NowPlaying(PlaybackState.PLAYING, currentTrack, 0L, nowTime, volume);
            }

            return new NowPlaying(PlaybackState.PLAYING, currentTrack, totalPos, nowTime, volume);
        } else if (state == PlaybackState.PAUSED) {
            return new NowPlaying(PlaybackState.PAUSED, currentTrack, basePositionMs, lastStateChangeTimeMs, volume);
        } else {
            return new NowPlaying(PlaybackState.STOPPED, currentTrack, 0L, nowTime, volume);
        }
    }

    @Override
    public synchronized void play() {
        if (state != PlaybackState.PLAYING) {
            lastStateChangeTimeMs = timeSupplier.getAsLong();
            state = PlaybackState.PLAYING;
        }
    }

    @Override
    public synchronized void pause() {
        if (state == PlaybackState.PLAYING) {
            long nowTime = timeSupplier.getAsLong();
            basePositionMs += Math.max(0L, nowTime - lastStateChangeTimeMs);
            lastStateChangeTimeMs = nowTime;
            state = PlaybackState.PAUSED;
        }
    }

    @Override
    public synchronized void next() {
        if (playlist.isEmpty()) return;
        trackIndex = (trackIndex + 1) % playlist.size();
        basePositionMs = 0L;
        lastStateChangeTimeMs = timeSupplier.getAsLong();
    }

    @Override
    public synchronized void prev() {
        if (playlist.isEmpty()) return;
        trackIndex = (trackIndex - 1 + playlist.size()) % playlist.size();
        basePositionMs = 0L;
        lastStateChangeTimeMs = timeSupplier.getAsLong();
    }

    @Override
    public synchronized void seek(long ms) {
        if (playlist.isEmpty()) return;
        Track currentTrack = playlist.get(trackIndex);
        long max = (currentTrack.durationMs() > 0) ? currentTrack.durationMs() : Long.MAX_VALUE;
        basePositionMs = Math.max(0L, Math.min(ms, max));
        lastStateChangeTimeMs = timeSupplier.getAsLong();
    }

    @Override
    public synchronized void volume(float v) {
        volume = Math.max(0.0f, Math.min(1.0f, v));
    }

    @Override
    public synchronized List<Track> search(String q) {
        if (q == null || q.isBlank()) {
            return Collections.unmodifiableList(playlist);
        }
        String lower = q.trim().toLowerCase();
        List<Track> matches = new ArrayList<>();
        for (Track track : playlist) {
            if (track.title().toLowerCase().contains(lower) || track.artist().toLowerCase().contains(lower)) {
                matches.add(track);
            }
        }
        return Collections.unmodifiableList(matches);
    }
}

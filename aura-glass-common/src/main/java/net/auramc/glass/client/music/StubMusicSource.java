package net.auramc.glass.client.music;

import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

public class StubMusicSource implements MusicSource {
    private final List<Track> playlist = new ArrayList<>();
    private int currentIndex = 0;
    private String state = "paused";
    private long positionMs = 45000;
    private long updatedAtUnixMs = System.currentTimeMillis();
    private float volume = 0.5f;

    public StubMusicSource() {
        playlist.add(new Track("track-1", "Aura Sound", "Crystal Skies", 194000, ""));
        playlist.add(new Track("track-2", "Aura Chill", "Northern Glow", 142000, ""));
        playlist.add(new Track("track-3", "Liquid Glass", "Reflections", 215000, ""));
        playlist.add(new Track("track-4", "Oasis Ambient", "Deep Focus", 180000, ""));
        playlist.add(new Track("track-5", "Starlight", "Horizon", 240000, ""));
    }

    public List<Track> playlist() {
        return Collections.unmodifiableList(playlist);
    }

    @Override
    public synchronized NowPlaying now() {
        Track current = playlist.isEmpty() ? null : playlist.get(currentIndex);
        if ("playing".equalsIgnoreCase(state) && current != null && current.durationMs() > 0) {
            long elapsed = System.currentTimeMillis() - updatedAtUnixMs;
            if (positionMs + elapsed >= current.durationMs()) {
                currentIndex = (currentIndex + 1) % playlist.size();
                positionMs = 0;
                updatedAtUnixMs = System.currentTimeMillis();
                current = playlist.get(currentIndex);
            }
        }
        return new NowPlaying(state, current, positionMs, updatedAtUnixMs, volume);
    }

    @Override
    public synchronized void play() {
        state = "playing";
        updatedAtUnixMs = System.currentTimeMillis();
    }

    @Override
    public synchronized void pause() {
        if ("playing".equalsIgnoreCase(state)) {
            positionMs = now().currentPositionMs();
        }
        state = "paused";
        updatedAtUnixMs = System.currentTimeMillis();
    }

    @Override
    public synchronized void next() {
        if (!playlist.isEmpty()) {
            currentIndex = (currentIndex + 1) % playlist.size();
            positionMs = 0;
            updatedAtUnixMs = System.currentTimeMillis();
        }
    }

    @Override
    public synchronized void prev() {
        if (!playlist.isEmpty()) {
            if (now().currentPositionMs() > 3000) {
                positionMs = 0;
            } else {
                currentIndex = (currentIndex - 1 + playlist.size()) % playlist.size();
                positionMs = 0;
            }
            updatedAtUnixMs = System.currentTimeMillis();
        }
    }

    @Override
    public synchronized void seek(long ms) {
        Track current = playlist.isEmpty() ? null : playlist.get(currentIndex);
        long max = current != null ? current.durationMs() : ms;
        positionMs = Math.max(0, Math.min(max, ms));
        updatedAtUnixMs = System.currentTimeMillis();
    }

    @Override
    public synchronized void volume(float v) {
        volume = Math.max(0.0f, Math.min(1.0f, v));
    }

    @Override
    public synchronized List<Track> search(String q) {
        if (q == null || q.isBlank()) return Collections.unmodifiableList(playlist);
        String query = q.trim().toLowerCase(Locale.ROOT);
        List<Track> found = new ArrayList<>();
        for (Track t : playlist) {
            if (t.title().toLowerCase(Locale.ROOT).contains(query) ||
                    t.artist().toLowerCase(Locale.ROOT).contains(query)) {
                found.add(t);
            }
        }
        return found;
    }

    @Override
    public boolean available() {
        return true;
    }

    @Override
    public synchronized void queue(String trackId) {
        for (int i = 0; i < playlist.size(); i++) {
            if (playlist.get(i).id().equals(trackId)) {
                currentIndex = i;
                positionMs = 0;
                updatedAtUnixMs = System.currentTimeMillis();
                return;
            }
        }
    }

    @Override
    public synchronized void playTrack(Track track) {
        if (track == null) return;
        int idx = playlist.indexOf(track);
        if (idx >= 0) {
            currentIndex = idx;
        } else {
            playlist.add(track);
            currentIndex = playlist.size() - 1;
        }
        positionMs = 0;
        state = "playing";
        updatedAtUnixMs = System.currentTimeMillis();
    }
}

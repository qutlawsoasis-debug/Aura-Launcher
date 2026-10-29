package net.auramc.glass.client.music;

import java.util.List;

public interface MusicSource {
    NowPlaying now();
    void play();
    void pause();
    void next();
    void prev();
    void seek(long ms);
    void volume(float v);
    List<Track> search(String q);
    boolean available();

    default void queue(String trackId) {}
    default void playTrack(Track track) {
        if (track != null) {
            queue(track.id());
            play();
        }
    }
}

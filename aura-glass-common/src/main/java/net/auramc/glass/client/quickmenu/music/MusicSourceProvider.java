package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.StubMusicSource;

public final class MusicSourceProvider {
    private static volatile MusicSource source = new StubMusicSource();

    private MusicSourceProvider() {}

    public static MusicSource get() {
        return source;
    }

    public static void set(MusicSource s) {
        if (s != null) {
            source = s;
        }
    }
}

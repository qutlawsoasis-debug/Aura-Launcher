package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.BridgeMusicSource;
import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.StubMusicSource;

public final class MusicSourceProvider {
    private static volatile MusicSource source;

    static {
        if (Boolean.getBoolean("aura.qa.stubMusic") || "true".equalsIgnoreCase(System.getProperty("aura.qa.stubMusic"))) {
            source = new StubMusicSource();
        } else {
            try {
                source = new BridgeMusicSource();
            } catch (Throwable t) {
                source = new StubMusicSource();
            }
        }
    }

    private MusicSourceProvider() {}

    public static MusicSource get() {
        return source;
    }

    public static void set(MusicSource s) {
        if (s != null) {
            source = s;
        }
    }

    public static void close() {
        if (source instanceof AutoCloseable closeable) {
            try {
                closeable.close();
            } catch (Exception ignored) {}
        }
    }
}

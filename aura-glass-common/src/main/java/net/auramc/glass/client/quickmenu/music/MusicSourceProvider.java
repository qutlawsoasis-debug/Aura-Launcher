package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.BridgeMusicSource;
import net.auramc.glass.client.music.MusicSource;
import net.auramc.glass.client.music.NowPlaying;
import net.auramc.glass.client.music.StubMusicSource;
import net.auramc.glass.client.music.Track;

import java.util.Collections;
import java.util.List;

public final class MusicSourceProvider {
    private static volatile MusicSource source;

    static {
        initDefault();
    }

    public static void initDefault() {
        if (isQaStubRequested()) {
            source = new StubMusicSource();
        } else {
            try {
                source = new BridgeMusicSource();
            } catch (Throwable t) {
                source = UnavailableMusicSource.INSTANCE;
            }
        }
    }

    public static boolean isQaStubRequested() {
        return Boolean.getBoolean("aura.qa.stubMusic")
                || "true".equalsIgnoreCase(System.getProperty("aura.qa.stubMusic"));
    }

    private MusicSourceProvider() {}

    public static MusicSource get() {
        MusicSource s = source;
        return s != null ? s : UnavailableMusicSource.INSTANCE;
    }

    public static void set(MusicSource s) {
        if (s != null) {
            source = s;
        }
    }

    public static void close() {
        MusicSource s = source;
        if (s instanceof AutoCloseable closeable) {
            try {
                closeable.close();
            } catch (Exception ignored) {}
        }
    }

    public static final class UnavailableMusicSource implements MusicSource, AutoCloseable {
        public static final UnavailableMusicSource INSTANCE = new UnavailableMusicSource();

        @Override
        public NowPlaying now() {
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
            return Collections.emptyList();
        }

        @Override
        public boolean available() {
            return false;
        }

        @Override
        public void close() {}
    }
}

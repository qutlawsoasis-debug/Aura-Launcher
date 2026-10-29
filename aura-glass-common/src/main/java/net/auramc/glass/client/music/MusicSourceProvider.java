package net.auramc.glass.client.music;

public final class MusicSourceProvider {
    private static volatile MusicSource instance = new StubMusicSource();

    private MusicSourceProvider() {}

    public static MusicSource get() {
        return instance;
    }

    public static void set(MusicSource source) {
        instance = source != null ? source : new StubMusicSource();
    }
}

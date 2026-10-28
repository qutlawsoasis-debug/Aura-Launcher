package net.auramc.glass.common.hud;

public interface HudMusicSource {
    record Track(String title,String artist,boolean playing){}
    HudMusicSource NONE=()->null;
    Track currentTrack();
    default void previous(){}
    default void togglePlayback(){}
    default void next(){}
}

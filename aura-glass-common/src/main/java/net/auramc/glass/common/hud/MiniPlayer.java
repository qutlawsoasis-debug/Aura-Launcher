package net.auramc.glass.common.hud;

public final class MiniPlayer {
    private HudMusicSource source=HudMusicSource.NONE;
    private HudMusicSource.Track track;
    public void source(HudMusicSource source){this.source=source==null?HudMusicSource.NONE:source;track=null;}
    public void update(){track=source.currentTrack();}
    public boolean available(){return track!=null && track.title()!=null && !track.title().isBlank();}
    public HudMusicSource.Track track(){return track;}
    public void previous(){if(available())source.previous();}
    public void togglePlayback(){if(available())source.togglePlayback();}
    public void next(){if(available())source.next();}
}

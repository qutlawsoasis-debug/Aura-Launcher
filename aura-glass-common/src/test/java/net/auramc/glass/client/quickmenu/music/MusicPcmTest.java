package net.auramc.glass.client.quickmenu.music;

public final class MusicPcmTest {
    public static void main(String[] args) {
        byte[] signal = {0, 0, -1, 127, 0, -128, -24, 3, 24, -4};
        byte[] quiet = signal.clone();MusicRuntime.scalePcm16(quiet,quiet.length,0);
        for(byte sample:quiet)check(sample==0,"Mute must write digital silence");
        byte[] unity=signal.clone();MusicRuntime.scalePcm16(unity,unity.length,1);
        check(java.util.Arrays.equals(signal,unity),"Unity gain must preserve signed PCM");
        byte[] half=signal.clone();MusicRuntime.scalePcm16(half,half.length,.5);
        check(value(half,2)==16384&&value(half,4)==-16384,"Volume must scale signed extrema");
        check(value(half,6)==500&&value(half,8)==-500,"Volume must scale both channels/signs");
        check(!new MusicTrack("abc","x","y","",0,"",false).playable(),"YouTube metadata without audio URL must not claim playback");
        check(new MusicTrack("local","x","y","",0,"file:///fixture.wav",true).playable(),"Local source can be decoded");
        check(!new MusicTrack("yt","x","y","",0,"https://www.youtube.com/watch?v=abc",false).playable(),"Watch page is not audio");
        System.out.println("Music PCM gain and truthful source capability PASS");
    }
    private static int value(byte[] bytes,int offset){return (short)((bytes[offset]&255)|(bytes[offset+1]<<8));}
    private static void check(boolean value,String message){if(!value)throw new AssertionError(message);}
}
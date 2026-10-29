package net.auramc.glass.client.quickmenu;

import net.minecraft.client.sound.OggAudioStream;
import java.io.IOException;
import java.nio.ByteBuffer;

public final class QuickMenuAudioPlatform {
    private QuickMenuAudioPlatform() {}
    public static ByteBuffer read(OggAudioStream stream, int bytes) throws IOException {
        return stream.getBuffer(bytes);
    }
}
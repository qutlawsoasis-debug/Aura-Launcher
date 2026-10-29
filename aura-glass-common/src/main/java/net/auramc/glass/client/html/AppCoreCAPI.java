package net.auramc.glass.client.html;

import com.sun.jna.Library;
import com.sun.jna.Pointer;

public interface AppCoreCAPI extends Library {
    void ulEnablePlatformFontLoader();
    void ulEnablePlatformFileSystem(Pointer baseDir);
    void ulEnableDefaultLogger(Pointer logPath);
}

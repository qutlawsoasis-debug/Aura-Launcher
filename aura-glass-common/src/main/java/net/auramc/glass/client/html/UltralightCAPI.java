package net.auramc.glass.client.html;

import com.sun.jna.Callback;
import com.sun.jna.Library;
import com.sun.jna.Pointer;

public interface UltralightCAPI extends Library {
    Pointer ulCreateString(String str);
    Pointer ulCreateStringUTF8(byte[] str, long len);
    void ulDestroyString(Pointer str);
    String ulStringGetData(Pointer str);
    long ulStringGetLength(Pointer str);

    Pointer ulCreateConfig();
    void ulDestroyConfig(Pointer config);
    void ulConfigSetResourcePathPrefix(Pointer config, Pointer prefix);
    void ulConfigSetCachePath(Pointer config, Pointer cachePath);

    Pointer ulCreateRenderer(Pointer config);
    void ulDestroyRenderer(Pointer renderer);
    void ulUpdate(Pointer renderer);
    void ulRender(Pointer renderer);
    void ulPurgeMemory(Pointer renderer);
    void ulLogMemoryUsage(Pointer renderer);

    Pointer ulCreateViewConfig();
    void ulDestroyViewConfig(Pointer config);
    void ulViewConfigSetIsAccelerated(Pointer config, boolean isAccelerated);
    void ulViewConfigSetIsTransparent(Pointer config, boolean isTransparent);
    void ulViewConfigSetInitialDeviceScale(Pointer config, double initialDeviceScale);
    void ulViewConfigSetInitialFocus(Pointer config, boolean isFocused);

    Pointer ulCreateView(Pointer renderer, int width, int height, Pointer viewConfig, Pointer session);
    void ulDestroyView(Pointer view);
    void ulViewLoadURL(Pointer view, Pointer url);
    void ulViewLoadHTML(Pointer view, Pointer html);
    void ulViewResize(Pointer view, int width, int height);
    Pointer ulViewGetSurface(Pointer view);
    boolean ulViewGetNeedsPaint(Pointer view);
    Pointer ulViewEvaluateScript(Pointer view, Pointer jsString, Pointer exceptionOut);

    Pointer ulCreateMouseEvent(int type, int x, int y, int button);
    void ulDestroyMouseEvent(Pointer evt);
    void ulViewFireMouseEvent(Pointer view, Pointer evt);

    Pointer ulCreateKeyEvent(int type, int modifiers, int virtualKeyCode, int nativeKeyCode,
                             Pointer text, Pointer unmodifiedText, boolean isKeypad,
                             boolean isAutoRepeat, boolean isSystemKey);
    void ulDestroyKeyEvent(Pointer evt);
    void ulViewFireKeyEvent(Pointer view, Pointer evt);

    Pointer ulCreateScrollEvent(int type, int deltaX, int deltaY);
    void ulDestroyScrollEvent(Pointer evt);
    void ulViewFireScrollEvent(Pointer view, Pointer evt);

    Pointer ulBitmapSurfaceGetBitmap(Pointer surface);
    int ulBitmapGetWidth(Pointer bitmap);
    int ulBitmapGetHeight(Pointer bitmap);
    int ulBitmapGetRowBytes(Pointer bitmap);
    long ulBitmapGetSize(Pointer bitmap);
    Pointer ulBitmapLockPixels(Pointer bitmap);
    void ulBitmapUnlockPixels(Pointer bitmap);
    boolean ulBitmapWritePNG(Pointer bitmap, String path);

    interface ConsoleCallback extends Callback {
        void invoke(Pointer userData, Pointer caller, int source, int level, Pointer message,
                    int lineNumber, int columnNumber, Pointer sourceId);
    }
    void ulViewSetAddConsoleMessageCallback(Pointer view, ConsoleCallback callback, Pointer userData);
}

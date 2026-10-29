package net.auramc.glass.common.render;

import net.auramc.glass.common.style.GlassStyles;
import net.minecraft.client.MinecraftClient;

public final class MenuResources {
    private static boolean started;
    private static boolean stopping;
    private static int width,height;
    private static boolean regular;
    private static net.auramc.glass.common.style.GlassQuality quality;
    private MenuResources() {}
    public static boolean stopping(){return stopping;}
    public static void stop(){stopping=true;started=false;}

    public static void start(MinecraftClient client) {
        started=true;
        AuraRenderer.initialize();
        GlassDiagnostics.timers().prepare();
        prepare(client);
    }

    public static void prepare(MinecraftClient client) {
        if(!started || client.getFramebuffer()==null)return;
        var framebuffer=client.getFramebuffer();
        var nextQuality=GlassStyles.quality();
        if(width==framebuffer.textureWidth && height==framebuffer.textureHeight && quality==nextQuality && regular==GlassStyles.regular())return;
        width=framebuffer.textureWidth;height=framebuffer.textureHeight;quality=nextQuality;regular=GlassStyles.regular();
        try {
            GlassRenderer.prepare();
            BackdropRenderer.prepare();
            AdaptiveText.prepare();
            if(GlassStyles.quality().needsBackdrop())BlurService.get().prepare(framebuffer.fbo,framebuffer.textureWidth,framebuffer.textureHeight);
        }finally{GuiRenderState.restore(framebuffer.fbo,framebuffer.textureWidth,framebuffer.textureHeight);}
    }
}

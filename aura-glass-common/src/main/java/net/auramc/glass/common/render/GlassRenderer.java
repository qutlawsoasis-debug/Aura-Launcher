package net.auramc.glass.common.render;

import net.auramc.glass.common.style.GlassQuality;
import net.auramc.glass.common.style.GlassStyle;
import net.auramc.glass.common.style.GlassStyles;
import org.joml.Matrix4f;
import org.lwjgl.BufferUtils;
import java.nio.ByteBuffer;
import java.nio.FloatBuffer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import static org.lwjgl.opengl.GL33C.*;

public final class GlassRenderer {
    private static final int FLOATS = 32;
    private static final int MAX_PANES=4096;
    private static final List<Pane> PANES = new ArrayList<>(MAX_PANES);
    private static final Pane[] PANE_POOL=new Pane[MAX_PANES];
    static{for(int i=0;i<PANE_POOL.length;i++)PANE_POOL[i]=new Pane();}
    private static final int MAX_FOREGROUND=8192;
    private static final Runnable[] FOREGROUND=new Runnable[MAX_FOREGROUND],PENDING_FOREGROUND=new Runnable[MAX_FOREGROUND];
    private static int foregroundCount;
    private static final float[][] CLIPS=new float[8192][4];
    private static final ClippedCommand[] CLIPPED=new ClippedCommand[MAX_FOREGROUND];
    private static int clipCursor,clippedCursor;
    static{for(int i=0;i<CLIPPED.length;i++)CLIPPED[i]=new ClippedCommand();}
    private static final FloatBuffer MATRIX = BufferUtils.createFloatBuffer(16);
    private static final Matrix4f PROJECTION = new Matrix4f();
    private static final FloatBuffer instances = BufferUtils.createFloatBuffer(MAX_PANES * FLOATS * 6);
    private static int program, vao, vbo, noiseTexture, projectionLocation, qualityLocation, sizeLocation, instancedLocation, modalLocation;
    private static int framebuffer, width, height, drawCalls;
    private static float scale, modalProgress;
    private static boolean captured, failed, instanced,lowRenderer;
    private static String fallbackReason = "none";
    private static GlassQuality quality = GlassQuality.HIGH;
    private static float[] clip;

    private static final class Pane {
        float x,y,w,h,alpha;
        Object widget;
        final GlassStyle style=new GlassStyle();
    }
    private GlassRenderer() {}

    public static void prepare() {
        if(program!=0 || failed)return;
        try{initialize(false);lowRenderer=false;}
        catch(Exception highError){
            GlassDiagnostics.log("High glass renderer unavailable: "+highError.getMessage());
            releaseGpu();
            try{initialize(true);lowRenderer=true;}
            catch(Exception lowError){failed=true;GlassDiagnostics.rendererFailure("Low shader failed: "+lowError.getMessage());}
        }
    }

    public static void beginFrame(int fbo, int w, int h, float guiScale) {
        PANES.clear(); java.util.Arrays.fill(FOREGROUND,0,foregroundCount,null);foregroundCount=0; captured = false; modalProgress=0; drawCalls = 0;
        GlassStyles.beginFrame();
        clipCursor=clippedCursor=0;clip=null;
        framebuffer = fbo; width = w; height = h; scale = guiScale;
        quality = GlassStyles.quality();
        if(lowRenderer && quality.needsBackdrop())quality=GlassQuality.LOW;
        fallbackReason = lowRenderer && GlassStyles.quality().needsBackdrop() ? "High shader unavailable; Low fill renderer" : quality == GlassQuality.OFF ? "effects Off (configured)" : quality == GlassQuality.LOW ? "quality Low (configured)" : "none";
    }

    public static void captureBackdrop() { captureBackdrop(false); }
    public static void captureBackdrop(boolean modal) {
        if (captured || !quality.needsBackdrop()) return;
        captured = true;
        if (!BlurService.get().capture(framebuffer, width, height)) {
            quality = GlassQuality.LOW; fallbackReason = BlurService.get().failureReason();
        }
    }

    // Reserved for world overlays. No capture or HUD rendering is requested yet.
    public static void worldBeforeHud() { BackdropRenderer.captureWorldIfRequested(); }
    public static void modalBackground(float progress) { modalProgress=progress; BackdropRenderer.modal(progress,hasBackdrop()); }

    public static void draw(float x, float y, float w, float h, GlassStyle style, float alpha) {
        enqueue(null, x, y, w, h, style, alpha);
    }

    public static void drawWidget(Object widget, float x, float y, float w, float h, GlassStyle style, float alpha) {
        enqueue(widget, x, y, w, h, style, alpha);
    }

    private static void enqueue(Object widget, float x, float y, float w, float h, GlassStyle style, float alpha) {
        if(clip!=null){float right=Math.min(x+w,clip[2]),bottom=Math.min(y+h,clip[3]);x=Math.max(x,clip[0]);y=Math.max(y,clip[1]);w=right-x;h=bottom-y;}
        if (w <= 0 || h <= 0 || alpha <= 0) return;
        if(PANES.size()==MAX_PANES){GlassDiagnostics.log("Menu pane capacity exceeded");return;}
        Pane pane=PANE_POOL[PANES.size()];
        pane.x=x;pane.y=y;pane.w=w;pane.h=h;pane.alpha=Math.min(1,alpha);pane.widget=widget;
        GlassStyles.materialInto(style,quality,pane.style);
        PANES.add(pane);
    }

    public static float[] clipBounds(){return clip;}
    public static void withClip(float l,float t,float r,float b,Runnable draw){
        float[] saved=pushClip(l,t,r,b);
        try{draw.run();}finally{popClip(saved);}
    }
    public static float[] pushClip(float l,float t,float r,float b){
        if(clipCursor==CLIPS.length){GlassDiagnostics.log("Menu clip capacity exceeded");return clip;}
        float[] saved=clip;clip=CLIPS[clipCursor++];
        clip[0]=saved==null?l:Math.max(l,saved[0]);clip[1]=saved==null?t:Math.max(t,saved[1]);
        clip[2]=saved==null?r:Math.min(r,saved[2]);clip[3]=saved==null?b:Math.min(b,saved[3]);
        return saved;
    }
    public static void popClip(float[] saved){clip=saved;}
    public static void foreground(Runnable drawing) {
        float[] bounds=clip;
        if(foregroundCount==MAX_FOREGROUND){GlassDiagnostics.log("Menu foreground capacity exceeded");return;}
        if(bounds==null){FOREGROUND[foregroundCount++]=drawing;return;}
        if(clippedCursor==CLIPPED.length){GlassDiagnostics.log("Menu clipped command capacity exceeded");return;}
        ClippedCommand command=CLIPPED[clippedCursor++];command.drawing=drawing;
        command.l=bounds[0];command.t=bounds[1];command.r=bounds[2];command.b=bounds[3];
        FOREGROUND[foregroundCount++]=command;
    }
    private static final class ClippedCommand implements Runnable {
        float l,t,r,b;Runnable drawing;
        @Override public void run(){try{withClip(l,t,r,b,drawing);}finally{drawing=null;}}
    }
    public static boolean hasBackdrop() { return captured && BlurService.get().captured(); }
    public static GlassQuality effectiveQuality() { return quality; }
    public static int drawCalls() { return drawCalls; }
    public static String fallbackReason() { return fallbackReason; }

    public static void flush() {
        // List entries may enqueue their own vanilla widgets after the container batch.
        for(int pass=0;pass<4 && (!PANES.isEmpty() || foregroundCount>0);pass++)flushBatch();
    }

    private static void flushBatch() {
        if (PANES.isEmpty()) { foreground(); return; }
        boolean timer = false;
        try {
            if (!captured && quality.needsBackdrop()) {
                quality = GlassQuality.LOW;
                fallbackReason = "backdrop hook not reached; automatic Low";
                GlassDiagnostics.log(fallbackReason);
            }
            if (failed || program==0) return;
            int copies = instanced ? 1 : 6;
            int required = PANES.size() * FLOATS * copies;
            instances.clear();
            for(int i=0;i<PANES.size();i++)for(int copy=0;copy<copies;copy++)pack(PANES.get(i));
            instances.flip();
            timer = GlassDiagnostics.timers().begin(GpuGlassTimers.GLASS);
            glUseProgram(program); glBindVertexArray(vao); glBindBuffer(GL_ARRAY_BUFFER, vbo);
            glBufferData(GL_ARRAY_BUFFER, instances, GL_STREAM_DRAW);
            glUniformMatrix4fv(projectionLocation, false, PROJECTION.setOrtho(0, (float) Math.ceil(width / (double) scale), (float) Math.ceil(height / (double) scale), 0, -1, 1).get(MATRIX));
            glUniform1i(qualityLocation, quality.shaderLevel());
            glUniform1i(instancedLocation, instanced ? 1 : 0);
            glUniform2f(sizeLocation, width, height);
            glUniform1f(modalLocation,modalProgress);
            for (int unit = 0; unit < 4; unit++) {
                com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE0 + unit); GuiRenderState.clearSampler(unit);
                com.mojang.blaze3d.platform.GlStateManager._bindTexture(hasBackdrop() ? BlurService.get().texture(unit) : 0);
            }
            com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE4); GuiRenderState.clearSampler(4); com.mojang.blaze3d.platform.GlStateManager._bindTexture(noiseTexture);
            glDisable(GL_DEPTH_TEST); glDisable(GL_CULL_FACE); glDisable(GL_SCISSOR_TEST);
            glEnable(GL_BLEND); glBlendEquation(GL_FUNC_ADD); glBlendFuncSeparate(GL_ONE, GL_ONE_MINUS_SRC_ALPHA, GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
            if (instanced) glDrawArraysInstanced(GL_TRIANGLE_STRIP, 0, 4, PANES.size());
            else glDrawArrays(GL_TRIANGLES, 0, PANES.size() * 6);
            AdaptiveText.update(hasBackdrop() ? BlurService.get().texture(3) : 0, width, height, scale, 1-.25f*modalProgress, hasBackdrop());
            int error = glGetError();
            if (error != GL_NO_ERROR) throw new IllegalStateException("glass draw GL error 0x" + Integer.toHexString(error));
            drawCalls++;
            GlassDiagnostics.drawSucceeded();
            for (int i = 0; i < PANES.size(); i++) {
                if (PANES.get(i).widget != null) GlassDiagnostics.widgetDrawn(PANES.get(i).widget);
                if (quality == GlassQuality.OFF) GlassDiagnostics.flatDrawn();
                else if (quality == GlassQuality.LOW) GlassDiagnostics.fallback(fallbackReason);
                else GlassDiagnostics.glassDrawn();
            }
        } catch (Exception error) {
            quality = GlassQuality.LOW;
            BlurService.get().fail(error.getMessage());
            fallbackReason=error.getMessage();
            for (int i = 0; i < PANES.size(); i++) GlassDiagnostics.fallback(error.getMessage());
        } finally {
            GlassDiagnostics.timers().end(timer);
            GuiRenderState.restore(framebuffer, width, height);
            for (Pane pane : PANES) pane.widget = null;
            PANES.clear(); foreground();
        }
    }

    private static void foreground() {
        int count=foregroundCount;
        System.arraycopy(FOREGROUND,0,PENDING_FOREGROUND,0,count);
        java.util.Arrays.fill(FOREGROUND,0,count,null);foregroundCount=0;
        for(int i=0;i<count;i++){Runnable draw=PENDING_FOREGROUND[i];PENDING_FOREGROUND[i]=null;draw.run();}
    }

    private static void pack(Pane p) {
        GlassStyle s = p.style;
        if (quality == GlassQuality.LOW) s.tintA = Math.max(s.tintA, .12f);
        float w = p.w * s.scale, h = p.h * s.scale;
        instances.put(p.x + (p.w - w) * .5f).put(p.y + (p.h - h) * .5f).put(w).put(h);
        instances.put(s.refraction).put(Math.min(s.bevel < 0 ? h / 2 : s.bevel, h * .35f)).put(s.splay).put(s.depth);
        instances.put(s.dispersion).put(s.frost).put(s.light).put(s.gloss);
        instances.put(s.lightDirX).put(s.lightDirY).put(s.rimOuter).put(s.rimInner);
        instances.put(s.tintR).put(s.tintG).put(s.tintB).put(s.tintA);
        instances.put(s.saturation).put(s.noise).put(s.radius < 0 ? h / 2 : s.radius).put(s.squircleK);
        instances.put(s.wobble).put(s.gradient).put(s.innerGlow).put(scale);
        instances.put(p.alpha).put(s.profile).put(s.cornerSoften).put(s.specFalloff);
    }

    private static int shader(int type, String file) throws Exception {
        String path = "/assets/aura-glass/shaders/" + file;
        String source;
        try (var stream = GlassRenderer.class.getResourceAsStream(path)) {
            if (stream == null) throw new IllegalStateException("missing " + path);
            source = new String(stream.readAllBytes(), StandardCharsets.UTF_8);
        }
        int shader = glCreateShader(type);
        glShaderSource(shader, source); glCompileShader(shader);
        if (glGetShaderi(shader, GL_COMPILE_STATUS) == GL_FALSE) {
            String log = glGetShaderInfoLog(shader); glDeleteShader(shader);
            throw new IllegalStateException(file + " shader compile: " + log);
        }
        return shader;
    }

    private static void initialize(boolean low) throws Exception {
        int vs = shader(GL_VERTEX_SHADER, "glass.vert"), fs = 0;
        try {
            fs = shader(GL_FRAGMENT_SHADER, low ? "glass-low.frag" : "glass.frag"); program = glCreateProgram();
            glAttachShader(program, vs); glAttachShader(program, fs);
            String[] names = {"Rect", "Lens", "Optical", "Lighting", "Tint", "Finish", "Extra", "Opacity"};
            for (int i = 0; i < names.length; i++) glBindAttribLocation(program, i, names[i]);
            glLinkProgram(program);
            if (glGetProgrami(program, GL_LINK_STATUS) == GL_FALSE) throw new IllegalStateException("glass shader link: " + glGetProgramInfoLog(program));
            glUseProgram(program);
            for (int i = 0; i < 4; i++) glUniform1i(glGetUniformLocation(program, "Backdrop" + i), i);
            glUniform1i(glGetUniformLocation(program, "Noise"), 4);
            projectionLocation = glGetUniformLocation(program, "Projection");
            qualityLocation = glGetUniformLocation(program, "Quality");
            modalLocation = glGetUniformLocation(program,"Modal");
            instancedLocation = glGetUniformLocation(program, "Instanced"); sizeLocation = glGetUniformLocation(program, "FramebufferSize");
            vao = glGenVertexArrays(); vbo = glGenBuffers(); glBindVertexArray(vao); glBindBuffer(GL_ARRAY_BUFFER, vbo);
            var capabilities = org.lwjgl.opengl.GL.getCapabilities();
            instanced = capabilities.OpenGL33 || capabilities.GL_ARB_instanced_arrays;
            for (int i = 0; i < 8; i++) {
                glEnableVertexAttribArray(i); glVertexAttribPointer(i, 4, GL_FLOAT, false, FLOATS * 4, i * 16L); if (org.lwjgl.opengl.GL.getCapabilities().OpenGL33) glVertexAttribDivisor(i, 1);
                else if (org.lwjgl.opengl.GL.getCapabilities().GL_ARB_instanced_arrays) org.lwjgl.opengl.ARBInstancedArrays.glVertexAttribDivisorARB(i, 1);

            }
            ByteBuffer pixels = BufferUtils.createByteBuffer(64 * 64 * 2);
            Random random = new Random(0x41555241);
            while (pixels.hasRemaining()) pixels.put((byte) random.nextInt(256));
            pixels.flip(); noiseTexture = glGenTextures(); com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE4); com.mojang.blaze3d.platform.GlStateManager._bindTexture(noiseTexture);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
            glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
            glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
            glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
            glPixelStorei(GL_UNPACK_SKIP_ROWS, 0);
            glPixelStorei(GL_UNPACK_SKIP_PIXELS, 0);
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RG8, 64, 64, 0, GL_RG, GL_UNSIGNED_BYTE, pixels);
        } finally { glDeleteShader(vs); if (fs != 0) glDeleteShader(fs); }
    }

    private static void releaseGpu() {
        if (program != 0) glDeleteProgram(program);
        if (vao != 0) glDeleteVertexArrays(vao);
        if (vbo != 0) glDeleteBuffers(vbo);
        if (noiseTexture != 0) glDeleteTextures(noiseTexture);
        program = vao = vbo = noiseTexture = 0; failed = false;
    }

    public static void close() { BackdropRenderer.close(); AdaptiveText.close(); releaseGpu(); PANES.clear();java.util.Arrays.fill(FOREGROUND,null);foregroundCount=0; }
}

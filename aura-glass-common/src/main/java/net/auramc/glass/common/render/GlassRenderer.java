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
    private static final List<Pane> PANES = new ArrayList<>();
    private static final List<Runnable> FOREGROUND = new ArrayList<>();
    private static final FloatBuffer MATRIX = BufferUtils.createFloatBuffer(16);
    private static FloatBuffer instances = BufferUtils.createFloatBuffer(128 * FLOATS);
    private static int program, vao, vbo, noiseTexture, projectionLocation, qualityLocation, sizeLocation, instancedLocation, modalLocation;
    private static int framebuffer, width, height, drawCalls;
    private static float scale, modalProgress;
    private static boolean captured, failed, instanced;
    private static String fallbackReason = "none";
    private static GlassQuality quality = GlassQuality.HIGH;
    private static float[] clip;

    private record Pane(float x, float y, float w, float h, GlassStyle style, float alpha) {}
    private GlassRenderer() {}

    public static void beginFrame(int fbo, int w, int h, float guiScale) {
        PANES.clear(); FOREGROUND.clear(); captured = false; modalProgress=0; drawCalls = 0;
        framebuffer = fbo; width = w; height = h; scale = guiScale;
        GlassStyles.poll(); quality = GlassStyles.quality();
        fallbackReason = quality == GlassQuality.LOW ? "quality Low (configured)" : "none";
    }

    public static void captureBackdrop() { captureBackdrop(false); }
    public static void captureBackdrop(boolean modal) {
        if (captured) return;
        captured = true;
        if ((modal || quality != GlassQuality.LOW) && !BlurService.get().capture(framebuffer, width, height)) {
            quality = GlassQuality.LOW; fallbackReason = BlurService.get().failureReason();
        }
    }

    // Reserved for world overlays. No capture or HUD rendering is requested yet.
    public static void worldBeforeHud() { BackdropRenderer.captureWorldIfRequested(); }
    public static void modalBackground(float progress) { modalProgress=progress; BackdropRenderer.modal(progress,hasBackdrop()); }

    public static void draw(float x, float y, float w, float h, GlassStyle style, float alpha) {
        if(clip!=null){float right=Math.min(x+w,clip[2]),bottom=Math.min(y+h,clip[3]);x=Math.max(x,clip[0]);y=Math.max(y,clip[1]);w=right-x;h=bottom-y;}
        if (w <= 0 || h <= 0 || alpha <= 0) return;
        PANES.add(new Pane(x, y, w, h, GlassStyles.material(style), Math.min(1, alpha)));
    }

    public static float[] clipBounds(){return clip;}
    public static void withClip(float l,float t,float r,float b,Runnable draw){
        float[] saved=clip;clip=saved==null?new float[]{l,t,r,b}:new float[]{Math.max(l,saved[0]),Math.max(t,saved[1]),Math.min(r,saved[2]),Math.min(b,saved[3])};
        try{draw.run();}finally{clip=saved;}
    }
    public static void foreground(Runnable drawing) {
        float[] bounds=clip;
        FOREGROUND.add(bounds==null?drawing:()->withClip(bounds[0],bounds[1],bounds[2],bounds[3],drawing));
    }
    public static boolean hasBackdrop() { return captured && BlurService.get().captured(); }
    public static GlassQuality effectiveQuality() { return quality; }
    public static int drawCalls() { return drawCalls; }

    public static void flush() {
        // List entries may enqueue their own vanilla widgets after the container batch.
        for(int pass=0;pass<4 && (!PANES.isEmpty() || !FOREGROUND.isEmpty());pass++)flushBatch();
    }

    private static void flushBatch() {
        if (PANES.isEmpty()) { foreground(); return; }
        boolean timer = false;
        try {
            if (!captured && quality != GlassQuality.LOW) {
                quality = GlassQuality.LOW;
                fallbackReason = "backdrop hook not reached; automatic Low";
                GlassDiagnostics.log(fallbackReason);
            }
            if (failed) return;
            if (program == 0) {
                try { initialize(false); }
                catch (Exception error) {
                    BlurService.get().fail(error.getMessage()); quality = GlassQuality.LOW; fallbackReason = error.getMessage();
                    releaseGpu(); initialize(true);
                }
            }
            int copies = instanced ? 1 : 6;
            int required = PANES.size() * FLOATS * copies;
            if (instances.capacity() < required) instances = BufferUtils.createFloatBuffer(Integer.highestOneBit(required - 1) * 2);
            instances.clear();
            for (Pane pane : PANES) for (int copy = 0; copy < copies; copy++) pack(pane);
            instances.flip();
            timer = GlassDiagnostics.timers().begin(GpuGlassTimers.GLASS);
            glUseProgram(program); glBindVertexArray(vao); glBindBuffer(GL_ARRAY_BUFFER, vbo);
            glBufferData(GL_ARRAY_BUFFER, instances, GL_STREAM_DRAW);
            glUniformMatrix4fv(projectionLocation, false, new Matrix4f().setOrtho(0, (float) Math.ceil(width / (double) scale), (float) Math.ceil(height / (double) scale), 0, -1, 1).get(MATRIX));
            glUniform1i(qualityLocation, quality.ordinal());
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
            drawCalls++;
            AdaptiveText.update(hasBackdrop() ? BlurService.get().texture(3) : 0, width, height, scale, 1-.25f*modalProgress, hasBackdrop());
            int error = glGetError();
            if (error != GL_NO_ERROR) throw new IllegalStateException("glass draw GL error 0x" + Integer.toHexString(error));
            for (int i = 0; i < PANES.size(); i++) {
                if (quality == GlassQuality.LOW) GlassDiagnostics.fallback(fallbackReason);
                else GlassDiagnostics.glassDrawn();
            }
        } catch (Exception error) {
            quality = GlassQuality.LOW;
            BlurService.get().fail(error.getMessage());
            try { releaseGpu(); initialize(true); }
            catch (Exception lowError) { failed = true; GlassDiagnostics.rendererFailure("Low shader failed: " + lowError.getMessage()); }
            for (int i = 0; i < PANES.size(); i++) GlassDiagnostics.fallback(error.getMessage());
        } finally {
            GlassDiagnostics.timers().end(timer);
            GuiRenderState.restore(framebuffer, width, height);
            PANES.clear(); foreground();
        }
    }

    private static void foreground() {
        var pending = new ArrayList<>(FOREGROUND);
        FOREGROUND.clear();
        for (Runnable draw : pending) draw.run();
    }

    private static void pack(Pane p) {
        GlassStyle s = p.style;
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

    public static void close() { BackdropRenderer.close(); AdaptiveText.close(); releaseGpu(); PANES.clear(); FOREGROUND.clear(); }
}

package net.auramc.glass.common.render;

import static org.lwjgl.opengl.GL33C.*;

public final class BlurService {
    private static final BlurService INSTANCE = new BlurService();
    private static final String VERTEX = "#version 150\nout vec2 uv; void main(){ vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2); uv=p; gl_Position=vec4(p*2.0-1.0,0,1); }";
    private static final String FRAGMENT = """
        #version 150
        uniform sampler2D source;
        uniform vec2 texel;
        uniform int kernel;
        in vec2 uv;
        out vec4 color;
        void main() {
            if (kernel == 0) {
                vec4 c=textureLod(source,uv,0.0)*4.0;
                c+=textureLod(source,uv+texel,0.0);
                c+=textureLod(source,uv-texel,0.0);
                c+=textureLod(source,uv+vec2(texel.x,-texel.y),0.0);
                c+=textureLod(source,uv+vec2(-texel.x,texel.y),0.0);
                color=c/8.0;
            } else {
                vec4 c=textureLod(source,uv,0.0)*.2270270270;
                c+=(textureLod(source,uv+texel*1.3846153846,0.0)+textureLod(source,uv-texel*1.3846153846,0.0))*.3162162162;
                c+=(textureLod(source,uv+texel*3.2307692308,0.0)+textureLod(source,uv-texel*3.2307692308,0.0))*.0702702703;
                color=c;
            }
        }
        """;
    private final int[] textures = new int[5];
    private final int[] fbos = new int[5];
    private final int[] scratchTextures = new int[5], scratchFbos = new int[5];
    private final int[] widths = new int[5];
    private final int[] heights = new int[5];
    private final java.nio.ByteBuffer ambientPixel = java.nio.ByteBuffer.allocateDirect(4);
    private int program, vao, width, height, sourceLocation, texelLocation, kernelLocation;
    private boolean captured, attempted, failedForScreen, activeLogged, inactiveLogged;
    private static final int LEVELS = 3;
    private Object screen;
    private String version = "unknown";
    private String failureReason = "none";
    private final GlBackend gl;

    private BlurService() { this(new DefaultGlBackend()); }
    BlurService(GlBackend gl) { this.gl = gl; }
    public static BlurService get() { return INSTANCE; }

    public void beginFrame(Object nextScreen, String minecraftVersion) {
        if (screen != nextScreen) {
            screen=nextScreen;
            failedForScreen=activeLogged=inactiveLogged=false;
            failureReason="none";
        }
        version = minecraftVersion;
        captured = false;
        attempted = false;
    }

    public void prepare(int mainFbo,int w,int h) {
        if(w<=0 || h<=0 || width==w && height==h && program!=0)return;
        try {
            gl.bindFramebuffer(GL_FRAMEBUFFER,mainFbo);
            int samples=gl.getInteger(GL_SAMPLES);
            if(samples>0){fail("MSAA main framebuffer samples="+samples);return;}
            release();
            if(program==0)initializeProgram();
            initialize(w,h);
            failedForScreen=false;
            failureReason="none";
        }catch(RuntimeException error){release();fail("blur prepare: "+error.getMessage());}
        finally{gl.restoreRenderState(mainFbo,w,h);}
    }

    public boolean capture(int mainFbo, int w, int h) {
        if (captured) return true;
        if (attempted) return false;
        attempted = true;
        if (failedForScreen) return false;
        if (w <= 0 || h <= 0) return fail("invalid framebuffer size");
        clearErrors();

        try {
            gl.bindFramebuffer(GL_READ_FRAMEBUFFER, mainFbo);
            int mainStatus = gl.checkFramebufferStatus(GL_READ_FRAMEBUFFER);
            if (mainStatus != GL_FRAMEBUFFER_COMPLETE) return fail("main framebuffer incomplete: 0x" + Integer.toHexString(mainStatus));
            gl.bindFramebuffer(GL_DRAW_FRAMEBUFFER, mainFbo);

            gl.activeTexture(GL_TEXTURE0);
            gl.bindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
            gl.clearSampler(0);
            if (width != w || height != h || program == 0) {
                prepare(mainFbo, w, h);
                if (width != w || height != h || program == 0) return fail("blur resources not prepared for framebuffer");
            }
            gl.disable(GL_SCISSOR_TEST);
            gl.disable(GL_BLEND); gl.disable(GL_DEPTH_TEST); gl.disable(GL_CULL_FACE); gl.disable(GL_STENCIL_TEST);
            gl.colorMask(true, true, true, true);
            gl.activeTexture(GL_TEXTURE0);
            gl.bindFramebuffer(GL_READ_FRAMEBUFFER, mainFbo);
            gl.bindTexture(textures[0]);
            boolean captureTimer = GlassDiagnostics.timers().begin(GpuGlassTimers.CAPTURE);
            try {
                gl.copyTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, 0, 0, w, h);
                check("capture");
                GlassDiagnostics.captureDone();
            } finally { GlassDiagnostics.timers().end(captureTimer); }
            gl.useProgram(program); gl.bindVertexArray(vao);
            gl.uniform1i(sourceLocation, 0);
            boolean blurTimer = GlassDiagnostics.timers().begin(GpuGlassTimers.BLUR);
            try {
                for (int i = 1; i <= LEVELS; i++) {
                    pass(i - 1, i);
                    gaussian(i,true);
                    gaussian(i,false);
                }

                check("blur");
            } finally { GlassDiagnostics.timers().end(blurTimer); }
            captured = true;
            return true;
        } catch (Exception e) {
            release();
            return fail("blur service: " + e.getClass().getSimpleName() + ": " + e.getMessage());
        } finally { gl.restoreRenderState(mainFbo, w, h); }
    }

    private void pass(int from, int to) {
        gl.bindFramebuffer(GL_DRAW_FRAMEBUFFER, fbos[to]);
        gl.viewport(0, 0, widths[to], heights[to]);
        gl.bindTexture(textures[from]);
        gl.uniform2f(texelLocation, 1f / widths[from], 1f / heights[from]);
        gl.uniform1i(kernelLocation, 0);
        gl.drawArrays(GL_TRIANGLES, 0, 3);
    }

    private void gaussian(int level,boolean horizontal) {
        gl.bindFramebuffer(GL_DRAW_FRAMEBUFFER,horizontal?scratchFbos[level]:fbos[level]);
        gl.viewport(0,0,widths[level],heights[level]);
        gl.bindTexture(horizontal?textures[level]:scratchTextures[level]);
        gl.uniform2f(texelLocation,horizontal?1f/widths[level]:0,horizontal?0:1f/heights[level]);
        gl.uniform1i(kernelLocation,1);gl.drawArrays(GL_TRIANGLES,0,3);
    }

    private void initializeProgram() {
        int vs = compile(GL_VERTEX_SHADER, VERTEX), fs = 0;
        try {
            fs = compile(GL_FRAGMENT_SHADER, FRAGMENT);
            program = gl.createProgram(); gl.attachShader(program, vs); gl.attachShader(program, fs); gl.linkProgram(program);
            if (gl.getProgrami(program, GL_LINK_STATUS) == GL_FALSE) {
                String error = gl.getProgramInfoLog(program);
                gl.deleteProgram(program); program = 0;
                throw new IllegalStateException("blur shader link: " + error);
            }
        } finally { gl.deleteShader(vs); if (fs != 0) gl.deleteShader(fs); }
        sourceLocation = gl.getUniformLocation(program, "source");
        texelLocation = gl.getUniformLocation(program, "texel");
        kernelLocation = gl.getUniformLocation(program, "kernel");
        vao = gl.genVertexArray();
    }

    private void initialize(int w, int h) {
        width = w; height = h;
        for (int i = 0; i <= LEVELS; i++) {
            widths[i] = Math.max(1, w >> i); heights[i] = Math.max(1, h >> i);
            textures[i] = gl.genTexture(); gl.bindTexture(textures[i]);
            gl.texParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR); gl.texParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            gl.texParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE); gl.texParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            gl.texImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, widths[i], heights[i], 0, GL_RGBA, GL_UNSIGNED_BYTE, 0L);
            if (i > 0) {
                fbos[i] = gl.genFramebuffer(); gl.bindFramebuffer(GL_DRAW_FRAMEBUFFER, fbos[i]);
                gl.framebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, textures[i], 0);
                int status = gl.checkFramebufferStatus(GL_DRAW_FRAMEBUFFER);
                if (status != GL_FRAMEBUFFER_COMPLETE) throw new IllegalStateException("blur framebuffer level " + i + " incomplete: 0x" + Integer.toHexString(status));
            }
            if(i>0) {
                scratchTextures[i]=gl.genTexture();gl.bindTexture(scratchTextures[i]);
                gl.texParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_LINEAR);gl.texParameteri(GL_TEXTURE_2D,GL_TEXTURE_MAG_FILTER,GL_LINEAR);
                gl.texParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_S,GL_CLAMP_TO_EDGE);gl.texParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_T,GL_CLAMP_TO_EDGE);
                gl.texImage2D(GL_TEXTURE_2D,0,GL_RGBA8,widths[i],heights[i],0,GL_RGBA,GL_UNSIGNED_BYTE,0L);
                scratchFbos[i]=gl.genFramebuffer();gl.bindFramebuffer(GL_DRAW_FRAMEBUFFER,scratchFbos[i]);
                gl.framebufferTexture2D(GL_DRAW_FRAMEBUFFER,GL_COLOR_ATTACHMENT0,GL_TEXTURE_2D,scratchTextures[i],0);
                int scratchStatus=gl.checkFramebufferStatus(GL_DRAW_FRAMEBUFFER);
                if(scratchStatus!=GL_FRAMEBUFFER_COMPLETE)throw new IllegalStateException("blur scratch framebuffer level "+i+" incomplete: 0x"+Integer.toHexString(scratchStatus));
            }
        }
        check("initialize");
        System.out.println("[Aura] Glass BlurService initialized: Minecraft " + version + ", " + w + "x" + h + ", levels=" + LEVELS);
    }

    private int compile(int type, String source) {
        int shader = gl.createShader(type); gl.shaderSource(shader, source); gl.compileShader(shader);
        if (gl.getShaderi(shader, GL_COMPILE_STATUS) == GL_FALSE) { String error = gl.getShaderInfoLog(shader); gl.deleteShader(shader); throw new IllegalStateException("blur " + (type == GL_VERTEX_SHADER ? "vertex" : "fragment") + " shader compile: " + error); }
        return shader;
    }
    private void clearErrors() {
        for (int i = 0; i < 16; i++) {
            int error = gl.getError();
            if (error == GL_NO_ERROR) break;
            GlassDiagnostics.log("before blur capture GL error 0x" + Integer.toHexString(error) + " (" + error + ")");
        }
    }
    private void check(String operation) { int error = gl.getError(); if (error != GL_NO_ERROR) throw new IllegalStateException(operation + " GL error 0x" + Integer.toHexString(error) + " (" + error + ")"); }

    public float sampleAmbient(float u, float v) {
        if (!captured || program == 0 || fbos[LEVELS] == 0 || widths[LEVELS] <= 0 || heights[LEVELS] <= 0) return 0.2f;
        int px = Math.max(0, Math.min(widths[LEVELS] - 1, (int)(u * widths[LEVELS])));
        int py = Math.max(0, Math.min(heights[LEVELS] - 1, (int)((1.0f - v) * heights[LEVELS])));
        int prevFbo = gl.getInteger(GL_READ_FRAMEBUFFER_BINDING);
        gl.bindFramebuffer(GL_READ_FRAMEBUFFER, fbos[LEVELS]);
        ambientPixel.clear();
        gl.readPixels(px, py, 1, 1, GL_RGBA, GL_UNSIGNED_BYTE, ambientPixel);
        if (prevFbo != fbos[LEVELS]) {
            gl.bindFramebuffer(GL_READ_FRAMEBUFFER, prevFbo);
        }
        int r = ambientPixel.get(0) & 0xFF;
        int g = ambientPixel.get(1) & 0xFF;
        int b = ambientPixel.get(2) & 0xFF;
        return (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255.0f;
    }

    public int fbo(int level) { return fbos[Math.max(0, Math.min(LEVELS, level))]; }
    public int fboWidth(int level) { return widths[Math.max(0, Math.min(LEVELS, level))]; }
    public int fboHeight(int level) { return heights[Math.max(0, Math.min(LEVELS, level))]; }
    public int levels() { return LEVELS; }
    public boolean captured() { return captured; }
    public int texture(int level) { return textures[Math.max(0, Math.min(3, level))]; }
    public int sharpTexture() { return textures[0]; }
    public int blurredTexture() { return textures[1]; }
    public int width() { return width; }
    public int height() { return height; }
    public boolean failedForScreen() { return failedForScreen; }
    public String failureReason() { return failureReason; }
    public boolean isInitialized() { return program != 0 && width > 0 && textures[0] != 0; }
    public boolean fail(String reason) {
        failedForScreen = true;
        failureReason = reason;
        GlassDiagnostics.log(reason);
        return false;
    }
    public void active(float guiScale) {
        if (!activeLogged) {
            activeLogged = true;
            System.out.println("[Aura] Glass active: " + (screen == null ? "unknown" : screen.getClass().getSimpleName()) + ", MC " + version + ", " + width + "x" + height + ", guiScale=" + guiScale);
        }
    }
    public void close() { release(); screen = null; failureReason = "none"; failedForScreen = activeLogged = inactiveLogged = captured = attempted = false; }
    public void shutdown() {
        close();
        if (vao != 0) gl.deleteVertexArray(vao);
        if (program != 0) gl.deleteProgram(program);
        vao = program = 0;
    }
    private void release() {
        for (int i = 0; i < textures.length; i++) {
            if (fbos[i] != 0) gl.deleteFramebuffer(fbos[i]);
            if (textures[i] != 0) gl.deleteTexture(textures[i]);
            if(scratchFbos[i]!=0) gl.deleteFramebuffer(scratchFbos[i]);
            if(scratchTextures[i]!=0) gl.deleteTexture(scratchTextures[i]);
            scratchFbos[i]=scratchTextures[i]=fbos[i]=textures[i]=0;
        }
        width = height = 0; captured = false;
    }

    interface GlBackend {
        int genTexture();
        void bindTexture(int texture);
        void texParameteri(int target, int pname, int param);
        void texImage2D(int target, int level, int internalformat, int width, int height, int border, int format, int type, long pixels);
        int genFramebuffer();
        void bindFramebuffer(int target, int framebuffer);
        void framebufferTexture2D(int target, int attachment, int textarget, int texture, int level);
        int checkFramebufferStatus(int target);
        void deleteFramebuffer(int fbo);
        void deleteTexture(int texture);
        void copyTexSubImage2D(int target, int level, int xoffset, int yoffset, int x, int y, int width, int height);
        int createProgram();
        int createShader(int type);
        void shaderSource(int shader, String source);
        void compileShader(int shader);
        int getShaderi(int shader, int pname);
        String getShaderInfoLog(int shader);
        void attachShader(int program, int shader);
        void linkProgram(int program);
        int getProgrami(int program, int pname);
        String getProgramInfoLog(int program);
        void deleteShader(int shader);
        void deleteProgram(int program);
        int getUniformLocation(int program, String name);
        int genVertexArray();
        void deleteVertexArray(int vao);
        void bindVertexArray(int vao);
        void useProgram(int program);
        void uniform1i(int location, int v0);
        void uniform2f(int location, float v0, float v1);
        void viewport(int x, int y, int width, int height);
        void drawArrays(int mode, int first, int count);
        void disable(int cap);
        void colorMask(boolean red, boolean green, boolean blue, boolean alpha);
        void activeTexture(int texture);
        void bindBuffer(int target, int buffer);
        int getInteger(int pname);
        int getError();
        void readPixels(int x, int y, int width, int height, int format, int type, java.nio.ByteBuffer pixels);
        default void clearSampler(int unit) { GuiRenderState.clearSampler(unit); }
        default void restoreRenderState(int fbo, int width, int height) { GuiRenderState.restore(fbo, width, height); }
    }

    private static final class DefaultGlBackend implements GlBackend {
        @Override public int genTexture() { return glGenTextures(); }
        @Override public void bindTexture(int texture) { com.mojang.blaze3d.platform.GlStateManager._bindTexture(texture); }
        @Override public void texParameteri(int target, int pname, int param) { glTexParameteri(target, pname, param); }
        @Override public void texImage2D(int target, int level, int internalformat, int width, int height, int border, int format, int type, long pixels) {
            glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
        }
        @Override public int genFramebuffer() { return glGenFramebuffers(); }
        @Override public void bindFramebuffer(int target, int framebuffer) { glBindFramebuffer(target, framebuffer); }
        @Override public void framebufferTexture2D(int target, int attachment, int textarget, int texture, int level) {
            glFramebufferTexture2D(target, attachment, textarget, texture, level);
        }
        @Override public int checkFramebufferStatus(int target) { return glCheckFramebufferStatus(target); }
        @Override public void deleteFramebuffer(int fbo) { glDeleteFramebuffers(fbo); }
        @Override public void deleteTexture(int texture) { com.mojang.blaze3d.platform.GlStateManager._deleteTexture(texture); }
        @Override public void copyTexSubImage2D(int target, int level, int xoffset, int yoffset, int x, int y, int width, int height) {
            glCopyTexSubImage2D(target, level, xoffset, yoffset, x, y, width, height);
        }
        @Override public int createProgram() { return glCreateProgram(); }
        @Override public int createShader(int type) { return glCreateShader(type); }
        @Override public void shaderSource(int shader, String source) { glShaderSource(shader, source); }
        @Override public void compileShader(int shader) { glCompileShader(shader); }
        @Override public int getShaderi(int shader, int pname) { return glGetShaderi(shader, pname); }
        @Override public String getShaderInfoLog(int shader) { return glGetShaderInfoLog(shader); }
        @Override public void attachShader(int program, int shader) { glAttachShader(program, shader); }
        @Override public void linkProgram(int program) { glLinkProgram(program); }
        @Override public int getProgrami(int program, int pname) { return glGetProgrami(program, pname); }
        @Override public String getProgramInfoLog(int program) { return glGetProgramInfoLog(program); }
        @Override public void deleteShader(int shader) { glDeleteShader(shader); }
        @Override public void deleteProgram(int program) { glDeleteProgram(program); }
        @Override public int getUniformLocation(int program, String name) { return glGetUniformLocation(program, name); }
        @Override public int genVertexArray() { return glGenVertexArrays(); }
        @Override public void deleteVertexArray(int vao) { glDeleteVertexArrays(vao); }
        @Override public void bindVertexArray(int vao) { glBindVertexArray(vao); }
        @Override public void useProgram(int program) { glUseProgram(program); }
        @Override public void uniform1i(int location, int v0) { glUniform1i(location, v0); }
        @Override public void uniform2f(int location, float v0, float v1) { glUniform2f(location, v0, v1); }
        @Override public void viewport(int x, int y, int width, int height) { glViewport(x, y, width, height); }
        @Override public void drawArrays(int mode, int first, int count) { glDrawArrays(mode, first, count); }
        @Override public void disable(int cap) { glDisable(cap); }
        @Override public void colorMask(boolean red, boolean green, boolean blue, boolean alpha) { glColorMask(red, green, blue, alpha); }
        @Override public void activeTexture(int texture) { com.mojang.blaze3d.platform.GlStateManager._activeTexture(texture); }
        @Override public void bindBuffer(int target, int buffer) { glBindBuffer(target, buffer); }
        @Override public int getInteger(int pname) { return glGetInteger(pname); }
        @Override public int getError() { return glGetError(); }
        @Override public void readPixels(int x, int y, int width, int height, int format, int type, java.nio.ByteBuffer pixels) { glReadPixels(x, y, width, height, format, type, pixels); }
    }
}

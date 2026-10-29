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
    private int program, vao, width, height, sourceLocation, texelLocation, kernelLocation;
    private boolean captured, attempted, failedForScreen, activeLogged, inactiveLogged;
    private static final int LEVELS = 3;
    private Object screen;
    private String version = "unknown";
    private String failureReason = "none";

    private BlurService() {}
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
            glBindFramebuffer(GL_FRAMEBUFFER,mainFbo);
            int samples=glGetInteger(GL_SAMPLES);
            if(samples>0){fail("MSAA main framebuffer samples="+samples);return;}
            release();
            if(program==0)initializeProgram();
            initialize(w,h);
            failedForScreen=false;
            failureReason="none";
        }catch(RuntimeException error){release();fail("blur prepare: "+error.getMessage());}
        finally{GuiRenderState.restore(mainFbo,w,h);}
    }

    public boolean capture(int mainFbo, int w, int h) {
        if (captured) return true;
        if (attempted) return false;
        attempted = true;
        if (failedForScreen) return false;
        if (w <= 0 || h <= 0) return fail("invalid framebuffer size");
        clearErrors();

        try {
            glBindFramebuffer(GL_READ_FRAMEBUFFER, mainFbo);
            int mainStatus = glCheckFramebufferStatus(GL_READ_FRAMEBUFFER);
            if (mainStatus != GL_FRAMEBUFFER_COMPLETE) return fail("main framebuffer incomplete: 0x" + Integer.toHexString(mainStatus));
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, mainFbo);

            com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE0);
            glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
            GuiRenderState.clearSampler(0);
            if (width != w || height != h || program == 0) {
                prepare(mainFbo, w, h);
                if (width != w || height != h || program == 0) return fail("blur resources not prepared for framebuffer");
            }
            glDisable(GL_SCISSOR_TEST);
            glDisable(GL_BLEND); glDisable(GL_DEPTH_TEST); glDisable(GL_CULL_FACE); glDisable(GL_STENCIL_TEST);
            glColorMask(true, true, true, true);
            com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE0);
            glBindFramebuffer(GL_READ_FRAMEBUFFER, mainFbo);
            com.mojang.blaze3d.platform.GlStateManager._bindTexture(textures[0]);
            boolean captureTimer = GlassDiagnostics.timers().begin(GpuGlassTimers.CAPTURE);
            try {
                glCopyTexSubImage2D(GL_TEXTURE_2D, 0, 0, 0, 0, 0, w, h);
                check("capture");
                GlassDiagnostics.captureDone();
            } finally { GlassDiagnostics.timers().end(captureTimer); }
            glUseProgram(program); glBindVertexArray(vao);
            glUniform1i(sourceLocation, 0);
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
        } finally { GuiRenderState.restore(mainFbo, w, h); }
    }

    private void pass(int from, int to) {
        glBindFramebuffer(GL_DRAW_FRAMEBUFFER, fbos[to]);
        glViewport(0, 0, widths[to], heights[to]);
        com.mojang.blaze3d.platform.GlStateManager._bindTexture(textures[from]);
        glUniform2f(texelLocation, 1f / widths[from], 1f / heights[from]);
        glUniform1i(kernelLocation, 0);
        glDrawArrays(GL_TRIANGLES, 0, 3);
    }

    private void gaussian(int level,boolean horizontal) {
        glBindFramebuffer(GL_DRAW_FRAMEBUFFER,horizontal?scratchFbos[level]:fbos[level]);
        glViewport(0,0,widths[level],heights[level]);
        com.mojang.blaze3d.platform.GlStateManager._bindTexture(horizontal?textures[level]:scratchTextures[level]);
        glUniform2f(texelLocation,horizontal?1f/widths[level]:0,horizontal?0:1f/heights[level]);
        glUniform1i(kernelLocation,1);glDrawArrays(GL_TRIANGLES,0,3);
    }

    private void initializeProgram() {
        int vs = compile(GL_VERTEX_SHADER, VERTEX), fs = 0;
        try {
            fs = compile(GL_FRAGMENT_SHADER, FRAGMENT);
            program = glCreateProgram(); glAttachShader(program, vs); glAttachShader(program, fs); glLinkProgram(program);
            if (glGetProgrami(program, GL_LINK_STATUS) == GL_FALSE) {
                String error = glGetProgramInfoLog(program);
                glDeleteProgram(program); program = 0;
                throw new IllegalStateException("blur shader link: " + error);
            }
        } finally { glDeleteShader(vs); if (fs != 0) glDeleteShader(fs); }
        sourceLocation = glGetUniformLocation(program, "source");
        texelLocation = glGetUniformLocation(program, "texel");
        kernelLocation = glGetUniformLocation(program, "kernel");
        vao = glGenVertexArrays();
    }

    private void initialize(int w, int h) {
        width = w; height = h;
        for (int i = 0; i <= LEVELS; i++) {
            widths[i] = Math.max(1, w >> i); heights[i] = Math.max(1, h >> i);
            textures[i] = glGenTextures(); com.mojang.blaze3d.platform.GlStateManager._bindTexture(textures[i]);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE); glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, widths[i], heights[i], 0, GL_RGBA, GL_UNSIGNED_BYTE, 0L);
            if (i > 0) {
                fbos[i] = glGenFramebuffers(); glBindFramebuffer(GL_DRAW_FRAMEBUFFER, fbos[i]);
                glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, textures[i], 0);
                int status = glCheckFramebufferStatus(GL_DRAW_FRAMEBUFFER);
                if (status != GL_FRAMEBUFFER_COMPLETE) throw new IllegalStateException("blur framebuffer level " + i + " incomplete: 0x" + Integer.toHexString(status));
            }
            if(i>0) {
                scratchTextures[i]=glGenTextures();com.mojang.blaze3d.platform.GlStateManager._bindTexture(scratchTextures[i]);
                glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MAG_FILTER,GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_S,GL_CLAMP_TO_EDGE);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_T,GL_CLAMP_TO_EDGE);
                glTexImage2D(GL_TEXTURE_2D,0,GL_RGBA8,widths[i],heights[i],0,GL_RGBA,GL_UNSIGNED_BYTE,0L);
                scratchFbos[i]=glGenFramebuffers();glBindFramebuffer(GL_DRAW_FRAMEBUFFER,scratchFbos[i]);
                glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER,GL_COLOR_ATTACHMENT0,GL_TEXTURE_2D,scratchTextures[i],0);
                int scratchStatus=glCheckFramebufferStatus(GL_DRAW_FRAMEBUFFER);
                if(scratchStatus!=GL_FRAMEBUFFER_COMPLETE)throw new IllegalStateException("blur scratch framebuffer level "+i+" incomplete: 0x"+Integer.toHexString(scratchStatus));
            }
        }
        check("initialize");
        System.out.println("[Aura] Glass BlurService initialized: Minecraft " + version + ", " + w + "x" + h + ", levels=" + LEVELS);
    }

    private static int compile(int type, String source) {
        int shader = glCreateShader(type); glShaderSource(shader, source); glCompileShader(shader);
        if (glGetShaderi(shader, GL_COMPILE_STATUS) == GL_FALSE) { String error = glGetShaderInfoLog(shader); glDeleteShader(shader); throw new IllegalStateException("blur " + (type == GL_VERTEX_SHADER ? "vertex" : "fragment") + " shader compile: " + error); }
        return shader;
    }
    private static void clearErrors() { GlassDiagnostics.drainErrors("before blur capture"); }
    private static void check(String operation) { int error = glGetError(); if (error != GL_NO_ERROR) throw new IllegalStateException(operation + " GL error 0x" + Integer.toHexString(error) + " (" + error + ")"); }
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
        if (vao != 0) glDeleteVertexArrays(vao);
        if (program != 0) glDeleteProgram(program);
        vao = program = 0;
    }
    private void release() {
        for (int i = 0; i < textures.length; i++) { if (fbos[i] != 0) glDeleteFramebuffers(fbos[i]); if (textures[i] != 0) com.mojang.blaze3d.platform.GlStateManager._deleteTexture(textures[i]);
            if(scratchFbos[i]!=0)glDeleteFramebuffers(scratchFbos[i]);
            if(scratchTextures[i]!=0)com.mojang.blaze3d.platform.GlStateManager._deleteTexture(scratchTextures[i]);
            scratchFbos[i]=scratchTextures[i]=fbos[i]=textures[i]=0; }
        width = height = 0; captured = false;
    }

}

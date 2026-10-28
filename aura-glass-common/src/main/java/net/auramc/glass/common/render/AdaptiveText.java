package net.auramc.glass.common.render;

import com.mojang.blaze3d.platform.GlStateManager;
import net.auramc.glass.common.style.GlassStyle;
import org.lwjgl.BufferUtils;
import java.nio.FloatBuffer;
import java.util.IdentityHashMap;
import java.util.Map;
import static org.lwjgl.opengl.GL33C.*;

public final class AdaptiveText {
    private static final int CAPACITY = 256;
    private static final Map<Object,Integer> SLOTS = new IdentityHashMap<>();
    private static final FloatBuffer DATA = BufferUtils.createFloatBuffer(CAPACITY * 8);
    private static final int[] textures = new int[2], framebuffers = new int[2];
    private static int program, vao, vbo, current, count, sizeUniform, dimUniform, backdropUniform;
    private static Object screen;
    private static boolean reset,failed;
    private AdaptiveText() {}

    public static void beginFrame(Object nextScreen) {
        if (screen != nextScreen) { screen=nextScreen; SLOTS.clear(); reset=true; }
        DATA.clear(); count=0;
    }
    public static int request(Object key, float centerX, float centerY, GlassStyle style) {
        if (!SLOTS.containsKey(key)) {
            if (SLOTS.size() >= CAPACITY) return -1;
            SLOTS.put(key,SLOTS.size());
        }
        int slot=SLOTS.get(key);
        if (count < CAPACITY) {
            DATA.put(centerX).put(centerY).put(slot).put(style.tintA);
            DATA.put(style.tintR).put(style.tintG).put(style.tintB).put(0); count++;
        }
        return slot;
    }
    public static int slot(Object key) { return failed ? -1 : SLOTS.getOrDefault(key,-1); }
    public static int texture() { return textures[current]; }

    static void update(int backdrop, int width, int height, float scale, float dim, boolean available) {
        if (count==0 || failed) return;
        try {
            if (program==0) initialize();
            if (reset) {
                for(int fbo:framebuffers) { glBindFramebuffer(GL_DRAW_FRAMEBUFFER,fbo); glClearBufferfv(GL_COLOR,0,new float[]{0,0,0,0}); }
                reset=false;
            }
            int next=1-current;
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER,framebuffers[next]); glViewport(0,0,CAPACITY,1);
            glDisable(GL_BLEND); glDisable(GL_DEPTH_TEST); glDisable(GL_CULL_FACE); glDisable(GL_SCISSOR_TEST);
            glUseProgram(program); glBindVertexArray(vao); glBindBuffer(GL_ARRAY_BUFFER,vbo);
            FloatBuffer upload=DATA.duplicate();upload.flip();glBufferData(GL_ARRAY_BUFFER,upload,GL_STREAM_DRAW);
            glUniform3f(sizeUniform,(float)Math.ceil(width/scale),(float)Math.ceil(height/scale),CAPACITY);
            glUniform1f(dimUniform,dim); glUniform1i(backdropUniform,available?1:0);
            GlStateManager._activeTexture(GL_TEXTURE0); GuiRenderState.clearSampler(0); GlStateManager._bindTexture(backdrop);
            GlStateManager._activeTexture(GL_TEXTURE1); GuiRenderState.clearSampler(1); GlStateManager._bindTexture(textures[current]);
            glPointSize(1); glDrawArrays(GL_POINTS,0,count); current=next;
        } catch(Exception error) { failed=true; GlassDiagnostics.log("Adaptive text unavailable: "+error.getMessage()); }
    }

    private static int compile(int type,String source) {
        int shader=glCreateShader(type);glShaderSource(shader,source);glCompileShader(shader);
        if(glGetShaderi(shader,GL_COMPILE_STATUS)==0) {String log=glGetShaderInfoLog(shader);glDeleteShader(shader);throw new IllegalStateException(log);}
        return shader;
    }
    private static void initialize() {
        int vs=compile(GL_VERTEX_SHADER,"""
            #version 150
            in vec4 Sample; in vec4 Tint;
            uniform vec3 Size;
            flat out vec2 center; flat out vec4 tint; flat out int slot;
            void main(){slot=int(Sample.z);center=vec2(Sample.x/Size.x,1.0-Sample.y/Size.y);tint=vec4(Tint.rgb,Sample.w);
            gl_PointSize=1.0;gl_Position=vec4((Sample.z+.5)/Size.z*2.0-1.0,0,0,1);}
            """);
        int fs=compile(GL_FRAGMENT_SHADER,"""
            #version 150
            uniform sampler2D Backdrop; uniform sampler2D History;
            uniform float Dim; uniform int Available;
            flat in vec2 center; flat in vec4 tint; flat in int slot;
            out vec4 result;
            void main(){vec3 base=Available!=0?texture(Backdrop,clamp(center,vec2(0),vec2(1))).rgb*Dim:vec3(.5);
            float luma=dot(mix(base,tint.rgb,tint.a),vec3(.2126,.7152,.0722));
            float previous=texelFetch(History,ivec2(slot,0),0).r;
            float dark=previous>.5?(luma>.57?1.0:0.0):(luma>.63?1.0:0.0);
            result=vec4(dark,luma,0,1);}
            """);
        program=glCreateProgram();glAttachShader(program,vs);glAttachShader(program,fs);
        glBindAttribLocation(program,0,"Sample");glBindAttribLocation(program,1,"Tint");glLinkProgram(program);
        glDeleteShader(vs);glDeleteShader(fs);
        if(glGetProgrami(program,GL_LINK_STATUS)==0)throw new IllegalStateException(glGetProgramInfoLog(program));
        glUseProgram(program);glUniform1i(glGetUniformLocation(program,"Backdrop"),0);glUniform1i(glGetUniformLocation(program,"History"),1);
        sizeUniform=glGetUniformLocation(program,"Size");dimUniform=glGetUniformLocation(program,"Dim");backdropUniform=glGetUniformLocation(program,"Available");
        vao=glGenVertexArrays();vbo=glGenBuffers();glBindVertexArray(vao);glBindBuffer(GL_ARRAY_BUFFER,vbo);
        for(int i=0;i<2;i++){glEnableVertexAttribArray(i);glVertexAttribPointer(i,4,GL_FLOAT,false,32,i*16L);}
        for(int i=0;i<2;i++){
            textures[i]=glGenTextures();GlStateManager._activeTexture(GL_TEXTURE0);GlStateManager._bindTexture(textures[i]);
            glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_NEAREST);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MAG_FILTER,GL_NEAREST);
            glBindBuffer(GL_PIXEL_UNPACK_BUFFER,0);glTexImage2D(GL_TEXTURE_2D,0,GL_RGBA8,CAPACITY,1,0,GL_RGBA,GL_UNSIGNED_BYTE,0L);
            framebuffers[i]=glGenFramebuffers();glBindFramebuffer(GL_DRAW_FRAMEBUFFER,framebuffers[i]);
            glFramebufferTexture2D(GL_DRAW_FRAMEBUFFER,GL_COLOR_ATTACHMENT0,GL_TEXTURE_2D,textures[i],0);
            if(glCheckFramebufferStatus(GL_DRAW_FRAMEBUFFER)!=GL_FRAMEBUFFER_COMPLETE)throw new IllegalStateException("contrast framebuffer incomplete");
        }
        reset=true;
    }
    public static void close() {
        for(int id:textures)if(id!=0)glDeleteTextures(id);
        for(int id:framebuffers)if(id!=0)glDeleteFramebuffers(id);
        if(program!=0)glDeleteProgram(program);if(vao!=0)glDeleteVertexArrays(vao);if(vbo!=0)glDeleteBuffers(vbo);
        java.util.Arrays.fill(textures,0);java.util.Arrays.fill(framebuffers,0);program=vao=vbo=0;SLOTS.clear();screen=null;failed=false;
    }
}

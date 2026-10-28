package net.auramc.glass.common.render;

import com.mojang.blaze3d.platform.GlStateManager;
import net.minecraft.client.MinecraftClient;
import static org.lwjgl.opengl.GL33C.*;

public final class BackdropRenderer {
    private static int program,vao,amountLocation,dimLocation,availableLocation,worldTexture,worldW,worldH;
    private static boolean requested;
    private BackdropRenderer() {}
    public static void prepare(){
        if(program!=0)return;
        try{initialize();}
        catch(RuntimeException error){GlassDiagnostics.log("Backdrop renderer unavailable: "+error.getMessage());}
    }
    public static void requestWorldImage() { requested=net.auramc.glass.common.style.GlassStyles.quality().needsBackdrop(); }
    public static boolean hasWorldImage() { return worldTexture!=0; }
    public static void captureWorldIfRequested() {
        if(!requested)return;
        requested=false;
        if(!net.auramc.glass.common.style.GlassStyles.quality().needsBackdrop())return;
        var c=MinecraftClient.getInstance();if(c.world==null)return;
        var f=c.getFramebuffer();
        if(worldTexture==0)worldTexture=glGenTextures();
        GlStateManager._activeTexture(GL_TEXTURE0);GlStateManager._bindTexture(worldTexture);
        glBindBuffer(GL_PIXEL_UNPACK_BUFFER,0);
        if(worldW!=f.textureWidth || worldH!=f.textureHeight){
            worldW=f.textureWidth;worldH=f.textureHeight;
            glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MAG_FILTER,GL_LINEAR);
            glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_S,GL_CLAMP_TO_EDGE);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_WRAP_T,GL_CLAMP_TO_EDGE);
            glTexImage2D(GL_TEXTURE_2D,0,GL_RGBA8,worldW,worldH,0,GL_RGBA,GL_UNSIGNED_BYTE,0L);
        }
        glBindFramebuffer(GL_READ_FRAMEBUFFER,f.fbo);glCopyTexSubImage2D(GL_TEXTURE_2D,0,0,0,0,0,worldW,worldH);
        GuiRenderState.restore(f.fbo,f.textureWidth,f.textureHeight);
    }
    public static void worldImage() { if(worldTexture!=0)draw(worldTexture,worldTexture,0,1,true); }
    static void modal(float progress,boolean available) {
        boolean timer=GlassDiagnostics.timers().begin(GpuGlassTimers.GLASS);
        try {draw(BlurService.get().texture(0),BlurService.get().texture(1),progress,1-.25f*progress,available);}
        finally {GlassDiagnostics.timers().end(timer);}
    }
    private static void draw(int sharp,int blurred,float amount,float dim,boolean available) {
        var f=MinecraftClient.getInstance().getFramebuffer();
        try {
            if(program==0)return;
            glBindFramebuffer(GL_FRAMEBUFFER,f.fbo);glViewport(0,0,f.textureWidth,f.textureHeight);
            glDisable(GL_DEPTH_TEST);glDisable(GL_CULL_FACE);glDisable(GL_SCISSOR_TEST);
            glEnable(GL_BLEND);glBlendFuncSeparate(GL_ONE,GL_ONE_MINUS_SRC_ALPHA,GL_ONE,GL_ONE_MINUS_SRC_ALPHA);
            glUseProgram(program);glBindVertexArray(vao);
            glUniform1f(amountLocation,amount);glUniform1f(dimLocation,dim);glUniform1i(availableLocation,available?1:0);
            GlStateManager._activeTexture(GL_TEXTURE0);GuiRenderState.clearSampler(0);GlStateManager._bindTexture(sharp);
            GlStateManager._activeTexture(GL_TEXTURE1);GuiRenderState.clearSampler(1);GlStateManager._bindTexture(blurred);
            glDrawArrays(GL_TRIANGLES,0,3);
        } catch(RuntimeException error) { GlassDiagnostics.log("Backdrop renderer unavailable: "+error.getMessage());
        } finally {GuiRenderState.restore(f.fbo,f.textureWidth,f.textureHeight);}
    }
    private static int compile(int type,String source) {
        int id=glCreateShader(type);glShaderSource(id,source);glCompileShader(id);
        if(glGetShaderi(id,GL_COMPILE_STATUS)==0)throw new IllegalStateException(glGetShaderInfoLog(id));return id;
    }
    private static void initialize() {
        int v=compile(GL_VERTEX_SHADER,"#version 150\nout vec2 uv;void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);uv=p;gl_Position=vec4(p*2.0-1.0,0,1);}");
        int f=compile(GL_FRAGMENT_SHADER,"#version 150\nuniform sampler2D Sharp;uniform sampler2D Blurred;uniform float Amount;uniform float Dim;uniform int Available;in vec2 uv;out vec4 color;void main(){color=Available!=0?vec4(mix(texture(Sharp,uv).rgb,texture(Blurred,uv).rgb,Amount)*Dim,1):vec4(0,0,0,.25*Amount);}");
        program=glCreateProgram();glAttachShader(program,v);glAttachShader(program,f);glLinkProgram(program);glDeleteShader(v);glDeleteShader(f);
        if(glGetProgrami(program,GL_LINK_STATUS)==0)throw new IllegalStateException(glGetProgramInfoLog(program));
        glUseProgram(program);glUniform1i(glGetUniformLocation(program,"Sharp"),0);glUniform1i(glGetUniformLocation(program,"Blurred"),1);
        amountLocation=glGetUniformLocation(program,"Amount");dimLocation=glGetUniformLocation(program,"Dim");availableLocation=glGetUniformLocation(program,"Available");vao=glGenVertexArrays();
    }
    public static void clearWorldImage(){if(worldTexture!=0)glDeleteTextures(worldTexture);worldTexture=worldW=worldH=0;requested=false;}
    public static void close(){clearWorldImage();if(program!=0)glDeleteProgram(program);if(vao!=0)glDeleteVertexArrays(vao);program=vao=0;}
}

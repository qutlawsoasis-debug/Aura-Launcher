package net.auramc.glass.client.quickmenu.lyrics;

import com.google.gson.*;
import org.joml.Matrix4f;
import org.lwjgl.BufferUtils;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import javax.imageio.ImageIO;
import static org.lwjgl.opengl.GL33C.*;

public final class WorldLyricsRenderer {
    private static final int STRIDE=24, CAPACITY=2048;
    private static final FloatBuffer INSTANCES=BufferUtils.createFloatBuffer(CAPACITY*STRIDE);
    private static final FloatBuffer MATRIX=BufferUtils.createFloatBuffer(16);
    private static final Map<Integer,Glyph> GLYPHS=new HashMap<>();
    private static int program,vao,vbo,texture,count;
    private static boolean failed;
    public static long renderedGlyphs, frameNanos;
    private record Glyph(float advance,float left,float top,float right,float bottom,float u0,float v0,float u1,float v1) {}
    private static final String VERTEX="""
        #version 330 core
        layout(location=0) in vec4 CenterScale;
        layout(location=1) in vec4 Rect;
        layout(location=2) in vec4 Atlas;
        layout(location=3) in vec4 Color;
        layout(location=4) in vec4 RightRotation;
        layout(location=5) in vec4 Light;
        uniform mat4 ProjectionView;
        out vec2 uv; out vec4 color; out float light;
        void main(){
            vec2 corners[4]=vec2[4](vec2(0,0),vec2(0,1),vec2(1,0),vec2(1,1));
            vec2 corner=corners[gl_VertexID]; vec2 p=mix(Rect.xy,Rect.zw,corner)*CenterScale.w;
            float a=RightRotation.w; p=mat2(cos(a),-sin(a),sin(a),cos(a))*p;
            vec3 world=CenterScale.xyz+RightRotation.xyz*p.x+vec3(0,-p.y,0);
            gl_Position=ProjectionView*vec4(world,1);uv=mix(Atlas.xy,Atlas.zw,corner);color=Color;light=Light.x;
        }
        """;
    private static final String FRAGMENT="""
        #version 330 core
        uniform sampler2D Font;
        in vec2 uv;in vec4 color;in float light;out vec4 fragColor;
        float median(vec3 v){return max(min(v.r,v.g),min(max(v.r,v.g),v.b));}
        void main(){
            float distance=median(texture(Font,uv).rgb)-.5;
            float range=max(fwidth(distance),.0005);
            float fill=smoothstep(-range,range,distance);
            float outline=smoothstep(-range*3.5,-range,distance);
            float alpha=max(fill,outline*.75)*color.a;
            vec3 rgb=mix(vec3(.015,.025,.035),color.rgb*max(.75,light),fill);
            fragColor=vec4(rgb*alpha,alpha);
        }
        """;
    private WorldLyricsRenderer() {}
    public static boolean ready(){return program!=0;}
    public static void initialize(){
        if(program!=0||failed)return;
        int previousProgram=glGetInteger(GL_CURRENT_PROGRAM),previousVao=glGetInteger(GL_VERTEX_ARRAY_BINDING),previousBuffer=glGetInteger(GL_ARRAY_BUFFER_BINDING),previousTexture=glGetInteger(GL_TEXTURE_BINDING_2D),previousUnpack=glGetInteger(GL_UNPACK_ALIGNMENT),previousUnpackBuffer=glGetInteger(GL_PIXEL_UNPACK_BUFFER_BINDING),previousRow=glGetInteger(GL_UNPACK_ROW_LENGTH),previousSkipRows=glGetInteger(GL_UNPACK_SKIP_ROWS),previousSkipPixels=glGetInteger(GL_UNPACK_SKIP_PIXELS);
        try{
            try(var input=WorldLyricsRenderer.class.getResourceAsStream("/assets/aura-glass/msdf/onest.json")){
                var root=JsonParser.parseReader(new java.io.InputStreamReader(Objects.requireNonNull(input),StandardCharsets.UTF_8)).getAsJsonObject();
                float width=root.get("width").getAsFloat(),height=root.get("height").getAsFloat();
                var font=root.getAsJsonArray("fonts").get(0).getAsJsonObject();
                for(var entry:font.getAsJsonArray("glyphs")){
                    var g=entry.getAsJsonObject();int cp=g.get("unicode").getAsInt();
                    if(!g.has("planeBounds")){GLYPHS.put(cp,new Glyph(g.get("advance").getAsFloat(),0,0,0,0,0,0,0,0));continue;}
                    var p=g.getAsJsonObject("planeBounds");var a=g.getAsJsonObject("atlasBounds");
                    GLYPHS.put(cp,new Glyph(g.get("advance").getAsFloat(),p.get("left").getAsFloat(),p.get("top").getAsFloat(),p.get("right").getAsFloat(),p.get("bottom").getAsFloat(),a.get("left").getAsFloat()/width,a.get("top").getAsFloat()/height,a.get("right").getAsFloat()/width,a.get("bottom").getAsFloat()/height));
                }
            }
            try(var input=WorldLyricsRenderer.class.getResourceAsStream("/assets/aura-glass/msdf/onest.png")){
                var image=ImageIO.read(Objects.requireNonNull(input));var pixels=BufferUtils.createByteBuffer(image.getWidth()*image.getHeight()*4);
                for(int y=0;y<image.getHeight();y++)for(int x=0;x<image.getWidth();x++){int c=image.getRGB(x,y);pixels.put((byte)(c>>16)).put((byte)(c>>8)).put((byte)c).put((byte)255);}pixels.flip();
                texture=glGenTextures();glBindTexture(GL_TEXTURE_2D,texture);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MIN_FILTER,GL_LINEAR);glTexParameteri(GL_TEXTURE_2D,GL_TEXTURE_MAG_FILTER,GL_LINEAR);glBindBuffer(GL_PIXEL_UNPACK_BUFFER,0);glPixelStorei(GL_UNPACK_ALIGNMENT,1);glPixelStorei(GL_UNPACK_ROW_LENGTH,0);glPixelStorei(GL_UNPACK_SKIP_ROWS,0);glPixelStorei(GL_UNPACK_SKIP_PIXELS,0);glTexImage2D(GL_TEXTURE_2D,0,GL_RGBA8,image.getWidth(),image.getHeight(),0,GL_RGBA,GL_UNSIGNED_BYTE,pixels);
            }
            int vs=shader(GL_VERTEX_SHADER,VERTEX),fs=shader(GL_FRAGMENT_SHADER,FRAGMENT);program=glCreateProgram();glAttachShader(program,vs);glAttachShader(program,fs);glLinkProgram(program);glDeleteShader(vs);glDeleteShader(fs);
            if(glGetProgrami(program,GL_LINK_STATUS)==GL_FALSE)throw new IllegalStateException(glGetProgramInfoLog(program));
            vao=glGenVertexArrays();vbo=glGenBuffers();glBindVertexArray(vao);glBindBuffer(GL_ARRAY_BUFFER,vbo);
            for(int i=0;i<6;i++){glEnableVertexAttribArray(i);glVertexAttribPointer(i,4,GL_FLOAT,false,STRIDE*4,i*16L);glVertexAttribDivisor(i,1);}
        }catch(Exception error){failed=true;System.err.println("[AuraKaraoke] Renderer unavailable: "+error.getMessage());close();}
        finally{glUseProgram(previousProgram);glBindVertexArray(previousVao);glBindBuffer(GL_ARRAY_BUFFER,previousBuffer);glBindTexture(GL_TEXTURE_2D,previousTexture);glBindBuffer(GL_PIXEL_UNPACK_BUFFER,previousUnpackBuffer);glPixelStorei(GL_UNPACK_ALIGNMENT,previousUnpack);glPixelStorei(GL_UNPACK_ROW_LENGTH,previousRow);glPixelStorei(GL_UNPACK_SKIP_ROWS,previousSkipRows);glPixelStorei(GL_UNPACK_SKIP_PIXELS,previousSkipPixels);}
    }
    private static int shader(int kind,String text){int shader=glCreateShader(kind);glShaderSource(shader,text);glCompileShader(shader);if(glGetShaderi(shader,GL_COMPILE_STATUS)==GL_FALSE)throw new IllegalStateException(glGetShaderInfoLog(shader));return shader;}
    public static void begin(){INSTANCES.clear();count=0;}
    public static void word(KaraokeEngine.BodySnapshot body,double cameraX,double cameraY,double cameraZ,float size,float light,float interpolation){
        if(!ready()||body.alpha()<=0)return;
        String text=body.word().text();float width=0;for(int cp:text.codePoints().toArray()){var g=GLYPHS.get(cp);if(g!=null)width+=g.advance;}
        float x=-width*.5f;int letters=0;
        double px=body.previousPosition().x()+(body.position().x()-body.previousPosition().x())*interpolation-cameraX;
        double py=body.previousPosition().y()+(body.position().y()-body.previousPosition().y())*interpolation-cameraY;
        double pz=body.previousPosition().z()+(body.position().z()-body.previousPosition().z())*interpolation-cameraZ;
        double yaw=Math.toRadians(body.yawDegrees());float rx=(float)Math.cos(yaw),rz=(float)-Math.sin(yaw);
        for(int cp:text.codePoints().toArray()){
            var g=GLYPHS.get(cp);if(g==null)continue;
            if(letters++>=body.revealedLetters()||count>=CAPACITY)break;
            if(g.right>g.left){
                boolean accent=!body.sung();
                INSTANCES.put((float)px).put((float)py).put((float)pz).put((float)(size*.28*body.scale()));
                INSTANCES.put(x+g.left).put(g.top).put(x+g.right).put(g.bottom);
                INSTANCES.put(g.u0).put(g.v0).put(g.u1).put(g.v1);
                INSTANCES.put(accent?100/255f:1).put(accent?210/255f:1).put(1).put((float)body.alpha());
                INSTANCES.put(rx).put(0).put(rz).put((float)Math.toRadians(body.rotationDegrees()));
                INSTANCES.put(light).put(0).put(0).put(0);count++;
            }x+=g.advance;
        }
    }
    public static void draw(Matrix4f projectionView){
        if(count==0||!ready())return;
        long began=System.nanoTime();int oldProgram=glGetInteger(GL_CURRENT_PROGRAM),oldVao=glGetInteger(GL_VERTEX_ARRAY_BINDING),oldBuffer=glGetInteger(GL_ARRAY_BUFFER_BINDING),oldActive=glGetInteger(GL_ACTIVE_TEXTURE);glActiveTexture(GL_TEXTURE0);int oldTexture=glGetInteger(GL_TEXTURE_BINDING_2D),oldSampler=glGetInteger(GL_SAMPLER_BINDING),equation=glGetInteger(GL_BLEND_EQUATION_RGB),equationA=glGetInteger(GL_BLEND_EQUATION_ALPHA);boolean blend=glIsEnabled(GL_BLEND),cull=glIsEnabled(GL_CULL_FACE),depth=glIsEnabled(GL_DEPTH_TEST),depthMask=glGetBoolean(GL_DEPTH_WRITEMASK);int src=glGetInteger(GL_BLEND_SRC_RGB),dst=glGetInteger(GL_BLEND_DST_RGB),srcA=glGetInteger(GL_BLEND_SRC_ALPHA),dstA=glGetInteger(GL_BLEND_DST_ALPHA);
        try{
            glUseProgram(program);glBindVertexArray(vao);glBindBuffer(GL_ARRAY_BUFFER,vbo);INSTANCES.flip();glBufferData(GL_ARRAY_BUFFER,INSTANCES,GL_STREAM_DRAW);glUniformMatrix4fv(glGetUniformLocation(program,"ProjectionView"),false,projectionView.get(MATRIX));glUniform1i(glGetUniformLocation(program,"Font"),0);glBindTexture(GL_TEXTURE_2D,texture);
            glBindSampler(0,0);glEnable(GL_BLEND);glBlendEquation(GL_FUNC_ADD);glBlendFuncSeparate(GL_ONE,GL_ONE_MINUS_SRC_ALPHA,GL_ONE,GL_ONE_MINUS_SRC_ALPHA);glEnable(GL_DEPTH_TEST);glDisable(GL_CULL_FACE);glDepthMask(false);glDrawArraysInstanced(GL_TRIANGLE_STRIP,0,4,count);renderedGlyphs+=count;
        }finally{
            glDepthMask(depthMask);if(!blend)glDisable(GL_BLEND);if(cull)glEnable(GL_CULL_FACE);if(!depth)glDisable(GL_DEPTH_TEST);glBlendFuncSeparate(src,dst,srcA,dstA);glBlendEquationSeparate(equation,equationA);glBindSampler(0,oldSampler);glBindTexture(GL_TEXTURE_2D,oldTexture);glActiveTexture(oldActive);glUseProgram(oldProgram);glBindVertexArray(oldVao);glBindBuffer(GL_ARRAY_BUFFER,oldBuffer);frameNanos=System.nanoTime()-began;
        }
    }
    public static void close(){if(program!=0)glDeleteProgram(program);if(vao!=0)glDeleteVertexArrays(vao);if(vbo!=0)glDeleteBuffers(vbo);if(texture!=0)glDeleteTextures(texture);program=vao=vbo=texture=0;GLYPHS.clear();}
}
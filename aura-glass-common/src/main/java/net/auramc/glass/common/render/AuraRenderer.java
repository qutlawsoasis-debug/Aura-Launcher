package net.auramc.glass.common.render;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import org.joml.Matrix4f;
import org.lwjgl.BufferUtils;

import javax.imageio.ImageIO;
import java.awt.image.BufferedImage;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.ByteBuffer;
import java.nio.FloatBuffer;
import java.nio.charset.StandardCharsets;
import static org.lwjgl.opengl.GL33C.*;

/** Own SDF geometry and MSDF text share an ordered vertex stream. No Minecraft types. */
public final class AuraRenderer {
    private static final int STRIDE = 20;
    private static final int MAX_VERTICES = 16380;
    private static final FloatBuffer VERTICES = BufferUtils.createFloatBuffer(MAX_VERTICES * STRIDE);
    private static final FloatBuffer MODEL_BUFFER = BufferUtils.createFloatBuffer(16);
    private static final FloatBuffer PROJECTION_BUFFER = BufferUtils.createFloatBuffer(16);
    private static final Glyph[][] GLYPHS = new Glyph[4][65536];
    private static final java.util.Map<Long, Float> KERNING = new java.util.HashMap<>();
    private static final Matrix4f MODEL = new Matrix4f();
    private static final Matrix4f PROJECTION = new Matrix4f();
    private static final Matrix4f CLIP_CHECK = new Matrix4f();
    private static int program, vao, vbo, texture, modelLocation, projectionLocation, fontLocation;
    private static float atlasWidth, atlasHeight, distanceRange, ascender;
    private static boolean ready, failed;
    private static int count,batchDrawCalls;
    public static int batchDrawCalls(){return batchDrawCalls;}
    private static int contrastLocation, contrastSlotLocation, contrastSlot=-1;
    private static float glyphKind=1;

    private AuraRenderer() {}

    public static boolean isReady() { return ready; }

    public static synchronized void ensureFontMetricsLoaded() {
        if (GLYPHS[0]['A'] != null) return;
        try (InputStream in = resource("msdf/onest.json")) {
            loadFontMetrics(in);
        } catch (Throwable ignored) {
        }
    }

    /** Called by client initialization on the render thread, never by a draw method. */
    public static void initialize() {
        if (ready || failed) return;
        int previousVao = glGetInteger(GL_VERTEX_ARRAY_BINDING);
        int previousBuffer = glGetInteger(GL_ARRAY_BUFFER_BINDING);
        int previousActive = glGetInteger(GL_ACTIVE_TEXTURE);
        com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE0);
        int previousTexture = glGetInteger(GL_TEXTURE_BINDING_2D);
        int previousUnpack = glGetInteger(GL_UNPACK_ALIGNMENT);
        try {
            int vs = compile(GL_VERTEX_SHADER, "native.vert");
            int fs = compile(GL_FRAGMENT_SHADER, "native.frag");
            program = glCreateProgram();
            glAttachShader(program, vs);
            glAttachShader(program, fs);
            String[] attributes = {"Position", "Local", "Size", "Color", "Edge", "Shape", "UV"};
            for (int i = 0; i < attributes.length; i++) glBindAttribLocation(program, i, attributes[i]);
            glLinkProgram(program);
            glDeleteShader(vs);
            glDeleteShader(fs);
            if (glGetProgrami(program, GL_LINK_STATUS) == GL_FALSE) throw new IllegalStateException("native shader link: " + glGetProgramInfoLog(program));
            modelLocation = glGetUniformLocation(program, "Model");
            projectionLocation = glGetUniformLocation(program, "Projection");
            fontLocation = glGetUniformLocation(program, "Font");
            contrastLocation = glGetUniformLocation(program, "Contrast");
            contrastSlotLocation = glGetUniformLocation(program, "ContrastSlot");
            int previousProgram = glGetInteger(GL_CURRENT_PROGRAM);
            try {
                glUseProgram(program);
                glUniform1i(fontLocation, 0);
            } finally { glUseProgram(previousProgram); }
            vao = glGenVertexArrays();
            vbo = glGenBuffers();
            glBindVertexArray(vao);
            glBindBuffer(GL_ARRAY_BUFFER, vbo);
            glBufferData(GL_ARRAY_BUFFER, (long) MAX_VERTICES * STRIDE * Float.BYTES, GL_STREAM_DRAW);
            int offset = 0;
            int[] widths = {2, 2, 2, 4, 4, 4, 2};
            for (int i = 0; i < widths.length; i++) {
                glEnableVertexAttribArray(i);
                glVertexAttribPointer(i, widths[i], GL_FLOAT, false, STRIDE * Float.BYTES, (long) offset * Float.BYTES);
                offset += widths[i];
            }
            loadFont();
            ready = true;
        } catch (Exception e) {
            failed = true;
            GlassDiagnostics.rendererFailure("native renderer unavailable: " + e.getMessage());
            close();
        } finally {
            glBindVertexArray(previousVao);
            glBindBuffer(GL_ARRAY_BUFFER, previousBuffer);
            com.mojang.blaze3d.platform.GlStateManager._bindTexture(previousTexture);
            com.mojang.blaze3d.platform.GlStateManager._activeTexture(previousActive);
            glPixelStorei(GL_UNPACK_ALIGNMENT, previousUnpack);
        }
    }

    private static InputStream resource(String path) {
        InputStream stream = AuraRenderer.class.getResourceAsStream("/assets/aura-glass/" + path);
        if (stream == null) throw new IllegalStateException("Missing renderer resource " + path);
        return stream;
    }

    private static int compile(int type, String file) throws Exception {
        String source;
        try (InputStream in = resource("shaders/" + file)) { source = new String(in.readAllBytes(), StandardCharsets.UTF_8); }
        int shader = glCreateShader(type);
        glShaderSource(shader, source);
        glCompileShader(shader);
        if (glGetShaderi(shader, GL_COMPILE_STATUS) == GL_FALSE) {
            String error = glGetShaderInfoLog(shader);
            glDeleteShader(shader);
            throw new IllegalStateException(file + " shader compile: " + error);
        }
        return shader;
    }

    public static void loadFontMetrics(InputStream jsonStream) throws Exception {
        JsonObject root;
        try (InputStreamReader in = new InputStreamReader(jsonStream, StandardCharsets.UTF_8)) {
            root = JsonParser.parseReader(in).getAsJsonObject();
        }
        KERNING.clear();
        if (root.has("fonts")) {
            for (var fontElem : root.getAsJsonArray("fonts")) {
                JsonObject fontObj = fontElem.getAsJsonObject();
                if (fontObj.has("kerning")) {
                    for (var ke : fontObj.getAsJsonArray("kerning")) {
                        JsonObject ko = ke.getAsJsonObject();
                        int c1 = ko.get("first").getAsInt();
                        int c2 = ko.get("second").getAsInt();
                        float amt = ko.get("amount").getAsFloat();
                        long k = (((long) c1) << 32) | (((long) c2) & 0xFFFFFFFFL);
                        KERNING.put(k, amt);
                    }
                    break;
                }
            }
        }
        atlasWidth = root.has("width")?root.get("width").getAsFloat():1024;
        atlasHeight = root.has("height")?root.get("height").getAsFloat():1024;
        for (int page = 0; page < 4; page++) {
        JsonObject font = root.getAsJsonArray("fonts").get(page).getAsJsonObject();
        JsonObject atlas = font.getAsJsonObject("atlas");
        distanceRange = atlas.get("distanceRange").getAsFloat();
        ascender = font.getAsJsonObject("metrics").get("ascender").getAsFloat();
        for (var item : font.getAsJsonArray("glyphs")) {
            JsonObject g = item.getAsJsonObject();
            int cp = g.get("unicode").getAsInt();
            if (cp >= GLYPHS[page].length) continue;
            Glyph glyph = new Glyph();
            glyph.advance = g.get("advance").getAsFloat();
            if (g.has("planeBounds")) {
                JsonObject p = g.getAsJsonObject("planeBounds"), a = g.getAsJsonObject("atlasBounds");
                glyph.left = p.get("left").getAsFloat(); glyph.top = p.get("top").getAsFloat();
                glyph.right = p.get("right").getAsFloat(); glyph.bottom = p.get("bottom").getAsFloat();
                float offsetX = page % 2 * atlas.get("width").getAsFloat(), offsetY = page / 2 * atlas.get("height").getAsFloat();
                glyph.u0 = (offsetX + a.get("left").getAsFloat()) / atlasWidth;
                glyph.v0 = (offsetY + a.get("top").getAsFloat()) / atlasHeight;
                glyph.u1 = (offsetX + a.get("right").getAsFloat()) / atlasWidth;
                glyph.v1 = (offsetY + a.get("bottom").getAsFloat()) / atlasHeight;
                glyph.visible = true;
            }
            GLYPHS[page][cp] = glyph;
        }
        }
    }

    private static void loadFont() throws Exception {
        try (InputStream in = resource("msdf/onest.json")) {
            loadFontMetrics(in);
        }
        BufferedImage image;
        try (InputStream in = resource("msdf/onest.png")) { image = ImageIO.read(in); }
        if (image == null) throw new IllegalStateException("Invalid MSDF image");
        ByteBuffer pixels = BufferUtils.createByteBuffer(image.getWidth() * image.getHeight() * 4);
        for (int y = 0; y < image.getHeight(); y++) for (int x = 0; x < image.getWidth(); x++) {
            int c = image.getRGB(x, y);
            pixels.put((byte) (c >> 16)).put((byte) (c >> 8)).put((byte) c).put((byte) 255);
        }
        pixels.flip();
        texture = glGenTextures();
        com.mojang.blaze3d.platform.GlStateManager._bindTexture(texture);
        glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA8, image.getWidth(), image.getHeight(), 0, GL_RGBA, GL_UNSIGNED_BYTE, pixels);
    }

    public static boolean begin(Matrix4f local, Matrix4f projection, Matrix4f view) {
        if (!ready) return false;
        // Minecraft's global GUI view moves z=0 inside the projection's near/far planes.
        GuiMatrices.composeModel(view, local, MODEL);
        PROJECTION.set(projection);
        VERTICES.clear(); count = 0; contrastSlot=-1; batchDrawCalls=0;
        if (!GuiMatrices.visibleDepth(PROJECTION, MODEL, CLIP_CHECK)) {
            GlassDiagnostics.rendererFailure("GUI depth outside clip volume; native renderer disabled");
            failed = true;
            close();
            return false;
        }
        return true;
    }

    public static void rect(float x, float y, float w, float h, float radius, float border, int fill, int edge) {
        if (!ready || w <= 0 || h <= 0) return;
        quad(x, y, w, h, Math.max(0, Math.min(radius, Math.min(w, h) * .5f)), border, 0, fill, edge, 0, 0, 0, 0);
    }

    public static void shadow(float x, float y, float w, float h, float radius, int color) {
        if (!ready) return;
        if (count + 6 > MAX_VERTICES) flush();
        float pad = 18;
        vertex(x - pad, y - pad, -pad, -pad, w, h, radius, 0, -1, color, 0, 0, 0);
        vertex(x + w + pad, y - pad, w + pad, -pad, w, h, radius, 0, -1, color, 0, 0, 0);
        vertex(x + w + pad, y + h + pad, w + pad, h + pad, w, h, radius, 0, -1, color, 0, 0, 0);
        vertex(x - pad, y - pad, -pad, -pad, w, h, radius, 0, -1, color, 0, 0, 0);
        vertex(x + w + pad, y + h + pad, w + pad, h + pad, w, h, radius, 0, -1, color, 0, 0, 0);
        vertex(x - pad, y + h + pad, -pad, h + pad, w, h, radius, 0, -1, color, 0, 0, 0);
    }

    public static boolean hasGlyph(int codePoint) { return ready && codePoint>=0 && codePoint<GLYPHS[0].length && GLYPHS[0][codePoint]!=null; }
    public static float glyphWidth(int codePoint,float size){return hasGlyph(codePoint)?GLYPHS[0][codePoint].advance*size:0;}
    public static float glyphWidth(int codePoint,float size,int weight){return hasGlyph(codePoint)?GLYPHS[weightIndex(weight)][codePoint].advance*size:0;}
    public static void glyph(int codePoint,float x,float y,float size,int color,int weight){
        if(!hasGlyph(codePoint))return;
        Glyph g=GLYPHS[weightIndex(weight)][codePoint];
        if(g.visible)quad(x+g.left*size,y+(-ascender+g.top)*size,(g.right-g.left)*size,(g.bottom-g.top)*size,0,0,glyphKind,color,0,g.u0,g.v0,g.u1,g.v1);
    }
    public static void adaptiveGlyph(int slot,int point,float x,float y,float size,int color,int weight){
        contrastSlot=slot;glyphKind=1.2f;glyph(point,x+.65f,y+.65f,size,color,weight);
        glyphKind=1;glyph(point,x,y,size,color,weight);
    }

    public static boolean supports(String text) {
        if (!ready) return false;
        for (int i = 0; i < text.length(); i++) if (GLYPHS[0][text.charAt(i)] == null) return false;
        return true;
    }

    public static float textWidth(String text, float size) {
        return textWidth(text, size, 400);
    }

    private static int weightIndex(int weight) {
        return Math.max(0, Math.min(3, (weight - 400) / 100));
    }

    public static float textWidth(String text, float size, int weight) {
        if (text == null || text.isEmpty()) return 0;
        ensureFontMetricsLoaded();
        float width = 0;
        int w = weightIndex(weight);
        for (int i = 0; i < text.length(); i++) {
            char ch = text.charAt(i);
            Glyph g = GLYPHS[w][ch];
            if (g != null) {
                width += g.advance * size;
                if (i > 0) {
                    width -= 0.01f * size; // Inter-letter spacing -0.01 em
                    width += getKerning(text.charAt(i - 1), ch) * size; // Font kerning
                }
            }
        }
        return width;
    }

    public static void text(String text, float x, float y, float size, int color) {
        text(text, x, y, size, color, 400);
    }

    public static void text(String text, float x, float y, float size, int color, int weight) {
        if (!ready) return;
        float pen = x;
        for (int i = 0; i < text.length(); i++) {
            Glyph g = GLYPHS[weightIndex(weight)][text.charAt(i)];
            if (g == null) continue;
            if (g.visible) quad(pen + g.left * size, y + (-ascender + g.top) * size,
                    (g.right - g.left) * size, (g.bottom - g.top) * size,
                    0, 0, glyphKind, color, 0, g.u0, g.v0, g.u1, g.v1);
            pen += g.advance * size;
        }
    }

    public static void adaptiveText(int slot, String text, float x, float y, float size, int color) {
        contrastSlot=slot;
        if(slot>=0) { glyphKind=1.2f; text(text,x+.65f,y+.65f,size,color); }
        glyphKind=1; text(text,x,y,size,color);
    }

    private static void quad(float x, float y, float w, float h, float radius, float border, float kind,
                             int fill, int edge, float u0, float v0, float u1, float v1) {
        if (count + 6 > MAX_VERTICES) flush();
        vertex(x, y, 0, 0, w, h, radius, border, kind, fill, edge, u0, v0);
        vertex(x + w, y, w, 0, w, h, radius, border, kind, fill, edge, u1, v0);
        vertex(x + w, y + h, w, h, w, h, radius, border, kind, fill, edge, u1, v1);
        vertex(x, y, 0, 0, w, h, radius, border, kind, fill, edge, u0, v0);
        vertex(x + w, y + h, w, h, w, h, radius, border, kind, fill, edge, u1, v1);
        vertex(x, y + h, 0, h, w, h, radius, border, kind, fill, edge, u0, v1);
    }

    private static void color(int c) {
        VERTICES.put((c >> 16 & 255) / 255f).put((c >> 8 & 255) / 255f).put((c & 255) / 255f).put((c >>> 24) / 255f);
    }

    private static void vertex(float x, float y, float lx, float ly, float w, float h, float r, float b, float kind, int fill, int edge, float u, float v) {
        VERTICES.put(x).put(y).put(lx).put(ly).put(w).put(h);
        color(fill); color(edge);
        VERTICES.put(r).put(b).put(kind).put(distanceRange).put(u).put(v);
        count++;
    }

    public static void flush() {
        if (!ready || count == 0) return;
        try {
            glEnable(GL_BLEND); glDisable(GL_DEPTH_TEST); glDisable(GL_CULL_FACE);
            glBlendEquationSeparate(GL_FUNC_ADD, GL_FUNC_ADD);
            glBlendFuncSeparate(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA, GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
            glUseProgram(program);
            glUniformMatrix4fv(modelLocation, false, MODEL.get(MODEL_BUFFER));
            glUniformMatrix4fv(projectionLocation, false, PROJECTION.get(PROJECTION_BUFFER));
            glUniform1i(fontLocation, 0);
            glUniform1i(contrastLocation,1); glUniform1i(contrastSlotLocation,contrastSlot);
            com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE1);
            GuiRenderState.clearSampler(1); com.mojang.blaze3d.platform.GlStateManager._bindTexture(AdaptiveText.texture());
            com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE0);
            com.mojang.blaze3d.platform.GlStateManager._bindTexture(texture);
            glBindVertexArray(vao); glBindBuffer(GL_ARRAY_BUFFER, vbo);
            VERTICES.flip();
            glBufferSubData(GL_ARRAY_BUFFER, 0L, VERTICES);
            glDrawArrays(GL_TRIANGLES, 0, count);batchDrawCalls++;
        } finally {
            var client = net.minecraft.client.MinecraftClient.getInstance();
            var framebuffer = client.getFramebuffer();
            GuiRenderState.restore(framebuffer.fbo, framebuffer.textureWidth, framebuffer.textureHeight);
            VERTICES.clear(); count = 0; contrastSlot=-1;
        }
    }

    public static void close() {
        ready = false;
        BlurService.get().shutdown();
        GlassDiagnostics.shutdown();
        if (texture != 0) glDeleteTextures(texture);
        if (vbo != 0) glDeleteBuffers(vbo);
        if (vao != 0) glDeleteVertexArrays(vao);
        if (program != 0) glDeleteProgram(program);
        texture = vbo = vao = program = 0;
    }

    public static int fontTexture() { return texture; }
    public static float distanceRange() { return distanceRange; }
    public static float fontAscender() { return ascender; }
    public static float atlasWidth() { return atlasWidth; }
    public static float atlasHeight() { return atlasHeight; }
    public static float getKerning(int cp1, int cp2) {
        if (cp1 <= 0 || cp2 <= 0) return 0.0f;
        ensureFontMetricsLoaded();
        if (KERNING.isEmpty()) return 0.0f;
        long key = (((long) cp1) << 32) | (((long) cp2) & 0xFFFFFFFFL);
        Float k = KERNING.get(key);
        return k != null ? k : 0.0f;
    }

    public static Glyph getGlyph(int weight, int codePoint) {
        ensureFontMetricsLoaded();
        int w = weightIndex(weight);
        if (codePoint >= 0 && codePoint < GLYPHS[w].length) {
            return GLYPHS[w][codePoint];
        }
        return null;
    }

    public static final class Glyph {
        public float advance, left, right, top, bottom, u0, v0, u1, v1;
        public boolean visible;
    }
}

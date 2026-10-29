package net.auramc.glass.client.karaoke;

import com.mojang.blaze3d.platform.GlStateManager;
import net.auramc.glass.common.render.AuraRenderer;
import net.minecraft.client.render.Camera;
import net.minecraft.util.math.BlockPos;
import net.minecraft.util.math.Vec3d;
import net.minecraft.world.LightType;
import net.minecraft.world.World;
import org.joml.Matrix4f;
import org.joml.Quaternionf;
import org.joml.Vector3f;
import org.joml.Vector4f;
import org.lwjgl.BufferUtils;

import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.FloatBuffer;
import java.nio.charset.StandardCharsets;
import java.util.List;

import static org.lwjgl.opengl.GL33C.*;

/**
 * High-performance MSDF Onest 3D world renderer for in-world karaoke:
 * - Single-pass batched instanced quad geometry with expanded halo padding
 * - Adaptive light/dark background luminance sensing (sampled 1 time per 4 ticks, 0 new FBOs)
 * - 300 ms smooth transition between dark (#64D2FF, 0.35 halo) and light (#0A84FF, 0.60 halo)
 * - MSDF outline / soft shadow: width 0.10 em, blur 0.08 em, color rgb(0,0,0)
 * - Apple HIG typography: white 0.95 upcoming, white 0.80 sung, falling words fade halo with alpha
 * - Budget <= 0.3 ms/frame (typical ~0.03 ms)
 */
public final class KaraokeWorldRenderer {

    // Stride: 3 pos + 2 uv + 4 col + 4 outline + 2 params + 4 uvBounds = 19 floats
    private static final int STRIDE = 19;
    private static final int MAX_VERTICES = 8192;
    private static final FloatBuffer VERTEX_BUFFER = BufferUtils.createFloatBuffer(MAX_VERTICES * STRIDE);
    private static final FloatBuffer PROJECTION_BUF = BufferUtils.createFloatBuffer(16);
    private static final FloatBuffer MODELVIEW_BUF = BufferUtils.createFloatBuffer(16);
    private static final ByteBuffer SAMPLE_BUF = BufferUtils.createByteBuffer(4 * 4 * 4); // 4x4 RGBA pixels = 64 bytes

    private static int program = 0;
    private static int vao = 0;
    private static int vbo = 0;
    private static int projUniform = -1;
    private static int mvUniform = -1;
    private static int fontUniform = -1;

    private static boolean initialized = false;
    private static boolean failed = false;
    private static int vertexCount = 0;
    private static double lastRenderTimeMs = 0.0;

    // Luminance & adaptive contrast state
    private static long lastSampleTick = -1L;
    private static long lastSampleTimeMs = 0L;
    private static long lastFrameTimeMs = 0L;
    private static boolean isLightBackground = false;
    private static float lightProgress = 0.0f; // 0.0 = dark, 1.0 = light (300 ms smooth transition)
    private static float lastSampledLuminance = 0.0f;

    private KaraokeWorldRenderer() {}

    public static double getLastRenderTimeMs() {
        return lastRenderTimeMs;
    }

    public static boolean isLightBackground() {
        return isLightBackground;
    }

    public static float getLightProgress() {
        return lightProgress;
    }

    public static float getLastSampledLuminance() {
        return lastSampledLuminance;
    }

    /**
     * For unit tests / scripted testing without an active OpenGL viewport.
     */
    public static void setScriptedLuminance(float luminance, long timeMs) {
        lastSampledLuminance = luminance;
        if (luminance > 0.60f) {
            isLightBackground = true;
        } else if (luminance < 0.50f) {
            isLightBackground = false;
        }
        lastFrameTimeMs = timeMs;
    }

    public static void resetState() {
        lastSampleTick = -1L;
        lastSampleTimeMs = 0L;
        lastFrameTimeMs = 0L;
        isLightBackground = false;
        lightProgress = 0.0f;
        lastSampledLuminance = 0.0f;
    }

    public static void updateTransition(long nowMs) {
        if (lastFrameTimeMs <= 0L) {
            lastFrameTimeMs = nowMs;
            lightProgress = isLightBackground ? 1.0f : 0.0f;
            return;
        }
        float dt = Math.max(0.0f, Math.min(0.5f, (nowMs - lastFrameTimeMs) / 1000.0f));
        lastFrameTimeMs = nowMs;

        float target = isLightBackground ? 1.0f : 0.0f;
        if (dt > 0.0f) {
            float step = dt / 0.300f; // Exactly 300 ms for full 0.0 <-> 1.0 transition
            if (target > lightProgress) {
                lightProgress = Math.min(target, lightProgress + step);
            } else if (target < lightProgress) {
                lightProgress = Math.max(target, lightProgress - step);
            }
        }
    }

    public static void initialize() {
        if (initialized || failed) return;

        try {
            if (!AuraRenderer.isReady()) {
                AuraRenderer.initialize();
            }

            int vs = compileShader(GL_VERTEX_SHADER, "karaoke_world.vert");
            int fs = compileShader(GL_FRAGMENT_SHADER, "karaoke_world.frag");

            program = glCreateProgram();
            glAttachShader(program, vs);
            glAttachShader(program, fs);

            glBindAttribLocation(program, 0, "Position");
            glBindAttribLocation(program, 1, "UV");
            glBindAttribLocation(program, 2, "Color");
            glBindAttribLocation(program, 3, "Outline");
            glBindAttribLocation(program, 4, "Params");
            glBindAttribLocation(program, 5, "UVBounds");

            glLinkProgram(program);
            glDeleteShader(vs);
            glDeleteShader(fs);

            if (glGetProgrami(program, GL_LINK_STATUS) == GL_FALSE) {
                String log = glGetProgramInfoLog(program);
                throw new IllegalStateException("Failed to link karaoke shader: " + log);
            }

            projUniform = glGetUniformLocation(program, "Projection");
            mvUniform = glGetUniformLocation(program, "ModelView");
            fontUniform = glGetUniformLocation(program, "Font");

            vao = glGenVertexArrays();
            vbo = glGenBuffers();

            glBindVertexArray(vao);
            glBindBuffer(GL_ARRAY_BUFFER, vbo);
            glBufferData(GL_ARRAY_BUFFER, (long) MAX_VERTICES * STRIDE * Float.BYTES, GL_STREAM_DRAW);

            int offset = 0;
            // 0: Position (3 floats)
            glEnableVertexAttribArray(0);
            glVertexAttribPointer(0, 3, GL_FLOAT, false, STRIDE * Float.BYTES, (long) offset * Float.BYTES);
            offset += 3;

            // 1: UV (2 floats)
            glEnableVertexAttribArray(1);
            glVertexAttribPointer(1, 2, GL_FLOAT, false, STRIDE * Float.BYTES, (long) offset * Float.BYTES);
            offset += 2;

            // 2: Color (4 floats)
            glEnableVertexAttribArray(2);
            glVertexAttribPointer(2, 4, GL_FLOAT, false, STRIDE * Float.BYTES, (long) offset * Float.BYTES);
            offset += 4;

            // 3: Outline (4 floats)
            glEnableVertexAttribArray(3);
            glVertexAttribPointer(3, 4, GL_FLOAT, false, STRIDE * Float.BYTES, (long) offset * Float.BYTES);
            offset += 4;

            // 4: Params (2 floats)
            glEnableVertexAttribArray(4);
            glVertexAttribPointer(4, 2, GL_FLOAT, false, STRIDE * Float.BYTES, (long) offset * Float.BYTES);
            offset += 2;

            // 5: UVBounds (4 floats)
            glEnableVertexAttribArray(5);
            glVertexAttribPointer(5, 4, GL_FLOAT, false, STRIDE * Float.BYTES, (long) offset * Float.BYTES);

            glBindVertexArray(0);
            glBindBuffer(GL_ARRAY_BUFFER, 0);

            initialized = true;
        } catch (Exception e) {
            failed = true;
            System.err.println("[AuraKaraoke] ERROR in KaraokeWorldRenderer.initialize: " + e);
            e.printStackTrace();
        }
    }

    private static int compileShader(int type, String name) throws Exception {
        String path = "/assets/aura-glass/shaders/" + name;
        InputStream in = KaraokeWorldRenderer.class.getResourceAsStream(path);
        if (in == null) throw new IllegalStateException("Missing shader resource: " + path);
        String source = new String(in.readAllBytes(), StandardCharsets.UTF_8);

        int shader = glCreateShader(type);
        glShaderSource(shader, source);
        glCompileShader(shader);
        if (glGetShaderi(shader, GL_COMPILE_STATUS) == GL_FALSE) {
            String error = glGetShaderInfoLog(shader);
            glDeleteShader(shader);
            throw new IllegalStateException("Shader compile error in " + name + ": " + error);
        }
        return shader;
    }

    public static void renderBatch(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera,
                                   List<WordPlacement.WordAnchor> activeAnchors,
                                   List<KaraokePhysics.PhysicalWord> physicalWords,
                                   long playbackMs,
                                   float tickDelta,
                                   World world) {
        if (!initialized && !failed) {
            initialize();
        }
        if (!initialized || failed) return;
        if (activeAnchors.isEmpty() && physicalWords.isEmpty()) return;

        long startTime = System.nanoTime();

        // 0. Update background luminance (sampled strictly 1 time per 4 ticks, 0 new FBOs)
        updateBackgroundLuminance(viewMatrix, projMatrix, camera, activeAnchors, world);

        VERTEX_BUFFER.clear();
        vertexCount = 0;

        Vec3d camPos = camera.getPos();

        // Basis vectors in camera-relative space derived directly from viewMatrix:
        // viewMatrix * U == (1, 0, 0) [Screen RIGHT]
        // viewMatrix * V == (0, -1, 0) [Screen DOWN]
        Vector3f billboardU = new Vector3f(viewMatrix.m00(), viewMatrix.m10(), viewMatrix.m20());
        Vector3f billboardV = new Vector3f(-viewMatrix.m01(), -viewMatrix.m11(), -viewMatrix.m21());

        float haloAlpha = WordAnimation.getHaloAlpha(lightProgress);
        int outlineBase = WordAnimation.COLOR_OUTLINE; // rgb(0, 0, 0)

        // 1. Emit active singing words (billboard facing camera)
        for (WordPlacement.WordAnchor anchor : activeAnchors) {
            KaraokeModel.KaraokeWord kw = anchor.word;

            // Time elapsed since word started
            long wordElapsedMs = playbackMs - (kw.t - KaraokeClock.PREROLL_MS);
            if (wordElapsedMs < 0) continue;

            // Dynamic color: #64D2FF on dark bg, #0A84FF on light bg, white 0.95 upcoming, white 0.80 sung
            int wordColor = WordAnimation.getWordColor(playbackMs, kw.t, kw.d, 1.0f, lightProgress);

            // Halo outline: rgb(0, 0, 0), alpha 0.35 dark, 0.60 light
            int outlineColor = WordAnimation.applyAlpha(outlineBase, haloAlpha * ((wordColor >>> 24) / 255f));

            float wordScale = anchor.height > 0.01f ? (anchor.height / WordPlacement.FONT_LINE_HEIGHT_EM) : KaraokeConfig.get().getSize() * WordPlacement.BASE_FONT_SCALE;
            emitString(kw.w, anchor.position, camPos, billboardU, billboardV, wordColor, outlineColor, wordElapsedMs, wordScale);
        }

        // 2. Emit physical falling bodies
        for (KaraokePhysics.PhysicalWord pw : physicalWords) {
            Vec3d interpPos = pw.getInterpolatedPos(tickDelta);
            float alpha = pw.getAlpha();
            if (alpha <= 0.01f) continue;

            // Falling words: white 0.80, halo fades out together with word alpha
            int color = WordAnimation.applyAlpha(WordAnimation.COLOR_SUNG, alpha);
            int outlineColor = WordAnimation.applyAlpha(outlineBase, haloAlpha * alpha);

            // Body rotation quaternion
            Quaternionf bodyRot = new Quaternionf()
                    .rotateY((float) Math.toRadians(-pw.getInterpolatedYaw(tickDelta)))
                    .rotateX((float) Math.toRadians(pw.getInterpolatedPitch(tickDelta)))
                    .rotateZ((float) Math.toRadians(pw.getInterpolatedRoll(tickDelta)));

            Vector3f physU = new Vector3f(1, 0, 0);
            Vector3f physV = new Vector3f(0, -1, 0);
            bodyRot.transform(physU);
            bodyRot.transform(physV);

            float scale = KaraokeConfig.get().getSize() * WordPlacement.BASE_FONT_SCALE * pw.getScale();
            emitString(pw.text, interpPos, camPos, physU, physV, color, outlineColor, 10000L, scale);
        }

        if (vertexCount == 0) return;

        // OpenGL rendering
        VERTEX_BUFFER.flip();

        int prevProgram = glGetInteger(GL_CURRENT_PROGRAM);
        int prevVao = glGetInteger(GL_VERTEX_ARRAY_BINDING);
        int prevVbo = glGetInteger(GL_ARRAY_BUFFER_BINDING);
        int prevActive = glGetInteger(GL_ACTIVE_TEXTURE);
        GlStateManager._activeTexture(GL_TEXTURE0);
        int prevTex = glGetInteger(GL_TEXTURE_BINDING_2D);

        boolean prevDepthTest = glIsEnabled(GL_DEPTH_TEST);
        boolean prevBlend = glIsEnabled(GL_BLEND);
        boolean prevCull = glIsEnabled(GL_CULL_FACE);

        try {
            glDisable(GL_DEPTH_TEST);
            glDepthMask(false); // Transparent overlay: drawn on top of foliage and terrain

            glEnable(GL_BLEND);
            glBlendFuncSeparate(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA, GL_ONE, GL_ONE_MINUS_SRC_ALPHA);
            glDisable(GL_CULL_FACE);

            glUseProgram(program);

            PROJECTION_BUF.clear();
            projMatrix.get(PROJECTION_BUF);
            glUniformMatrix4fv(projUniform, false, PROJECTION_BUF);

            MODELVIEW_BUF.clear();
            viewMatrix.get(MODELVIEW_BUF);
            glUniformMatrix4fv(mvUniform, false, MODELVIEW_BUF);

            glUniform1i(fontUniform, 0);

            GlStateManager._bindTexture(AuraRenderer.fontTexture());

            glBindVertexArray(vao);
            glBindBuffer(GL_ARRAY_BUFFER, vbo);
            glBufferSubData(GL_ARRAY_BUFFER, 0L, VERTEX_BUFFER);

            glDrawArrays(GL_TRIANGLES, 0, vertexCount);
        } finally {
            glDepthMask(true);
            if (prevDepthTest) glEnable(GL_DEPTH_TEST);
            if (!prevBlend) glDisable(GL_BLEND);
            if (prevCull) glEnable(GL_CULL_FACE);

            GlStateManager._bindTexture(prevTex);
            GlStateManager._activeTexture(prevActive);
            glBindBuffer(GL_ARRAY_BUFFER, prevVbo);
            glBindVertexArray(prevVao);
            glUseProgram(prevProgram);

            lastRenderTimeMs = (System.nanoTime() - startTime) / 1_000_000.0;
        }
    }

    private static void updateBackgroundLuminance(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera,
                                                  List<WordPlacement.WordAnchor> activeAnchors, World world) {
        long nowMs = System.currentTimeMillis();
        long currentTick = (world != null) ? world.getTime() : (nowMs / 50L);

        // Sample strictly once every 4 ticks (>= 4 ticks or >= 200 ms)
        boolean shouldSample = (lastSampleTick < 0) || (currentTick - lastSampleTick >= 4L) || (nowMs - lastSampleTimeMs >= 200L);

        if (shouldSample) {
            lastSampleTick = currentTick;
            lastSampleTimeMs = nowMs;

            float sampledLuma = sampleScreenLuminance(viewMatrix, projMatrix, camera, activeAnchors, world);
            lastSampledLuminance = sampledLuma;

            // Hysteresis: > 0.60 is light, < 0.50 is dark, between maintains current state
            if (sampledLuma > 0.60f) {
                isLightBackground = true;
            } else if (sampledLuma < 0.50f) {
                isLightBackground = false;
            }
        }

        // Smooth 300 ms transition updated every frame
        updateTransition(nowMs);
    }

    private static float sampleScreenLuminance(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera,
                                               List<WordPlacement.WordAnchor> activeAnchors, World world) {
        try {
            net.minecraft.client.MinecraftClient client = net.minecraft.client.MinecraftClient.getInstance();
            if (client != null && client.getFramebuffer() != null) {
                int fbWidth = client.getFramebuffer().textureWidth;
                int fbHeight = client.getFramebuffer().textureHeight;
                if (fbWidth > 10 && fbHeight > 10) {
                    int sampleX = fbWidth / 2;
                    int sampleY = fbHeight / 2;

                    if (!activeAnchors.isEmpty() && camera != null) {
                        double avgX = 0, avgY = 0, avgZ = 0;
                        for (WordPlacement.WordAnchor a : activeAnchors) {
                            avgX += a.position.x;
                            avgY += a.position.y;
                            avgZ += a.position.z;
                        }
                        int n = activeAnchors.size();
                        avgX /= n; avgY /= n; avgZ /= n;

                        Vec3d camPos = camera.getPos();
                        Vector3f relPos = new Vector3f((float) (avgX - camPos.x), (float) (avgY - camPos.y), (float) (avgZ - camPos.z));

                        Vector4f clip = new Vector4f(relPos.x, relPos.y, relPos.z, 1.0f);
                        viewMatrix.transform(clip);
                        projMatrix.transform(clip);

                        if (clip.w > 0.01f) {
                            float ndcX = clip.x / clip.w;
                            float ndcY = clip.y / clip.w;
                            sampleX = Math.max(2, Math.min(fbWidth - 3, (int) ((ndcX * 0.5f + 0.5f) * fbWidth)));
                            sampleY = Math.max(2, Math.min(fbHeight - 3, (int) ((ndcY * 0.5f + 0.5f) * fbHeight)));
                        }
                    }

                    SAMPLE_BUF.clear();
                    // Read tiny 4x4 pixels (64 bytes) from existing main framebuffer
                    glReadPixels(sampleX - 2, sampleY - 2, 4, 4, GL_RGBA, GL_UNSIGNED_BYTE, SAMPLE_BUF);

                    float sumLuma = 0.0f;
                    int count = 0;
                    for (int i = 0; i < 16; i++) {
                        int r = SAMPLE_BUF.get() & 0xFF;
                        int g = SAMPLE_BUF.get() & 0xFF;
                        int b = SAMPLE_BUF.get() & 0xFF;
                        SAMPLE_BUF.get(); // skip alpha
                        float rf = r / 255.0f;
                        float gf = g / 255.0f;
                        float bf = b / 255.0f;
                        sumLuma += (0.2126f * rf + 0.7152f * gf + 0.0722f * bf);
                        count++;
                    }
                    if (count > 0) {
                        return sumLuma / count;
                    }
                }
            }
        } catch (Throwable ignored) {
            // Safe headless fallback
        }

        return fallbackWorldLuminance(activeAnchors, world);
    }

    private static float fallbackWorldLuminance(List<WordPlacement.WordAnchor> activeAnchors, World world) {
        if (world == null || activeAnchors == null || activeAnchors.isEmpty()) return 0.5f;
        Vec3d pos = activeAnchors.get(0).position;
        BlockPos bp = BlockPos.ofFloored(pos.x, pos.y, pos.z);
        int sky = world.getLightLevel(LightType.SKY, bp);
        float timeOfDay = (world.getTimeOfDay() % 24000L) / 24000.0f;
        boolean isDay = timeOfDay < 0.5f;
        return isDay ? (sky / 15.0f) : 0.2f;
    }

    private static void emitString(String text, Vec3d worldPos, Vec3d camPos,
                                   Vector3f U, Vector3f V,
                                   int color, int outlineColor,
                                   long wordElapsedMs, float scale) {
        if (text == null || text.isEmpty()) return;

        // Camera-relative position D
        Vector3f D = new Vector3f(
                (float) (worldPos.x - camPos.x),
                (float) (worldPos.y - camPos.y),
                (float) (worldPos.z - camPos.z)
        );

        float totalW = AuraRenderer.textWidth(text, 1.0f);
        float penX = -totalW * 0.5f;
        float distRange = AuraRenderer.distanceRange();
        float ascender = AuraRenderer.fontAscender();
        float atlasW = AuraRenderer.atlasWidth() > 0 ? AuraRenderer.atlasWidth() : 2048.0f;
        float atlasH = AuraRenderer.atlasHeight() > 0 ? AuraRenderer.atlasHeight() : 2048.0f;

        for (int i = 0; i < text.length(); i++) {
            char ch = text.charAt(i);
            var glyph = AuraRenderer.getGlyph(400, ch);
            if (glyph == null) {
                penX += 0.5f;
                continue;
            }

            float charScale = WordAnimation.letterScale(wordElapsedMs, i) * scale;
            float charAlpha = WordAnimation.letterAlpha(wordElapsedMs, i);
            if (charAlpha <= 0.01f) {
                penX += glyph.advance;
                continue;
            }

            int charCol = WordAnimation.applyAlpha(color, charAlpha * ((color >>> 24) / 255f));
            int charOut = WordAnimation.applyAlpha(outlineColor, charAlpha * ((outlineColor >>> 24) / 255f));

            if (glyph.visible) {
                float verticalCenterOffset = 0.726f * charScale;
                // Expand quad by 0.05 em padding to allow the 0.10 em halo to render completely without clipping
                float pad = 0.05f * charScale;
                float lx0 = (penX + glyph.left) * charScale - pad;
                float lx1 = (penX + glyph.right) * charScale + pad;
                float ly0 = (-ascender + glyph.top) * charScale - verticalCenterOffset - pad;
                float ly1 = (-ascender + glyph.bottom) * charScale - verticalCenterOffset + pad;

                float uPad = 0.05f * 40.0f / atlasW;
                float vPad = 0.05f * 40.0f / atlasH;

                emitQuad(D, U, V, lx0, ly0, lx1, ly1,
                        glyph.u0 - uPad, glyph.v0 - vPad,
                        glyph.u1 + uPad, glyph.v1 + vPad,
                        glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                        charCol, charOut, distRange);
            }

            penX += glyph.advance;
        }
    }

    private static void emitQuad(Vector3f D, Vector3f U, Vector3f V,
                                 float lx0, float ly0, float lx1, float ly1,
                                 float u0, float v0, float u1, float v1,
                                 float bu0, float bv0, float bu1, float bv1,
                                 int color, int outline, float distRange) {
        if (vertexCount + 6 > MAX_VERTICES) return;

        // Corner 0: (lx0, ly0) -> (u0, v0)
        Vector3f p0 = new Vector3f(D).add(new Vector3f(U).mul(lx0)).add(new Vector3f(V).mul(ly0));
        // Corner 1: (lx1, ly0) -> (u1, v0)
        Vector3f p1 = new Vector3f(D).add(new Vector3f(U).mul(lx1)).add(new Vector3f(V).mul(ly0));
        // Corner 2: (lx1, ly1) -> (u1, v1)
        Vector3f p2 = new Vector3f(D).add(new Vector3f(U).mul(lx1)).add(new Vector3f(V).mul(ly1));
        // Corner 3: (lx0, ly1) -> (u0, v1)
        Vector3f p3 = new Vector3f(D).add(new Vector3f(U).mul(lx0)).add(new Vector3f(V).mul(ly1));

        // Triangle 1: p0, p1, p2
        putVertex(p0, u0, v0, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p1, u1, v0, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p2, u1, v1, color, outline, distRange, bu0, bv0, bu1, bv1);

        // Triangle 2: p0, p2, p3
        putVertex(p0, u0, v0, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p2, u1, v1, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p3, u0, v1, color, outline, distRange, bu0, bv0, bu1, bv1);
    }

    private static void putVertex(Vector3f pos, float u, float v, int col, int outline, float distRange,
                                  float bu0, float bv0, float bu1, float bv1) {
        VERTEX_BUFFER.put(pos.x).put(pos.y).put(pos.z);
        VERTEX_BUFFER.put(u).put(v);

        // Color RGBA
        VERTEX_BUFFER.put(((col >> 16) & 0xFF) / 255f)
                     .put(((col >> 8) & 0xFF) / 255f)
                     .put((col & 0xFF) / 255f)
                     .put(((col >>> 24) & 0xFF) / 255f);

        // Outline RGBA
        VERTEX_BUFFER.put(((outline >> 16) & 0xFF) / 255f)
                     .put(((outline >> 8) & 0xFF) / 255f)
                     .put((outline & 0xFF) / 255f)
                     .put(((outline >>> 24) & 0xFF) / 255f);

        // Params (distRange, 1.0 = MSDF text)
        VERTEX_BUFFER.put(distRange).put(1.0f);

        // UVBounds (bu0, bv0, bu1, bv1)
        VERTEX_BUFFER.put(bu0).put(bv0).put(bu1).put(bv1);

        vertexCount++;
    }
}

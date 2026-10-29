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
import java.util.ArrayList;
import java.util.List;

import static org.lwjgl.opengl.GL33C.*;

/**
 * High-performance MSDF Onest 700 (Bold) 3D world renderer for in-world karaoke (W6d):
 * - ZERO shadow, ZERO outline, ZERO halo: pure crisp MSDF subpixel edge
 * - Onest 700 (Bold) at 64px, distanceRange 8.0, with font GPOS kerning and -0.01 em spacing
 * - Adaptive light/dark background luminance sensing (sampled strictly 1 time per >= 4 game ticks)
 * - Single batched draw call per frame for all active and falling letters
 * - Falling letters rendered FIRST (behind active line)
 * - Un-sung words are NOT shown in advance (appear only upon singing onset)
 * - Per-letter Apple Spring pop-in (response 0.32s, damping 0.62)
 * - Falling letters screen intersection with active line causes alpha -> 0.25
 */
public final class KaraokeWorldRenderer {

    // Stride: 3 pos + 2 uv + 4 col + 4 outline + 2 params + 4 uvBounds = 19 floats
    private static final int STRIDE = 19;
    private static final int MAX_VERTICES = 16384;
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
    private static Object lastSampledWorld = null;
    private static long lastSampleTick = -1L;
    private static long lastSampleTimeMs = 0L;
    private static long lastFrameTimeMs = 0L;
    private static boolean isLightBackground = false;
    private static float lightProgress = 0.0f; // 0.0 = dark, 1.0 = light (300 ms smooth transition)
    private static float lastSampledLuminance = 0.0f;
    private static int luminanceSampleCallCount = 0;

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

    public static int getLuminanceSampleCallCount() {
        return luminanceSampleCallCount;
    }

    /**
     * Strict rate limit gate for background luminance sampling:
     * - When world is active: strictly once per >= 4 game ticks. NO wall-time bypass during pause / low TPS.
     * - Resets on world transition or time rewind.
     * - Fallback to 50ms tick-equivalent only when world is null (headless).
     */
    public static boolean checkAndTickSampleGate(Object worldRef, long currentTick, long nowMs) {
        if (worldRef != null) {
            if (lastSampledWorld != worldRef || currentTick < lastSampleTick) {
                lastSampleTick = -1L;
                lastSampledWorld = worldRef;
            }
            if (lastSampleTick < 0 || (currentTick - lastSampleTick >= 4L)) {
                lastSampleTick = currentTick;
                lastSampleTimeMs = nowMs;
                lastSampledWorld = worldRef;
                luminanceSampleCallCount++;
                return true;
            }
            return false;
        } else {
            long fallbackTick = nowMs / 50L;
            if (lastSampleTick < 0 || (fallbackTick - lastSampleTick >= 4L)) {
                lastSampleTick = fallbackTick;
                lastSampleTimeMs = nowMs;
                luminanceSampleCallCount++;
                return true;
            }
            return false;
        }
    }

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
        lastSampledWorld = null;
        lastSampleTick = -1L;
        lastSampleTimeMs = 0L;
        lastFrameTimeMs = 0L;
        isLightBackground = false;
        lightProgress = 0.0f;
        lastSampledLuminance = 0.0f;
        luminanceSampleCallCount = 0;
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
            if (glGetProgrami(program, GL_LINK_STATUS) == GL_FALSE) {
                String error = glGetProgramInfoLog(program);
                throw new IllegalStateException("Program link error in karaoke_world: " + error);
            }

            projUniform = glGetUniformLocation(program, "Projection");
            mvUniform = glGetUniformLocation(program, "ModelView");
            fontUniform = glGetUniformLocation(program, "Font");

            vao = glGenVertexArrays();
            vbo = glGenBuffers();

            glBindVertexArray(vao);
            glBindBuffer(GL_ARRAY_BUFFER, vbo);
            glBufferData(GL_ARRAY_BUFFER, (long) MAX_VERTICES * STRIDE * 4L, GL_DYNAMIC_DRAW);

            int strideBytes = STRIDE * 4;
            glEnableVertexAttribArray(0);
            glVertexAttribPointer(0, 3, GL_FLOAT, false, strideBytes, 0L);

            glEnableVertexAttribArray(1);
            glVertexAttribPointer(1, 2, GL_FLOAT, false, strideBytes, 3L * 4L);

            glEnableVertexAttribArray(2);
            glVertexAttribPointer(2, 4, GL_FLOAT, false, strideBytes, 5L * 4L);

            glEnableVertexAttribArray(3);
            glVertexAttribPointer(3, 4, GL_FLOAT, false, strideBytes, 9L * 4L);

            glEnableVertexAttribArray(4);
            glVertexAttribPointer(4, 2, GL_FLOAT, false, strideBytes, 13L * 4L);

            glEnableVertexAttribArray(5);
            glVertexAttribPointer(5, 4, GL_FLOAT, false, strideBytes, 15L * 4L);

            glBindBuffer(GL_ARRAY_BUFFER, 0);
            glBindVertexArray(0);

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
                                   KaraokePhysics physics,
                                   long playbackMs,
                                   float tickDelta,
                                   World world) {
        if (!initialized && !failed) {
            initialize();
        }
        if (!initialized || failed) return;
        if (activeAnchors.isEmpty() && (physics == null || physics.getBodyCount() == 0)) return;

        long startTime = System.nanoTime();

        // 0. Update background luminance (sampled strictly 1 time per 4 ticks, 0 new FBOs)
        updateBackgroundLuminance(viewMatrix, projMatrix, camera, activeAnchors, world);

        VERTEX_BUFFER.clear();
        vertexCount = 0;

        Vec3d camPos = camera.getPos();

        // Basis vectors in camera-relative space derived directly from viewMatrix:
        Vector3f billboardU = new Vector3f(viewMatrix.m00(), viewMatrix.m10(), viewMatrix.m20());
        Vector3f billboardV = new Vector3f(-viewMatrix.m01(), -viewMatrix.m11(), -viewMatrix.m21());

        int fbWidth = 1920;
        int fbHeight = 1080;
        net.minecraft.client.MinecraftClient mc = net.minecraft.client.MinecraftClient.getInstance();
        if (mc != null) {
            if (mc.getFramebuffer() != null && mc.getFramebuffer().textureWidth > 10) {
                fbWidth = mc.getFramebuffer().textureWidth;
                fbHeight = mc.getFramebuffer().textureHeight;
            } else if (mc.getWindow() != null) {
                fbWidth = mc.getWindow().getFramebufferWidth();
                fbHeight = mc.getWindow().getFramebufferHeight();
            }
        }

        // Screen bounding rectangles of active singing words
        List<WordPlacement.WordRect> activeRects = new ArrayList<>();
        for (WordPlacement.WordAnchor anchor : activeAnchors) {
            if (anchor.word != null && playbackMs >= anchor.word.t) {
                WordPlacement.WordRect r = WordPlacement.projectWordToScreen(anchor, viewMatrix, projMatrix, fbWidth, fbHeight, camPos);
                if (r != null) {
                    activeRects.add(r);
                }
            }
        }

        // 1. Emit physical falling letters FIRST (W6d: rendered behind active current line)
        if (physics != null) {
            int sungColor = WordAnimation.getSungWordColor(lightProgress);

            for (KaraokePhysics.PhysicalLetter pl : physics.getLetters()) {
                Vec3d interpPos = pl.getInterpolatedPos(tickDelta);

                // Project falling letter into screen rectangle to check line intersection
                WordPlacement.WordRect letRect = WordPlacement.projectBoxToScreen(
                        interpPos,
                        pl.baseSize * 0.5f,
                        pl.baseSize * 0.5f,
                        viewMatrix, projMatrix, fbWidth, fbHeight, camPos
                );

                boolean isIntersecting = false;
                if (letRect != null && !activeRects.isEmpty()) {
                    for (WordPlacement.WordRect ar : activeRects) {
                        if (letRect.intersects(ar)) {
                            isIntersecting = true;
                            break;
                        }
                    }
                }
                pl.isOccluded = isIntersecting;

                float alpha = pl.getAlpha();
                if (alpha <= 0.01f || pl.expired) continue;

                int col = WordAnimation.applyAlpha(sungColor, alpha);

                // Letter 3D rotation
                Quaternionf rot = new Quaternionf()
                        .rotateY((float) Math.toRadians(-pl.getInterpolatedYaw(tickDelta)))
                        .rotateX((float) Math.toRadians(pl.getInterpolatedPitch(tickDelta)))
                        .rotateZ((float) Math.toRadians(pl.getInterpolatedRoll(tickDelta)));

                Vector3f letU = new Vector3f(1, 0, 0);
                Vector3f letV = new Vector3f(0, -1, 0);
                rot.transform(letU);
                rot.transform(letV);

                emitSingleLetter(pl.ch, interpPos, camPos, letU, letV, col, pl.getScale());
            }

            // Legacy physical words (if any)
            for (KaraokePhysics.PhysicalWord pw : physics.getBodies()) {
                Vec3d interpPos = pw.getInterpolatedPos(tickDelta);
                float alpha = pw.getAlpha();
                if (alpha <= 0.01f || pw.expired) continue;
                int col = WordAnimation.applyAlpha(sungColor, alpha);
                Vector3f pwU = new Vector3f(billboardU);
                Vector3f pwV = new Vector3f(billboardV);
                emitString(pw.text, interpPos, camPos, pwU, pwV, col, 10000L, KaraokeConfig.get().getSize() * WordPlacement.BASE_FONT_SCALE);
            }
        }

        // 2. Emit active line letters SECOND (rendered ON TOP of falling letters, zero Z-fighting)
        for (WordPlacement.WordAnchor anchor : activeAnchors) {
            KaraokeModel.KaraokeWord kw = anchor.word;
            if (kw == null || kw.w == null || kw.w.isEmpty()) continue;

            // W6d: un-sung words of line are NOT shown in advance!
            if (playbackMs < kw.t) continue;

            int wordColor = WordAnimation.getLetterColor(playbackMs, kw.t, kw.d, lightProgress);
            float fontScale = anchor.height > 0.01f ? (anchor.height / WordPlacement.FONT_LINE_HEIGHT_EM) : KaraokeConfig.get().getSize() * WordPlacement.BASE_FONT_SCALE;
            // Return 1: Capital letter height strictly >= 28px on frame (target 33-34px)
            if (camPos != null && anchor.position != null) {
                double distToEye = camPos.distanceTo(anchor.position);
                float minFontScale = (float) (33.0 * distToEye / 650.7);
                if (fontScale < minFontScale && KaraokeConfig.get().getSize() <= 1.25f) {
                    fontScale = minFontScale;
                }
            }

            float totalW = AuraRenderer.textWidth(kw.w, fontScale, 700);
            float penX = -totalW * 0.5f;

            for (int i = 0; i < kw.w.length(); i++) {
                char ch = kw.w.charAt(i);
                var glyph = AuraRenderer.getGlyph(700, ch);
                float adv = glyph != null ? glyph.advance * fontScale : 0.5f * fontScale;

                if (i > 0) {
                    penX -= 0.01f * fontScale; // Inter-letter spacing -0.01 em
                    penX += AuraRenderer.getKerning(kw.w.charAt(i - 1), ch) * fontScale; // Font GPOS kerning
                }

                // If already detached as falling physics letter, do not draw on line
                if (anchor.lettersDetached == null || !anchor.lettersDetached[i]) {
                    long stepMs = WordAnimation.getLetterStepMs(kw.d, kw.w.length());
                    long charStartMs = kw.t + i * stepMs;

                    if (playbackMs >= charStartMs) {
                        float letScale = WordAnimation.letterScale(playbackMs, kw.t, kw.d, kw.w.length(), i) * fontScale;
                        float letAlpha = WordAnimation.letterAlpha(playbackMs, kw.t, kw.d, kw.w.length(), i);
                        float yOff = WordAnimation.letterYOffset(playbackMs, kw.t, kw.d, kw.w.length(), i);
                        float rotZ = WordAnimation.letterRotZ(playbackMs, kw.t, kw.d, kw.w.length(), i);

                        if (letAlpha > 0.01f && letScale > 0.001f) {
                            float charCenterOffsetX = penX + adv * 0.5f;
                            Vec3d letBasePos = anchor.position
                                    .add(billboardU.x * charCenterOffsetX, 0.0, billboardU.z * charCenterOffsetX)
                                    .add(billboardV.x * (-yOff), -yOff, billboardV.z * (-yOff));

                            // Rotate U and V by rotZ in billboard plane
                            float radZ = (float) Math.toRadians(rotZ);
                            float cosZ = (float) Math.cos(radZ);
                            float sinZ = (float) Math.sin(radZ);

                            Vector3f rotU = new Vector3f(billboardU).mul(cosZ).add(new Vector3f(billboardV).mul(sinZ));
                            Vector3f rotV = new Vector3f(billboardU).mul(-sinZ).add(new Vector3f(billboardV).mul(cosZ));

                            int letCol = WordAnimation.applyAlpha(wordColor, letAlpha);
                            emitSingleLetter(ch, letBasePos, camPos, rotU, rotV, letCol, letScale);
                        }
                    }
                }

                penX += adv;
            }
        }

        if (vertexCount == 0) return;

        // OpenGL rendering: single draw call
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

    // Backward-compatible overload
    public static void renderBatch(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera,
                                   List<WordPlacement.WordAnchor> activeAnchors,
                                   List<KaraokePhysics.PhysicalWord> physicalWords,
                                   long playbackMs,
                                   float tickDelta,
                                   World world) {
        KaraokePhysics dummy = new KaraokePhysics();
        if (physicalWords != null) {
            for (KaraokePhysics.PhysicalWord pw : physicalWords) {
                dummy.spawnBody(pw.text, new Vec3d(pw.x, pw.y, pw.z), 1.0f, 0.4f, pw.yaw, pw.pitch);
            }
        }
        renderBatch(viewMatrix, projMatrix, camera, activeAnchors, dummy, playbackMs, tickDelta, world);
    }

    private static void updateBackgroundLuminance(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera,
                                                  List<WordPlacement.WordAnchor> activeAnchors, World world) {
        long nowMs = System.currentTimeMillis();
        long currentTick = (world != null) ? world.getTime() : (nowMs / 50L);

        // Strict rate limit gate: once per >= 4 game ticks when world is active (no wall-time bypass)
        if (checkAndTickSampleGate(world, currentTick, nowMs)) {
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

                        if (clip.w > 0.001f) {
                            float ndcX = clip.x / clip.w;
                            float ndcY = clip.y / clip.w;
                            sampleX = (int) Math.max(0, Math.min(fbWidth - 4, (ndcX + 1.0f) * 0.5f * fbWidth));
                            sampleY = (int) Math.max(0, Math.min(fbHeight - 4, (ndcY + 1.0f) * 0.5f * fbHeight));
                        }
                    }

                    SAMPLE_BUF.clear();
                    glReadPixels(sampleX, sampleY, 4, 4, GL_RGBA, GL_UNSIGNED_BYTE, SAMPLE_BUF);

                    float totalLuma = 0.0f;
                    int count = 0;
                    while (SAMPLE_BUF.hasRemaining() && count < 16) {
                        int r = SAMPLE_BUF.get() & 0xFF;
                        int g = SAMPLE_BUF.get() & 0xFF;
                        int b = SAMPLE_BUF.get() & 0xFF;
                        SAMPLE_BUF.get(); // skip A
                        // Rec. 709 relative luminance
                        totalLuma += (0.2126f * r + 0.7152f * g + 0.0722f * b) / 255.0f;
                        count++;
                    }
                    if (count > 0) {
                        return totalLuma / count;
                    }
                }
            }
        } catch (Throwable ignored) {
        }

        // Fallback: block light and sky angle heuristic
        if (world != null && camera != null) {
            try {
                BlockPos pos = camera.getBlockPos();
                int blockLight = world.getLightLevel(LightType.BLOCK, pos);
                int skyLight = world.getLightLevel(LightType.SKY, pos);
                float skyAngle = world.getSkyAngle(1.0f);
                float sunFactor = (float) Math.max(0.0, Math.cos(skyAngle * Math.PI * 2.0));
                float effectiveLight = (blockLight / 15.0f) * 0.4f + (skyLight / 15.0f) * sunFactor * 0.6f;
                return effectiveLight;
            } catch (Throwable ignored) {
            }
        }
        return 0.2f;
    }

    private static void emitSingleLetter(char ch, Vec3d worldPos, Vec3d camPos,
                                         Vector3f U, Vector3f V,
                                         int color, float scale) {
        var glyph = AuraRenderer.getGlyph(700, ch);
        if (glyph == null || !glyph.visible) return;

        Vector3f D = new Vector3f(
                (float) (worldPos.x - camPos.x),
                (float) (worldPos.y - camPos.y),
                (float) (worldPos.z - camPos.z)
        );

        float distRange = AuraRenderer.distanceRange();
        float ascender = AuraRenderer.fontAscender();
        float verticalCenterOffset = 0.726f * scale;

        float halfW = (glyph.right - glyph.left) * 0.5f;
        float lx0 = (glyph.left - halfW) * scale;
        float lx1 = (glyph.right - halfW) * scale;
        float ly0 = (-ascender + glyph.top) * scale - verticalCenterOffset;
        float ly1 = (-ascender + glyph.bottom) * scale - verticalCenterOffset;

        emitQuad(D, U, V, lx0, ly0, lx1, ly1,
                glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                color, 0, distRange);
    }

    private static void emitString(String text, Vec3d worldPos, Vec3d camPos,
                                   Vector3f U, Vector3f V,
                                   int color, long wordElapsedMs, float scale) {
        if (text == null || text.isEmpty()) return;

        Vector3f D = new Vector3f(
                (float) (worldPos.x - camPos.x),
                (float) (worldPos.y - camPos.y),
                (float) (worldPos.z - camPos.z)
        );

        float totalW = AuraRenderer.textWidth(text, scale, 700);
        float penX = -totalW * 0.5f;
        float distRange = AuraRenderer.distanceRange();
        float ascender = AuraRenderer.fontAscender();
        float verticalCenterOffset = 0.726f * scale;

        for (int i = 0; i < text.length(); i++) {
            char ch = text.charAt(i);
            var glyph = AuraRenderer.getGlyph(700, ch);
            if (glyph == null) {
                penX += 0.5f * scale;
                continue;
            }

            if (i > 0) {
                penX -= 0.01f * scale;
                penX += AuraRenderer.getKerning(text.charAt(i - 1), ch) * scale;
            }

            if (glyph.visible) {
                float lx0 = (penX + glyph.left) * scale;
                float lx1 = (penX + glyph.right) * scale;
                float ly0 = (-ascender + glyph.top) * scale - verticalCenterOffset;
                float ly1 = (-ascender + glyph.bottom) * scale - verticalCenterOffset;

                emitQuad(D, U, V, lx0, ly0, lx1, ly1,
                        glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                        glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                        color, 0, distRange);
            }

            penX += glyph.advance * scale;
        }
    }

    private static void emitQuad(Vector3f D, Vector3f U, Vector3f V,
                                 float lx0, float ly0, float lx1, float ly1,
                                 float u0, float v0, float u1, float v1,
                                 float bu0, float bv0, float bu1, float bv1,
                                 int color, int outline, float distRange) {
        if (vertexCount + 6 > MAX_VERTICES) return;

        Vector3f p0 = new Vector3f(D).add(new Vector3f(U).mul(lx0)).add(new Vector3f(V).mul(ly0));
        Vector3f p1 = new Vector3f(D).add(new Vector3f(U).mul(lx1)).add(new Vector3f(V).mul(ly0));
        Vector3f p2 = new Vector3f(D).add(new Vector3f(U).mul(lx1)).add(new Vector3f(V).mul(ly1));
        Vector3f p3 = new Vector3f(D).add(new Vector3f(U).mul(lx0)).add(new Vector3f(V).mul(ly1));

        // Triangle 1
        putVertex(p0, u0, v0, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p1, u1, v0, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p2, u1, v1, color, outline, distRange, bu0, bv0, bu1, bv1);

        // Triangle 2
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

        // Outline RGBA (zero)
        VERTEX_BUFFER.put(0.0f).put(0.0f).put(0.0f).put(0.0f);

        // Params: x = distanceRange, y = isMsdf
        VERTEX_BUFFER.put(distRange).put(1.0f);

        // UV Bounds
        VERTEX_BUFFER.put(bu0).put(bv0).put(bu1).put(bv1);

        vertexCount++;
    }
}

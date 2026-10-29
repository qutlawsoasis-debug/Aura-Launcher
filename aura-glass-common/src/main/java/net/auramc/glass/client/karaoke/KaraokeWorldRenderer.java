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
import java.util.Collections;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

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

    // Preallocated buffers for zero-allocation rendering hot-path
    public static boolean headlessMode = false;
    private static final Vector3f BILLBOARD_U = new Vector3f();
    private static final Vector3f BILLBOARD_V = new Vector3f();
    private static final Vector3f ACTIVE_ROT_U = new Vector3f();
    private static final Vector3f ACTIVE_ROT_V = new Vector3f();
    private static final Quaternionf SCRATCH_QUAT = new Quaternionf();
    private static final Vector4f SCRATCH_CAM_PT = new Vector4f();
    private static final Vector4f SCRATCH_CLIP_PT = new Vector4f();
    private static final Vector3f SCRATCH_LET_U = new Vector3f();
    private static final Vector3f SCRATCH_LET_V = new Vector3f();
    private static final WordPlacement.WordRect[] ACTIVE_RECT_POOL = new WordPlacement.WordRect[32];
    static {
        for (int i = 0; i < ACTIVE_RECT_POOL.length; i++) {
            ACTIVE_RECT_POOL[i] = new WordPlacement.WordRect(0, 0, 0, 0);
        }
    }
    private static int activeRectCount = 0;
    private static final WordPlacement.WordRect SCRATCH_LETTER_RECT = new WordPlacement.WordRect(0, 0, 0, 0);
    private static final List<WordPlacement.WordAnchor> LINE0_ANCHORS = new ArrayList<>(16);
    private static final List<WordPlacement.WordAnchor> LINE1_ANCHORS = new ArrayList<>(16);
    private static final int[] SAMPLE_POINTS_X = new int[3];
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

    // Per-line tone state (W6d Task 4)
    public static final class LineToneState {
        public boolean isLight = false;
        public float lightProgress = 0.0f;
        public float lastSampledLuma = 0.0f;
        public long lastFrameTimeMs = 0L;

        public void updateTransition(long nowMs) {
            if (lastFrameTimeMs <= 0L) {
                lastFrameTimeMs = nowMs;
                lightProgress = isLight ? 1.0f : 0.0f;
                return;
            }
            float dt = Math.max(0.0f, Math.min(0.5f, (nowMs - lastFrameTimeMs) / 1000.0f));
            lastFrameTimeMs = nowMs;

            float target = isLight ? 1.0f : 0.0f;
            if (dt > 0.0f) {
                float step = dt / 0.300f; // Exactly 300 ms for full transition
                if (target > lightProgress) {
                    lightProgress = Math.min(target, lightProgress + step);
                } else if (target < lightProgress) {
                    lightProgress = Math.max(target, lightProgress - step);
                }
            }
        }
    }

    private static final LineToneState[] LINE_TONES = new LineToneState[8];
    static {
        for (int i = 0; i < LINE_TONES.length; i++) {
            LINE_TONES[i] = new LineToneState();
        }
    }

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

    public static LineToneState getLineTone(int lineIndex) {
        if (lineIndex >= 0 && lineIndex < LINE_TONES.length) {
            return LINE_TONES[lineIndex];
        }
        return LINE_TONES[0];
    }

    public static boolean isLightBackground(int lineIndex) {
        return getLineTone(lineIndex).isLight;
    }

    public static float getLightProgress(int lineIndex) {
        return getLineTone(lineIndex).lightProgress;
    }

    public static float getLastSampledLuminance(int lineIndex) {
        return getLineTone(lineIndex).lastSampledLuma;
    }

    // Backward-compatible global getters (reflect line 0)
    public static boolean isLightBackground() {
        return getLineTone(0).isLight;
    }

    public static float getLightProgress() {
        return getLineTone(0).lightProgress;
    }

    public static float getLastSampledLuminance() {
        return getLineTone(0).lastSampledLuma;
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
        setScriptedLuminance(0, luminance, timeMs);
    }

    public static void setScriptedLuminance(int lineIndex, float luminance, long timeMs) {
        LineToneState state = getLineTone(lineIndex);
        state.lastSampledLuma = luminance;
        if (luminance > 0.60f) {
            state.isLight = true;
        } else if (luminance < 0.50f) {
            state.isLight = false;
        }
        state.lastFrameTimeMs = timeMs;

        if (lineIndex == 0) {
            lastSampledLuminance = state.lastSampledLuma;
            isLightBackground = state.isLight;
            lastFrameTimeMs = timeMs;
        }
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
        for (int i = 0; i < LINE_TONES.length; i++) {
            LINE_TONES[i].isLight = false;
            LINE_TONES[i].lightProgress = 0.0f;
            LINE_TONES[i].lastSampledLuma = 0.0f;
            LINE_TONES[i].lastFrameTimeMs = 0L;
        }
    }

    public static void updateTransition(long nowMs) {
        for (int i = 0; i < LINE_TONES.length; i++) {
            LINE_TONES[i].updateTransition(nowMs);
        }
        LineToneState l0 = LINE_TONES[0];
        lightProgress = l0.lightProgress;
        isLightBackground = l0.isLight;
        lastFrameTimeMs = nowMs;
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
            if (headlessMode) {
                initialized = true;
            } else {
                initialize();
            }
        }
        if (!initialized || failed) return;
        if (activeAnchors.isEmpty() && (physics == null || physics.getBodyCount() == 0)) return;

        long startTime = System.nanoTime();

        // 0. Update background luminance (sampled strictly 1 time per 4 ticks, 0 new FBOs)
        updateBackgroundLuminance(viewMatrix, projMatrix, camera, activeAnchors, world);

        VERTEX_BUFFER.clear();
        vertexCount = 0;

        Vec3d camPos = camera != null ? camera.getPos() : null;
        double camX = camPos != null ? camPos.x : 0.0;
        double camY = camPos != null ? camPos.y : 0.0;
        double camZ = camPos != null ? camPos.z : 0.0;

        // Basis vectors in camera-relative space derived directly from viewMatrix:
        BILLBOARD_U.set(viewMatrix.m00(), viewMatrix.m10(), viewMatrix.m20());
        BILLBOARD_V.set(-viewMatrix.m01(), -viewMatrix.m11(), -viewMatrix.m21());

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

        // Screen bounding rectangles of active singing words (zero allocation)
        activeRectCount = 0;
        for (int i = 0; i < activeAnchors.size(); i++) {
            WordPlacement.WordAnchor anchor = activeAnchors.get(i);
            if (anchor.word != null && playbackMs >= anchor.word.t && activeRectCount < ACTIVE_RECT_POOL.length) {
                if (WordPlacement.projectWordToScreen(anchor, viewMatrix, projMatrix, fbWidth, fbHeight, camX, camY, camZ, ACTIVE_RECT_POOL[activeRectCount])) {
                    activeRectCount++;
                }
            }
        }

        // 1. Emit physical falling letters FIRST (W6d: rendered behind active current line)
        if (physics != null) {
            int sungColor = WordAnimation.getSungWordColor(lightProgress);
            List<KaraokePhysics.PhysicalLetter> letters = physics.getLetters();

            for (int li = 0; li < letters.size(); li++) {
                KaraokePhysics.PhysicalLetter pl = letters.get(li);
                double interpX = pl.getInterpX(tickDelta);
                double interpY = pl.getInterpY(tickDelta);
                double interpZ = pl.getInterpZ(tickDelta);

                // Project falling letter into screen rectangle to check line intersection
                boolean hasRect = WordPlacement.projectBoxToScreen(
                        interpX, interpY, interpZ,
                        pl.baseSize * 0.5f,
                        pl.baseSize * 0.5f,
                        viewMatrix, projMatrix, fbWidth, fbHeight, camX, camY, camZ,
                        SCRATCH_LETTER_RECT
                );

                boolean isIntersecting = false;
                if (hasRect && activeRectCount > 0) {
                    for (int r = 0; r < activeRectCount; r++) {
                        if (SCRATCH_LETTER_RECT.intersects(ACTIVE_RECT_POOL[r])) {
                            isIntersecting = true;
                            break;
                        }
                    }
                }
                pl.isOccluded = isIntersecting;

                float alpha = pl.getAlpha();
                if (alpha <= 0.01f || pl.expired) continue;

                int col = WordAnimation.applyAlpha(sungColor, alpha);

                // Letter 3D rotation (reusable quaternion & vectors: zero allocation)
                SCRATCH_QUAT.identity()
                        .rotateY((float) Math.toRadians(-pl.getInterpolatedYaw(tickDelta)))
                        .rotateX((float) Math.toRadians(pl.getInterpolatedPitch(tickDelta)))
                        .rotateZ((float) Math.toRadians(pl.getInterpolatedRoll(tickDelta)));

                SCRATCH_LET_U.set(1.0f, 0.0f, 0.0f);
                SCRATCH_LET_V.set(0.0f, -1.0f, 0.0f);
                SCRATCH_QUAT.transform(SCRATCH_LET_U);
                SCRATCH_QUAT.transform(SCRATCH_LET_V);

                emitSingleLetter(pl.ch, interpX, interpY, interpZ, camX, camY, camZ, SCRATCH_LET_U, SCRATCH_LET_V, col, pl.getScale());
            }

            // Legacy physical words (if any)
            List<KaraokePhysics.PhysicalWord> bodies = physics.getBodies();
            for (int bi = 0; bi < bodies.size(); bi++) {
                KaraokePhysics.PhysicalWord pw = bodies.get(bi);
                double interpX = pw.prevX + (pw.x - pw.prevX) * tickDelta;
                double interpY = pw.prevY + (pw.y - pw.prevY) * tickDelta;
                double interpZ = pw.prevZ + (pw.z - pw.prevZ) * tickDelta;
                float alpha = pw.getAlpha();
                if (alpha <= 0.01f || pw.expired) continue;
                int col = WordAnimation.applyAlpha(sungColor, alpha);
                emitString(pw.text, interpX, interpY, interpZ, camX, camY, camZ, BILLBOARD_U, BILLBOARD_V, col, 10000L, KaraokeConfig.get().getSize() * WordPlacement.BASE_FONT_SCALE);
            }
        }

        // 2. Emit active line letters SECOND (rendered ON TOP of falling letters, zero Z-fighting)
        for (int ai = 0; ai < activeAnchors.size(); ai++) {
            WordPlacement.WordAnchor anchor = activeAnchors.get(ai);
            KaraokeModel.KaraokeWord kw = anchor.word;
            if (kw == null || kw.w == null || kw.w.isEmpty()) continue;

            // W6d: un-sung words of line are NOT shown in advance!
            if (playbackMs < kw.t) continue;

            float lineProgress = getLightProgress(anchor.lineIndex);
            int wordColor = WordAnimation.getLetterColor(playbackMs, kw.t, kw.d, lineProgress);
            float fontScale = anchor.height > 0.01f ? (anchor.height / WordPlacement.FONT_LINE_HEIGHT_EM) : (float) (35.0 * (camPos != null && anchor.position != null ? camPos.distanceTo(anchor.position) : WordPlacement.TARGET_DISTANCE) / 650.7) * KaraokeConfig.get().getSize();

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
                            double letX = anchor.position.x + BILLBOARD_U.x * charCenterOffsetX + BILLBOARD_V.x * (-yOff);
                            double letY = anchor.position.y - yOff;
                            double letZ = anchor.position.z + BILLBOARD_U.z * charCenterOffsetX + BILLBOARD_V.z * (-yOff);

                            // Rotate U and V by rotZ in billboard plane
                            float radZ = (float) Math.toRadians(rotZ);
                            float cosZ = (float) Math.cos(radZ);
                            float sinZ = (float) Math.sin(radZ);

                            ACTIVE_ROT_U.set(
                                    BILLBOARD_U.x * cosZ + BILLBOARD_V.x * sinZ,
                                    BILLBOARD_U.y * cosZ + BILLBOARD_V.y * sinZ,
                                    BILLBOARD_U.z * cosZ + BILLBOARD_V.z * sinZ
                            );
                            ACTIVE_ROT_V.set(
                                    BILLBOARD_U.x * (-sinZ) + BILLBOARD_V.x * cosZ,
                                    BILLBOARD_U.y * (-sinZ) + BILLBOARD_V.y * cosZ,
                                    BILLBOARD_U.z * (-sinZ) + BILLBOARD_V.z * cosZ
                            );

                            int letCol = WordAnimation.applyAlpha(wordColor, letAlpha);
                            emitSingleLetter(ch, letX, letY, letZ, camX, camY, camZ, ACTIVE_ROT_U, ACTIVE_ROT_V, letCol, letScale);
                        }
                    }
                }

                penX += adv;
            }
        }

        if (vertexCount == 0 || headlessMode || program == 0) {
            lastRenderTimeMs = (System.nanoTime() - startTime) / 1_000_000.0;
            return;
        }

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

        // Strict rate limit gate: once per >= 4 game ticks per group (no wall-time bypass)
        if (checkAndTickSampleGate(world, currentTick, nowMs)) {
            LINE0_ANCHORS.clear();
            LINE1_ANCHORS.clear();
            for (int i = 0; i < activeAnchors.size(); i++) {
                WordPlacement.WordAnchor a = activeAnchors.get(i);
                if (a.lineIndex == 0) {
                    LINE0_ANCHORS.add(a);
                } else {
                    LINE1_ANCHORS.add(a);
                }
            }

            if (LINE0_ANCHORS.isEmpty() && LINE1_ANCHORS.isEmpty()) {
                float sampledLuma = sampleScreenLuminanceForLine(viewMatrix, projMatrix, camera, Collections.emptyList(), world);
                LineToneState state = getLineTone(0);
                state.lastSampledLuma = sampledLuma;
                if (sampledLuma > 0.60f) {
                    state.isLight = true;
                } else if (sampledLuma < 0.50f) {
                    state.isLight = false;
                }
            } else {
                if (!LINE0_ANCHORS.isEmpty()) {
                    float sampledLuma = sampleScreenLuminanceForLine(viewMatrix, projMatrix, camera, LINE0_ANCHORS, world);
                    LineToneState state = getLineTone(0);
                    state.lastSampledLuma = sampledLuma;
                    if (sampledLuma > 0.60f) {
                        state.isLight = true;
                    } else if (sampledLuma < 0.50f) {
                        state.isLight = false;
                    }
                }
                if (!LINE1_ANCHORS.isEmpty()) {
                    float sampledLuma = sampleScreenLuminanceForLine(viewMatrix, projMatrix, camera, LINE1_ANCHORS, world);
                    LineToneState state = getLineTone(1);
                    state.lastSampledLuma = sampledLuma;
                    if (sampledLuma > 0.60f) {
                        state.isLight = true;
                    } else if (sampledLuma < 0.50f) {
                        state.isLight = false;
                    }
                }
            }

            LineToneState l0 = getLineTone(0);
            lastSampledLuminance = l0.lastSampledLuma;
            isLightBackground = l0.isLight;
        }

        // Smooth 300 ms transition updated every frame
        updateTransition(nowMs);
    }

    private static float sampleScreenLuminance(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera,
                                                List<WordPlacement.WordAnchor> activeAnchors, World world) {
        return sampleScreenLuminanceForLine(viewMatrix, projMatrix, camera, activeAnchors, world);
    }

    private static float sampleScreenLuminanceForLine(Matrix4f viewMatrix, Matrix4f projMatrix, Camera camera,
                                                      List<WordPlacement.WordAnchor> lineAnchors, World world) {
        try {
            net.minecraft.client.MinecraftClient client = net.minecraft.client.MinecraftClient.getInstance();
            if (client != null && client.getFramebuffer() != null) {
                int fbWidth = client.getFramebuffer().textureWidth;
                int fbHeight = client.getFramebuffer().textureHeight;
                if (fbWidth > 10 && fbHeight > 10) {
                    Vec3d camPos = camera != null ? camera.getPos() : null;

                    if (!lineAnchors.isEmpty() && camPos != null) {
                        int minX = fbWidth;
                        int maxX = 0;
                        int sumY = 0;
                        int projectedCount = 0;

                        for (int i = 0; i < lineAnchors.size(); i++) {
                            WordPlacement.WordAnchor a = lineAnchors.get(i);
                            if (a.position == null) continue;
                            float relX = (float) (a.position.x - camPos.x);
                            float relY = (float) (a.position.y - camPos.y);
                            float relZ = (float) (a.position.z - camPos.z);

                            viewMatrix.transform(relX, relY, relZ, 1.0f, SCRATCH_CAM_PT);
                            projMatrix.transform(SCRATCH_CAM_PT, SCRATCH_CLIP_PT);

                            if (SCRATCH_CLIP_PT.w > 0.001f) {
                                float ndcX = SCRATCH_CLIP_PT.x / SCRATCH_CLIP_PT.w;
                                float ndcY = SCRATCH_CLIP_PT.y / SCRATCH_CLIP_PT.w;
                                int sx = (int) Math.max(0, Math.min(fbWidth - 4, (ndcX + 1.0f) * 0.5f * fbWidth));
                                int sy = (int) Math.max(0, Math.min(fbHeight - 4, (ndcY + 1.0f) * 0.5f * fbHeight));

                                minX = Math.min(minX, sx);
                                maxX = Math.max(maxX, sx);
                                sumY += sy;
                                projectedCount++;
                            }
                        }

                        if (projectedCount > 0) {
                            int avgY = sumY / projectedCount;
                            int midX = (minX + maxX) / 2;
                            int leftX = (minX + midX) / 2;
                            int rightX = (midX + maxX) / 2;

                            int samplePointCount;
                            if (minX == maxX) {
                                SAMPLE_POINTS_X[0] = midX;
                                samplePointCount = 1;
                            } else {
                                SAMPLE_POINTS_X[0] = leftX;
                                SAMPLE_POINTS_X[1] = midX;
                                SAMPLE_POINTS_X[2] = rightX;
                                samplePointCount = 3;
                            }

                            float totalLuma = 0.0f;
                            int totalPixels = 0;

                            for (int sp = 0; sp < samplePointCount; sp++) {
                                int sampleX = SAMPLE_POINTS_X[sp];
                                int clampedX = Math.max(0, Math.min(fbWidth - 4, sampleX));
                                int clampedY = Math.max(0, Math.min(fbHeight - 4, avgY));

                                SAMPLE_BUF.clear();
                                glReadPixels(clampedX, clampedY, 4, 4, GL_RGBA, GL_UNSIGNED_BYTE, SAMPLE_BUF);

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
                                totalPixels += count;
                            }

                            if (totalPixels > 0) {
                                return totalLuma / totalPixels;
                            }
                        }
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

    private static void emitSingleLetter(char ch, double worldX, double worldY, double worldZ,
                                         double camX, double camY, double camZ,
                                         Vector3f U, Vector3f V,
                                         int color, float scale) {
        var glyph = AuraRenderer.getGlyph(700, ch);
        if (glyph == null || !glyph.visible) return;

        float dx = (float) (worldX - camX);
        float dy = (float) (worldY - camY);
        float dz = (float) (worldZ - camZ);

        float distRange = AuraRenderer.distanceRange();
        float ascender = AuraRenderer.fontAscender();
        float verticalCenterOffset = 0.726f * scale;

        float halfW = (glyph.right - glyph.left) * 0.5f;
        float lx0 = (glyph.left - halfW) * scale;
        float lx1 = (glyph.right - halfW) * scale;
        float ly0 = (-ascender + glyph.top) * scale - verticalCenterOffset;
        float ly1 = (-ascender + glyph.bottom) * scale - verticalCenterOffset;

        emitQuad(dx, dy, dz, U, V, lx0, ly0, lx1, ly1,
                glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                color, 0, distRange);
    }

    private static void emitString(String text, double worldX, double worldY, double worldZ,
                                   double camX, double camY, double camZ,
                                   Vector3f U, Vector3f V,
                                   int color, long wordElapsedMs, float scale) {
        if (text == null || text.isEmpty()) return;

        float dx = (float) (worldX - camX);
        float dy = (float) (worldY - camY);
        float dz = (float) (worldZ - camZ);

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

                emitQuad(dx, dy, dz, U, V, lx0, ly0, lx1, ly1,
                        glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                        glyph.u0, glyph.v0, glyph.u1, glyph.v1,
                        color, 0, distRange);
            }

            penX += glyph.advance * scale;
        }
    }

    private static void emitQuad(float dx, float dy, float dz, Vector3f U, Vector3f V,
                                 float lx0, float ly0, float lx1, float ly1,
                                 float u0, float v0, float u1, float v1,
                                 float bu0, float bv0, float bu1, float bv1,
                                 int color, int outline, float distRange) {
        if (vertexCount + 6 > MAX_VERTICES) return;

        // Scalar corner positions (zero Vector3f allocation per quad)
        float p0x = dx + U.x * lx0 + V.x * ly0;
        float p0y = dy + U.y * lx0 + V.y * ly0;
        float p0z = dz + U.z * lx0 + V.z * ly0;

        float p1x = dx + U.x * lx1 + V.x * ly0;
        float p1y = dy + U.y * lx1 + V.y * ly0;
        float p1z = dz + U.z * lx1 + V.z * ly0;

        float p2x = dx + U.x * lx1 + V.x * ly1;
        float p2y = dy + U.y * lx1 + V.y * ly1;
        float p2z = dz + U.z * lx1 + V.z * ly1;

        float p3x = dx + U.x * lx0 + V.x * ly1;
        float p3y = dy + U.y * lx0 + V.y * ly1;
        float p3z = dz + U.z * lx0 + V.z * ly1;

        // Triangle 1
        putVertex(p0x, p0y, p0z, u0, v0, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p1x, p1y, p1z, u1, v0, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p2x, p2y, p2z, u1, v1, color, outline, distRange, bu0, bv0, bu1, bv1);

        // Triangle 2
        putVertex(p0x, p0y, p0z, u0, v0, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p2x, p2y, p2z, u1, v1, color, outline, distRange, bu0, bv0, bu1, bv1);
        putVertex(p3x, p3y, p3z, u0, v1, color, outline, distRange, bu0, bv0, bu1, bv1);
    }

    private static void putVertex(float px, float py, float pz, float u, float v, int col, int outline, float distRange,
                                  float bu0, float bv0, float bu1, float bv1) {
        VERTEX_BUFFER.put(px).put(py).put(pz);
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

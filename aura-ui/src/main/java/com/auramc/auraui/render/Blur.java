package com.auramc.auraui.render;

import com.auramc.auraui.AuraUi;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.blaze3d.systems.RenderSystem;
import java.io.File;
import java.io.FileReader;
import java.io.FileWriter;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.gl.Framebuffer;
import net.minecraft.client.gl.SimpleFramebuffer;
import org.lwjgl.opengl.GL11;
import org.lwjgl.opengl.GL20;
import org.lwjgl.opengl.GL30;

public final class Blur {
    public enum Mode {
        FULL, LOW, OFF
    }

    private static Mode currentMode = Mode.FULL;
    private static boolean shaderLoaded = false;
    private static int program = 0;
    private static int uTexture = -1;
    private static int uDelta = -1;
    private static int uRadius = -1;

    private static Framebuffer passA = null;
    private static Framebuffer passB = null;

    private static final String VERTEX_SHADER = """
        #version 150
        in vec2 Position;
        out vec2 TexCoord;
        void main() {
            TexCoord = Position * 0.5 + 0.5;
            gl_Position = vec4(Position, 0.0, 1.0);
        }
        """;

    private static final String FRAGMENT_SHADER = """
        #version 150
        uniform sampler2D DiffuseSampler;
        uniform vec2 Delta;
        uniform float Radius;
        in vec2 TexCoord;
        out vec4 FragColor;

        void main() {
            vec4 col = vec4(0.0);
            float weights[5] = float[](0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);
            col += texture(DiffuseSampler, TexCoord) * weights[0];
            for (int i = 1; i < 5; i++) {
                vec2 offset = Delta * (float(i) * Radius);
                col += texture(DiffuseSampler, TexCoord + offset) * weights[i];
                col += texture(DiffuseSampler, TexCoord - offset) * weights[i];
            }
            FragColor = col;
        }
        """;

    private Blur() {}

    public static void loadConfig() {
        try {
            File configFile = new File(MinecraftClient.getInstance().runDirectory, "config/aura-ui.json");
            if (configFile.exists()) {
                try (FileReader reader = new FileReader(configFile)) {
                    JsonObject json = JsonParser.parseReader(reader).getAsJsonObject();
                    if (json.has("blur")) {
                        String b = json.get("blur").getAsString().toLowerCase();
                        if ("off".equals(b)) currentMode = Mode.OFF;
                        else if ("low".equals(b)) currentMode = Mode.LOW;
                        else currentMode = Mode.FULL;
                    }
                }
            } else {
                configFile.getParentFile().mkdirs();
                JsonObject def = new JsonObject();
                def.addProperty("blur", "full");
                try (FileWriter writer = new FileWriter(configFile)) {
                    writer.write(def.toString());
                }
            }
        } catch (Throwable t) {
            AuraUi.LOGGER.warn("[aura-ui] Could not load aura-ui.json, using default blur=full", t);
        }
    }

    public static Mode getMode() {
        return currentMode;
    }

    public static void setMode(Mode mode) {
        currentMode = mode;
    }

    private static void initShader() {
        if (shaderLoaded) return;
        try {
            int vs = GL20.glCreateShader(GL20.GL_VERTEX_SHADER);
            GL20.glShaderSource(vs, VERTEX_SHADER);
            GL20.glCompileShader(vs);
            if (GL20.glGetShaderi(vs, GL20.GL_COMPILE_STATUS) == GL11.GL_FALSE) {
                AuraUi.LOGGER.error("[aura-ui] Blur VS compile error: " + GL20.glGetShaderInfoLog(vs));
                return;
            }

            int fs = GL20.glCreateShader(GL20.GL_FRAGMENT_SHADER);
            GL20.glShaderSource(fs, FRAGMENT_SHADER);
            GL20.glCompileShader(fs);
            if (GL20.glGetShaderi(fs, GL20.GL_COMPILE_STATUS) == GL11.GL_FALSE) {
                AuraUi.LOGGER.error("[aura-ui] Blur FS compile error: " + GL20.glGetShaderInfoLog(fs));
                return;
            }

            program = GL20.glCreateProgram();
            GL20.glAttachShader(program, vs);
            GL20.glAttachShader(program, fs);
            GL20.glBindAttribLocation(program, 0, "Position");
            GL20.glLinkProgram(program);
            if (GL20.glGetProgrami(program, GL20.GL_LINK_STATUS) == GL11.GL_FALSE) {
                AuraUi.LOGGER.error("[aura-ui] Blur link error: " + GL20.glGetProgramInfoLog(program));
                return;
            }

            uTexture = GL20.glGetUniformLocation(program, "DiffuseSampler");
            uDelta = GL20.glGetUniformLocation(program, "Delta");
            uRadius = GL20.glGetUniformLocation(program, "Radius");
            shaderLoaded = true;
        } catch (Throwable t) {
            AuraUi.LOGGER.error("[aura-ui] Failed to init blur shader", t);
        }
    }

    public static void renderBlur(float radiusPx) {
        if (currentMode == Mode.OFF) return;

        MinecraftClient mc = MinecraftClient.getInstance();
        Framebuffer main = mc.getFramebuffer();
        if (main == null) return;

        int w = main.textureWidth;
        int h = main.textureHeight;
        if (w <= 0 || h <= 0) return;

        if (currentMode == Mode.LOW) {
            // Draw a subtle darkening sheet instead of multi-pass GPU blur
            return;
        }

        initShader();
        if (!shaderLoaded || program == 0) return;

        // Downscaled framebuffers for performance
        int bw = Math.max(1, w / 2);
        int bh = Math.max(1, h / 2);

        if (passA == null || passA.textureWidth != bw || passA.textureHeight != bh) {
            if (passA != null) passA.delete();
            if (passB != null) passB.delete();
            passA = new SimpleFramebuffer(bw, bh, false, false);
            passB = new SimpleFramebuffer(bw, bh, false, false);
        }

        GL11.glPushAttrib(GL11.GL_ALL_ATTRIB_BITS);
        RenderSystem.disableBlend();
        RenderSystem.disableDepthTest();

        // Copy main framebuffer downscaled to passA
        GL30.glBindFramebuffer(GL30.GL_READ_FRAMEBUFFER, main.fbo);
        GL30.glBindFramebuffer(GL30.GL_DRAW_FRAMEBUFFER, passA.fbo);
        GL30.glBlitFramebuffer(0, 0, w, h, 0, 0, bw, bh, GL11.GL_COLOR_BUFFER_BIT, GL11.GL_LINEAR);

        GL20.glUseProgram(program);
        GL20.glUniform1i(uTexture, 0);
        GL20.glUniform1f(uRadius, radiusPx * 0.5f);

        // Pass 1: Horizontal into passB
        passB.beginWrite(true);
        GL20.glUniform2f(uDelta, 1.0f / bw, 0.0f);
        RenderSystem.bindTexture(passA.getColorAttachment());
        drawQuad();

        // Pass 2: Vertical back into main framebuffer
        main.beginWrite(false);
        GL20.glUniform2f(uDelta, 0.0f, 1.0f / bh);
        RenderSystem.bindTexture(passB.getColorAttachment());
        drawQuad();

        GL20.glUseProgram(0);
        GL11.glPopAttrib();
    }

    private static void drawQuad() {
        GL11.glBegin(GL11.GL_QUADS);
        GL11.glTexCoord2f(0, 0); GL11.glVertex2f(-1, -1);
        GL11.glTexCoord2f(1, 0); GL11.glVertex2f(1, -1);
        GL11.glTexCoord2f(1, 1); GL11.glVertex2f(1, 1);
        GL11.glTexCoord2f(0, 1); GL11.glVertex2f(-1, 1);
        GL11.glEnd();
    }
}

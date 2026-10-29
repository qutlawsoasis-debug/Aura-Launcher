package net.auramc.glass.common.render;

import com.mojang.blaze3d.systems.RenderSystem;
import static org.lwjgl.opengl.GL33C.*;

final class GuiRenderState {
    private GuiRenderState() {}
    static void clearSampler(int unit) {
        var capabilities = org.lwjgl.opengl.GL.getCapabilities();
        if (capabilities.OpenGL33 || capabilities.GL_ARB_sampler_objects) glBindSampler(unit, 0);
    }
    // Called only at explicit GUI boundaries after DrawContext.draw(). Minecraft's
    // shader and vertex buffer have been unbound there; engine texture IDs are cached.
    static void restore(int framebuffer, int width, int height) {
        glBindFramebuffer(GL_FRAMEBUFFER, framebuffer); glViewport(0, 0, width, height);
        if (RenderSystem.isOnRenderThread() && RenderSystem.getShader() != null) RenderSystem.getShader().unbind();
        else glUseProgram(0);
        net.minecraft.client.gl.VertexBuffer.unbind(); glBindBuffer(GL_ARRAY_BUFFER, 0);
        glBindBuffer(GL_PIXEL_UNPACK_BUFFER, 0);
        for (int unit = 0; unit < 5; unit++) {
            com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE0 + unit); GuiRenderState.clearSampler(unit);
            com.mojang.blaze3d.platform.GlStateManager._bindTexture(RenderSystem.isOnRenderThread() ? RenderSystem.getShaderTexture(unit) : 0);
        }
        com.mojang.blaze3d.platform.GlStateManager._activeTexture(GL_TEXTURE0);
        glBlendEquationSeparate(GL_FUNC_ADD, GL_FUNC_ADD);
        glBlendFuncSeparate(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA, GL_ONE, GL_ZERO);
        glEnable(GL_DEPTH_TEST); glEnable(GL_CULL_FACE); glDisable(GL_BLEND); glDisable(GL_SCISSOR_TEST);
        glColorMask(true, true, true, true);
        if (RenderSystem.isOnRenderThread()) { RenderSystem.enableDepthTest(); RenderSystem.enableCull(); RenderSystem.disableBlend(); }
        if (RenderSystem.isOnRenderThread()) RenderSystem.defaultBlendFunc();
    }
}

package net.auramc.glass.client.mixin;

import com.mojang.blaze3d.systems.RenderSystem;
import net.auramc.glass.client.karaoke.KaraokeEngine;
import net.minecraft.client.render.Camera;
import net.minecraft.client.render.GameRenderer;
import net.minecraft.client.render.LightmapTextureManager;
import net.minecraft.client.render.RenderTickCounter;
import net.minecraft.client.render.WorldRenderer;
import org.joml.Matrix4f;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(WorldRenderer.class)
public abstract class WorldRendererMixin {
    @Inject(method = "render", at = @At("TAIL"))
    private void aura_karaokeWorldRender(
            RenderTickCounter tickCounter,
            boolean renderBlockOutline,
            Camera camera,
            GameRenderer gameRenderer,
            LightmapTextureManager lightmapTextureManager,
            Matrix4f matrix4f,
            Matrix4f matrix4f2,
            CallbackInfo ci
    ) {
        Matrix4f viewMatrix = new Matrix4f()
                .rotationX((float) Math.toRadians(camera.getPitch()))
                .rotationY((float) Math.toRadians(camera.getYaw() + 180.0f));
        Matrix4f proj = matrix4f2 != null ? matrix4f2 : RenderSystem.getProjectionMatrix();
        KaraokeEngine.getInstance().render(viewMatrix, proj, camera, tickCounter.getTickDelta(false)); net.auramc.glass.client.quickmenu.MaxFpsBenchmarkRunner.onWorldRenderFrame(net.minecraft.client.MinecraftClient.getInstance());
    }
}
